using SocietyLedger.Application.DTOs.Society;
using System;
using System.Threading.Tasks;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface ISocietyService
    {
        Task<SocietyResponseDto> GetSocietyAsync(long userId);
        Task<SocietyResponseDto> UpdateSocietyAsync(Guid publicId, UpdateSocietyDto request, long userId);
    }
}