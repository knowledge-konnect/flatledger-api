namespace SocietyLedger.Application.DTOs.Admin
{
    /// <summary>
    /// Platform-wide key-value setting.
    /// </summary>
    public class PlatformSettingDto
    {
        public long Id { get; set; }
        public string Key { get; set; } = null!;
        public string? Value { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PlatformSettingUpsertRequest
    {
        public string Key { get; set; } = null!;
        public string? Value { get; set; }
        public string? Description { get; set; }
    }
}
