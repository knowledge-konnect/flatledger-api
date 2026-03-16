namespace SocietyLedger.Application.DTOs.Admin
{
    /// <summary>
    /// Read-only profile returned by GET /api/admin/auth/me.
    /// Never exposes the internal numeric id or password_hash.
    /// </summary>
    public class AdminProfileDto
    {
        public Guid PublicId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
