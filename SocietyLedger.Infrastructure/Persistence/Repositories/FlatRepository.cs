using Microsoft.EntityFrameworkCore;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Extensions;

namespace SocietyLedger.Infrastructure.Persistence.Repositories
{
    public class FlatRepository : IFlatRepository
    {
        private readonly AppDbContext _db;

        public FlatRepository(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IEnumerable<Flat>> GetBySocietyIdAsync(long societyId)
        {
            var flats = await _db.flats
                .ForSociety(societyId)
                .Include(f => f.status)
                .Include(f => f.society)
                .AsNoTracking()
                .ToListAsync();

            return flats.Select(e => e.ToDomain());
        }

        /// <summary>
        /// Get a flat by its public_id (UUID) within a specific society for tenant isolation.
        /// </summary>
        public async Task<Flat?> GetByPublicIdAsync(Guid publicId, long societyId)
        {
            var efFlat = await _db.flats
                .ForSociety(societyId)
                .Include(f => f.society)
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.public_id == publicId);

            return efFlat?.ToDomain();
        }

        /// <summary>
        /// Get a flat by flat number within a specific society.
        /// </summary>
        public async Task<Flat?> GetByFlatNoAndSocietyAsync(string flatNo, long societyId)
        {
            if (string.IsNullOrWhiteSpace(flatNo))
                return null;

            var efFlat = await _db.flats
                .ForSociety(societyId)
                .Include(f => f.society)
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.flat_no == flatNo);

            return efFlat?.ToDomain();
        }

        /// <summary>
        /// Adds a new flat to the database and saves immediately.
        /// Copies generated ID and PublicId back to the domain model.
        /// </summary>
        public async Task AddAsync(Flat flat)
        {
            if (flat == null)
                throw new ArgumentNullException(nameof(flat));

            var entity = flat.ToEntity();

            await _db.flats.AddAsync(entity);
            await _db.SaveChangesAsync();

            flat.Id = entity.id;
            flat.PublicId = entity.public_id;
        }

        /// <summary>
        /// Updates an existing flat with society_id verification for tenant isolation.
        /// </summary>
        public async Task UpdateAsync(Flat flat, long societyId)
        {
            if (flat == null)
                throw new ArgumentNullException(nameof(flat));

            var efFlat = await _db.flats
                .ForSociety(societyId)
                .FirstOrDefaultAsync(f => f.public_id == flat.PublicId);
            
            if (efFlat == null)
                throw new InvalidOperationException($"Flat with PublicId {flat.PublicId} not found in society {societyId}.");

            efFlat.flat_no = flat.FlatNo;
            efFlat.owner_name = flat.OwnerName;
            efFlat.contact_mobile = flat.ContactMobile;
            efFlat.contact_email = flat.ContactEmail;
            efFlat.maintenance_amount = flat.MaintenanceAmount;
            efFlat.status_id = flat.StatusId;
            efFlat.updated_at = flat.UpdatedAt;
        }

        /// <summary>
        /// Soft deletes a flat by its public_id with society_id verification for tenant isolation.
        /// </summary>
        public async Task DeleteByPublicIdAsync(Guid publicId, long societyId)
        {
            var efFlat = await _db.flats.FirstOrDefaultAsync(f => f.public_id == publicId && f.society_id == societyId && !f.is_deleted);
            if (efFlat != null)
            {
                efFlat.is_deleted = true;
                efFlat.deleted_at = DateTime.UtcNow;
                efFlat.updated_at = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Batch adds multiple flats in a single database operation (one SaveChanges call).
        /// Returns the created flats with generated IDs and PublicIds.
        /// </summary>
        public async Task<IEnumerable<Flat>> BulkAddAsync(IEnumerable<Flat> flats)
        {
            if (flats == null || !flats.Any())
                return Enumerable.Empty<Flat>();

            var entities = flats.Select(f => f.ToEntity()).ToList();
            
            await _db.flats.AddRangeAsync(entities);
            await _db.SaveChangesAsync();

            // Copy generated IDs back to domain models
            var result = new List<Flat>();
            foreach (var entity in entities)
            {
                var flat = entity.ToDomain();
                flat.Id = entity.id;
                flat.PublicId = entity.public_id;
                result.Add(flat);
            }

            return result;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
        public async Task<IEnumerable<FlatStatus>> GetAllAsync()
        {
            var list = await _db.flat_statuses
                .AsNoTracking()
                .OrderBy(s => s.id)
                .ToListAsync();

            return list.Select(e => e.ToDomain());
        }

        public async Task<FlatStatus?> GetByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            var e = await _db.flat_statuses
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.code == code);

            return e == null ? null : e.ToDomain();
        }

        public async Task<Flat?> GetByEmailAndSocietyAsync(string email, long societyId)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            var efFlat = await _db.flats
                .ForSociety(societyId)
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.contact_email == email && !f.is_deleted);

            return efFlat?.ToDomain();
        }

        public async Task<Flat?> GetByMobileAndSocietyAsync(string mobile, long societyId)
        {
            if (string.IsNullOrWhiteSpace(mobile))
                return null;

            var efFlat = await _db.flats
                .ForSociety(societyId)
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.contact_mobile == mobile && !f.is_deleted);

            return efFlat?.ToDomain();
        }
    }
}
