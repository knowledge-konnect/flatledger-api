using SocietyLedger.Application.DTOs.Society;
using System;
using System.Threading.Tasks;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface ISocietyService
    {
        Task<SocietyResponseDto> GetByUserAsync(long userId);

        Task<SocietyResponseDto> GetByPublicIdAsync(Guid publicId, long userId);

        Task<SocietyResponseDto> UpdateAsync(Guid publicId, UpdateSocietyRequest request, long userId);
    }
}
