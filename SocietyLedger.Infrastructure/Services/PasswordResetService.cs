using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocietyLedger.Application.DTOs.Auth;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Shared;
using System.Security.Cryptography;
using System.Text;

namespace SocietyLedger.Infrastructure.Services
{
    public class PasswordResetService : IPasswordResetService
    {
        private readonly IUserRepository _userRepository;
        private readonly IEmailService _emailService;
        private readonly EmailSettings _emailSettings;
        private readonly PasswordHasher _passwordHasher;
        private readonly ILogger<PasswordResetService> _logger;
        private readonly AppDbContext _dbContext;

        public PasswordResetService(
            IUserRepository userRepository,
            IEmailService emailService,
            IOptions<EmailSettings> emailSettings,
            PasswordHasher passwordHasher,
            ILogger<PasswordResetService> logger,
            AppDbContext dbContext)
        {
            _userRepository = userRepository;
            _emailService = emailService;
            _emailSettings = emailSettings.Value;
            _passwordHasher = passwordHasher;
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task<bool> InitiatePasswordResetAsync(ForgotPasswordRequest request, string ipAddress)
        {
            try
            {
                // Don't reveal whether email exists in the system
                // Always return success even if email doesn't exist
                
                var user = await _userRepository.GetByEmailAsync(request.Email);
                if (user == null || !user.IsActive)
                {
                    _logger.LogInformation("Password reset requested for non-existent or inactive email: {Email} from IP: {IP}", 
                        request.Email, ipAddress);
                    return true; // Return success to avoid email enumeration
                }

                // Generate secure reset token
                var resetToken = GenerateSecureToken();
                var tokenHash = HashToken(resetToken);
                var expiresAt = DateTime.UtcNow.AddMinutes(_emailSettings.PasswordResetTokenExpiryMinutes);

                // Create password reset token entity
                var resetTokenEntity = new password_reset_token
                {
                    user_id = user.Id,
                    token_hash = tokenHash,
                    expires_at = expiresAt,
                    created_at = DateTime.UtcNow,
                    created_by_ip = ipAddress,
                    is_used = false
                };

                // Save to database
                await _dbContext.password_reset_tokens.AddAsync(resetTokenEntity);
                await _dbContext.SaveChangesAsync();

                // Send reset email
                var frontendUrl = _emailSettings.FrontendUrl.TrimEnd('/');
                var resetUrl = $"{frontendUrl}/reset-password?token={resetToken}";
                var emailSent = await _emailService.SendPasswordResetEmailAsync(user.Email!, resetToken, resetUrl);

                if (emailSent)
                {
                    _logger.LogInformation("Password reset initiated successfully for user {UserId} from IP: {IP}", 
                        user.Id, ipAddress);

                    await _dbContext.email_notification_logs.AddAsync(new Persistence.Entities.email_notification_log
                    {
                        notification_type = "password_reset",
                        recipient_email = user.Email!,
                        recipient_name = user.Name,
                        subject = "Reset Your Password - FlatLedger",
                        sent_at = DateTime.UtcNow,
                        sent_by_system = false,
                        status = "sent",
                        society_id = user.SocietyId,
                        user_id = user.Id
                    });
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    _logger.LogError("Failed to send password reset email for user {UserId}", user.Id);

                    await _dbContext.email_notification_logs.AddAsync(new Persistence.Entities.email_notification_log
                    {
                        notification_type = "password_reset",
                        recipient_email = user.Email!,
                        recipient_name = user.Name,
                        subject = "Reset Your Password - FlatLedger",
                        sent_at = DateTime.UtcNow,
                        sent_by_system = false,
                        status = "failed",
                        error_message = "Email delivery failed",
                        society_id = user.SocietyId,
                        user_id = user.Id
                    });
                    await _dbContext.SaveChangesAsync();
                }

                return true; // Always return success to avoid email enumeration
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating password reset for email: {Email}", request.Email);
                return true; // Return success to avoid email enumeration
            }
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, string ipAddress)
        {
            try
            {
                // Validate token
                var tokenHash = HashToken(request.Token);
                var resetToken = await _dbContext.password_reset_tokens
                    .Include(prt => prt.user)
                    .FirstOrDefaultAsync(prt => 
                        prt.token_hash == tokenHash && 
                        !prt.is_used && 
                        prt.expires_at > DateTime.UtcNow);

                if (resetToken == null)
                {
                    _logger.LogWarning("Invalid or expired password reset token used from IP: {IP}", ipAddress);
                    throw new AuthenticationException("Invalid or expired reset token.");
                }

                // Validate user
                if (resetToken.user == null || !resetToken.user.is_active)
                {
                    _logger.LogWarning("Password reset attempted for inactive or non-existent user from IP: {IP}", ipAddress);
                    throw new AuthenticationException("Invalid user account.");
                }

                // Validate passwords match
                if (request.NewPassword != request.ConfirmPassword)
                {
                    throw new ValidationException("New password and confirmation password do not match.");
                }

                // Update password
                var newPasswordHash = _passwordHasher.Hash(request.NewPassword);
                resetToken.user.password_hash = newPasswordHash;
                resetToken.user.force_password_change = false;
                resetToken.user.updated_at = DateTime.UtcNow;

                // Mark token as used
                resetToken.is_used = true;
                resetToken.used_at = DateTime.UtcNow;

                // Revoke all active refresh tokens after password reset so old sessions
                // cannot continue with stale credentials/claims.
                var activeRefreshTokens = await _dbContext.refresh_tokens
                    .Where(rt => rt.user_id == resetToken.user_id && !rt.is_revoked && rt.expires_at > DateTime.UtcNow)
                    .ToListAsync();

                foreach (var refreshToken in activeRefreshTokens)
                {
                    refreshToken.is_revoked = true;
                    refreshToken.revoked_at = DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Password reset completed successfully for user {UserId} from IP: {IP}", 
                    resetToken.user_id, ipAddress);

                return true;
            }
            catch (AuthenticationException)
            {
                throw;
            }
            catch (ValidationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password for token from IP: {IP}", ipAddress);
                throw new AuthenticationException("Error resetting password. Please try again.");
            }
        }

        private string GenerateSecureToken()
        {
            using var rng = RandomNumberGenerator.Create();
            var tokenBytes = new byte[32];
            rng.GetBytes(tokenBytes);
            return Convert.ToBase64String(tokenBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        private string HashToken(string token)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(token);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}