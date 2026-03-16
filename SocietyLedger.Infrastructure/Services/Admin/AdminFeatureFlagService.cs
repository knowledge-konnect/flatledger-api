using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminFeatureFlagService : IAdminFeatureFlagService
    {
        private readonly AppDbContext _db;
        public AdminFeatureFlagService(AppDbContext db) { _db = db; }

        private static FeatureFlagDto ToDto(feature_flag f) => new()
        {
            Id = f.id,
            Key = f.key,
            Description = f.description,
            IsEnabled = f.is_enabled,
            SocietyId = f.society_id,
            CreatedAt = f.created_at,
            UpdatedAt = f.updated_at
        };

        public async Task<PagedResult<FeatureFlagDto>> GetFlagsAsync(int page, int pageSize, string? search = null, long? societyId = null)
        {
            var query = _db.feature_flags.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(f => f.key.Contains(search));
            if (societyId.HasValue)
                query = query.Where(f => f.society_id == societyId);
            var total = await query.CountAsync();
            var items = await query
                .OrderBy(f => f.key)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new FeatureFlagDto
                {
                    Id = f.id, Key = f.key, Description = f.description,
                    IsEnabled = f.is_enabled, SocietyId = f.society_id,
                    CreatedAt = f.created_at, UpdatedAt = f.updated_at
                })
                .ToListAsync();
            return new PagedResult<FeatureFlagDto>(items, total, page, pageSize);
        }

        public async Task<FeatureFlagDto?> GetFlagByIdAsync(long id)
        {
            var f = await _db.feature_flags.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
            return f == null ? null : ToDto(f);
        }

        public async Task<FeatureFlagDto> CreateFlagAsync(FeatureFlagCreateRequest request)
        {
            if (await _db.feature_flags.AnyAsync(f => f.key == request.Key && f.society_id == request.SocietyId))
                throw new ConflictException($"Feature flag '{request.Key}' already exists for the given scope.");

            var entity = new feature_flag
            {
                key = request.Key,
                description = request.Description,
                is_enabled = request.IsEnabled,
                society_id = request.SocietyId,
                created_at = DateTime.UtcNow,
                updated_at = DateTime.UtcNow
            };
            _db.feature_flags.Add(entity);
            await _db.SaveChangesAsync();
            return ToDto(entity);
        }

        public async Task<FeatureFlagDto> UpdateFlagAsync(long id, FeatureFlagUpdateRequest request)
        {
            var entity = await _db.feature_flags.FirstOrDefaultAsync(x => x.id == id);
            if (entity == null) throw new NotFoundException("FeatureFlag", id.ToString());
            entity.description = request.Description;
            entity.is_enabled = request.IsEnabled;
            entity.updated_at = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return ToDto(entity);
        }

        public async Task DeleteFlagAsync(long id)
        {
            var entity = await _db.feature_flags.FirstOrDefaultAsync(x => x.id == id);
            if (entity == null) throw new NotFoundException("FeatureFlag", id.ToString());
            _db.feature_flags.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }
}
