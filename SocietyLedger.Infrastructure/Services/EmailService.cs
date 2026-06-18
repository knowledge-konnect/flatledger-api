using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SocietyLedger.Application.Interfaces.Services;

namespace SocietyLedger.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;

        {
            _logger = logger;
        }

        {


        }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome email to {Email}", email);
                return false;
            }
        }

        {

        }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send subscription reminder email to {Email}", email);
                return false;
            }
        }

        {
            {
            };


        }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error sending email to {Email}", toEmail);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {Email}", toEmail);
                return false;
            }
        }

        {


        }
        private string GetPasswordResetEmailHtml(string resetLink)
        {
            var expiryMinutes = _emailSettings.PasswordResetTokenExpiryMinutes;

            var content = $@"
            <h2 style='margin-top:0;color:#111827;'>Reset Your Password</h2>

            <p style='color:#374151;line-height:1.7;'>
            We received a request to reset your FlatLedger password.
            </p>

            <p style='text-align:center;margin:32px 0;'>
            <a href='{resetLink}'
            style='background:#10B981;
            color:#ffffff !important;
            padding:14px 28px;
            text-decoration:none;
            border-radius:10px;
            font-weight:600;
            display:inline-block;'>
            Reset Password
            </a>
            </p>

            <div style='background:#ECFDF5;
            border-left:4px solid #10B981;
            padding:16px;
            border-radius:8px;'>

            <strong>Security Notice</strong>
            <p style='margin:8px 0 0 0;'>
            This link expires in {expiryMinutes} minutes.
            </p>

            </div>

            <p style='margin-top:24px;color:#6B7280;'>
            If you didn't request this password reset, you can safely ignore this email.
            </p>

            <p style='font-size:13px;color:#6B7280;'>
            {resetLink}
            </p>";

            {
            }

            {

            }
            {
        }
        
    }
}
