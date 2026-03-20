using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.DTOs.Admin;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Shared;

namespace SocietyLedger.Infrastructure.Services.Admin
{
    public class AdminSocietyService : IAdminSocietyService
    {
        private readonly AppDbContext _db;
        public AdminSocietyService(AppDbContext db) { _db = db; }

        public async Task<PagedResult<AdminSocietyDto>> GetSocietiesAsync(int page, int pageSize, string? search = null)
        {
            var query = _db.societies.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => s.name.ToLower().Contains(search.ToLower()));
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(s => s.created_at)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new AdminSocietyDto
                {
                    Id = s.id,
                    PublicId = s.public_id,
                    Name = s.name,
                    Address = s.address,
                    City = s.city,
                    State = s.state,
                    Pincode = s.pincode,
                    Currency = s.currency,
                    DefaultMaintenanceCycle = s.default_maintenance_cycle,
                    CreatedAt = s.created_at,
                    UpdatedAt = s.updated_at,
                    IsDeleted = s.is_deleted,
                    DeletedAt = s.deleted_at,
                    OnboardingDate = s.onboarding_date
                })
                .ToListAsync();
            return new PagedResult<AdminSocietyDto>(items, total, page, pageSize);
        }

        public async Task<AdminSocietyDto?> GetSocietyByIdAsync(long id)
        {
            var s = await _db.societies.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
            if (s == null) return null;
            return new AdminSocietyDto
            {
                Id = s.id,
                PublicId = s.public_id,
                Name = s.name,
                Address = s.address,
                City = s.city,
                State = s.state,
                Pincode = s.pincode,
                Currency = s.currency,
                DefaultMaintenanceCycle = s.default_maintenance_cycle,
                CreatedAt = s.created_at,
                UpdatedAt = s.updated_at,
                IsDeleted = s.is_deleted,
                DeletedAt = s.deleted_at,
                OnboardingDate = s.onboarding_date
            };
        }

        public async Task<AdminSocietyDto> UpdateSocietyAsync(long id, AdminSocietyUpdateRequest request)
        {
            var s = await _db.societies.FirstOrDefaultAsync(x => x.id == id);
            if (s == null) throw new NotFoundException("Society", id.ToString());
            s.name = request.Name;
            s.address = request.Address;
            s.city = request.City;
            s.state = request.State;
            s.pincode = request.Pincode;
            s.currency = request.Currency;
            s.default_maintenance_cycle = request.DefaultMaintenanceCycle;
            if (request.IsDeleted.HasValue)
            {
                s.is_deleted = request.IsDeleted.Value;
                s.deleted_at = request.IsDeleted.Value ? DateTime.UtcNow : null;
            }
            s.updated_at = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return await GetSocietyByIdAsync(s.id) ?? throw new Exception("Failed to update society");
        }
    }
}
