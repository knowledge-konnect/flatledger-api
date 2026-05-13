using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminPlatformSettingService : IAdminPlatformSettingService
    {
        private const int MaxPageSize = 200;
        private readonly AppDbContext _db;
        public AdminPlatformSettingService(AppDbContext db) { _db = db; }

        private static PlatformSettingDto ToDto(platform_setting s) => new()
        {
            Id = s.id, Key = s.key, Value = s.value,
            Description = s.description,
            CreatedAt = s.created_at, UpdatedAt = s.updated_at
        };

        public async Task<PagedResult<PlatformSettingDto>> GetSettingsAsync(int page, int pageSize, string? search = null)
        {
            pageSize = Math.Min(pageSize, MaxPageSize);
            var query = _db.platform_settings.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => s.key.Contains(search));
            var total = await query.CountAsync();
            var items = await query
                .OrderBy(s => s.key)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new PlatformSettingDto
                {
                    Id = s.id, Key = s.key, Value = s.value,
                    Description = s.description,
                    CreatedAt = s.created_at, UpdatedAt = s.updated_at
                })
                .ToListAsync();
            return new PagedResult<PlatformSettingDto>(items, total, page, pageSize);
        }

        public async Task<PlatformSettingDto?> GetSettingByKeyAsync(string key)
        {
            var s = await _db.platform_settings.AsNoTracking().FirstOrDefaultAsync(x => x.key == key);
            return s == null ? null : ToDto(s);
        }

        public async Task<PlatformSettingDto> UpsertSettingAsync(PlatformSettingUpsertRequest request)
        {
            var existing = await _db.platform_settings.FirstOrDefaultAsync(x => x.key == request.Key);
            if (existing != null)
            {
                existing.value = request.Value;
                existing.description = request.Description;
                existing.updated_at = DateTime.UtcNow;
            }
            else
            {
                existing = new platform_setting
                {
                    key = request.Key,
                    value = request.Value,
                    description = request.Description,
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };
                _db.platform_settings.Add(existing);
            }
            await _db.SaveChangesAsync();
            return ToDto(existing);
        }

        public async Task DeleteSettingAsync(string key)
        {
            var entity = await _db.platform_settings.FirstOrDefaultAsync(x => x.key == key);
            if (entity == null) throw new NotFoundException("PlatformSetting", key);
            _db.platform_settings.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }
}
