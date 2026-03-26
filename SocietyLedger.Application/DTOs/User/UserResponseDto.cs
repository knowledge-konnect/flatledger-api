namespace SocietyLedger.Application.DTOs.User
{
    /// <summary>
    /// DTO for user response. Password hash is never included in responses.
    /// Includes role data in both array form (Roles) and flat fallback (Role, RoleDisplayName).
    /// </summary>
    public record UserResponseDto(
        Guid PublicId,
        Guid SocietyPublicId,
        string Name,
        string? Email,
        string? Mobile,
        string? RoleDisplayName,
        bool IsActive,
        bool ForcePasswordChange,
        DateTime? LastLogin,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        /// <summary>Flat role code fallback (e.g. "society_admin" or "viewer").</summary>
        string? Role = null,
        /// <summary>Array of role objects — primary field used by the frontend.</summary>
        IEnumerable<SocietyLedger.Application.DTOs.RoleDto>? Roles = null
    );
}
