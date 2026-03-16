namespace SocietyLedger.Application.DTOs
{
    /// <summary>
    /// Portable role descriptor included in JWT claims, login responses, and user responses.
    /// </summary>
    public class RoleDto
    {
        public short Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
