using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Society;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;

namespace SocietyLedger.Infrastructure.Services
{
    public class SocietyService : ISocietyService
    {
        private readonly ISocietyRepository _societyRepo;
        private readonly IUserRepository _userRepo;
        private readonly ILogger<SocietyService> _logger;

        public SocietyService(
            ISocietyRepository societyRepo,
            IUserRepository userRepo,
            ILogger<SocietyService> logger)
        {
            _societyRepo = societyRepo;
            _userRepo = userRepo;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<SocietyResponseDto> GetByUserAsync(long userId)
        {
            var societyId = await _societyRepo.GetSocietyIdByUserIdAsync(userId)
                ?? throw new NotFoundException("Society", $"user {userId}");

            var society = await _societyRepo.GetByIdAsync(societyId)
                ?? throw new NotFoundException("Society", societyId.ToString());

            return MapToDto(society);
        }

        /// <inheritdoc/>
        public async Task<SocietyResponseDto> GetByPublicIdAsync(Guid publicId, long userId)
        {
            // Resolve the caller's society to enforce isolation
            var callerSocietyId = await _societyRepo.GetSocietyIdByUserIdAsync(userId)
                ?? throw new NotFoundException("Society", $"user {userId}");

            var society = await _societyRepo.GetByPublicIdAsync(publicId)
                ?? throw new NotFoundException("Society", publicId.ToString());

            // Prevent cross-society access
            if (society.Id != callerSocietyId)
                throw new AuthorizationException("You do not have access to this society.");

            return MapToDto(society);
        }

        /// <inheritdoc/>
        public async Task<SocietyResponseDto> UpdateAsync(Guid publicId, UpdateSocietyRequest request, long userId)
        {
            // Resolve the caller's society
            var callerSocietyId = await _societyRepo.GetSocietyIdByUserIdAsync(userId)
                ?? throw new NotFoundException("Society", $"user {userId}");

            var society = await _societyRepo.GetByPublicIdAsync(publicId)
                ?? throw new NotFoundException("Society", publicId.ToString());

            // Enforce society isolation
            if (society.Id != callerSocietyId)
                throw new AuthorizationException("You do not have access to this society.");

            // Enforce admin-only update
            var user = await _userRepo.GetByIdAsync(userId)
                ?? throw new NotFoundException("User", userId.ToString());

            if (user.Role?.Code != RoleCodes.SocietyAdmin)
                throw new AuthorizationException("Only a Society Admin can update society details.");

            society.Update(
                request.Name,
                request.Address,
                request.City,
                request.State,
                request.Country,
                request.Pincode);

            await _societyRepo.SaveChangesAsync();

            _logger.LogInformation("Society {PublicId} updated by user {UserId}", publicId, userId);
            return MapToDto(society);
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static SocietyResponseDto MapToDto(SocietyLedger.Domain.Entities.Society s) =>
            new()
            {
                PublicId = s.PublicId,
                Name = s.Name,
                Address = s.Address,
                City = s.City,
                State = s.State,
                Country = s.Country,
                Pincode = s.Pincode,
                OnboardingDate = s.OnboardingDate,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt,
            };
    }
}
