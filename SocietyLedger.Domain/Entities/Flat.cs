namespace SocietyLedger.Domain.Entities
{
    /// <summary>
    /// Represents a flat (unit) within a society.
    /// The <see cref="MaintenanceAmount"/> is used as the default charge when generating monthly bills.
    /// </summary>
    public class Flat
    {
        /// <summary>Internal database identifier — never exposed in API responses.</summary>
        public long Id { get; set; }
        /// <summary>Public-facing UUID used in all API endpoints.</summary>
        public Guid PublicId { get; set; }
        public long SocietyId { get; set; }
        public Guid SocietyPublicId { get; set; }
        public string FlatNo { get; set; } = null!;
        public string? OwnerName { get; set; }
        public string? ContactMobile { get; set; }
        public string? ContactEmail { get; set; }
        /// <summary>Monthly maintenance charge used as the default amount when generating bills for this flat.</summary>
        public decimal MaintenanceAmount { get; set; }
        public short? StatusId { get; set; }              
        public string StatusName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
