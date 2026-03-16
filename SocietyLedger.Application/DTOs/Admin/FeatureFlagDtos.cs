namespace SocietyLedger.Application.DTOs.Admin
{
    /// <summary>
    /// Feature flag for enabling/disabling product features globally
    /// or for a specific society.
    /// </summary>
    public class FeatureFlagDto
    {
        public long Id { get; set; }
        public string Key { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
        public long? SocietyId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class FeatureFlagCreateRequest
    {
        public string Key { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
        public long? SocietyId { get; set; }
    }

    public class FeatureFlagUpdateRequest
    {
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
    }
}
