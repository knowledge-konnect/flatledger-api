using SocietyLedger.Application.DTOs.Society;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface ISocietyService
    {
        /// <summary>Returns the society the authenticated user belongs to.</summary>
        Task<SocietyResponseDto> GetByUserAsync(long userId);

        /// <summary>Returns a society by its public ID. Enforces that the caller belongs to that society.</summary>
        Task<SocietyResponseDto> GetByPublicIdAsync(Guid publicId, long userId);

        /// <summary>Updates the society profile. Only the society admin may call this.</summary>
        Task<SocietyResponseDto> UpdateAsync(Guid publicId, UpdateSocietyRequest request, long userId);
    }
}
