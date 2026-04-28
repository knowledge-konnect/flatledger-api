namespace SocietyLedger.Domain.Entities
{
    /// <summary>
    /// Represents a system user. Can be a society admin, treasurer, or a flat resident.
    /// </summary>
    public class User
    {
        /// <summary>Internal database identifier — never exposed in API responses.</summary>
        public long Id { get; set; }
        /// <summary>Public-facing UUID used in all API endpoints.</summary>
        public Guid PublicId { get; set; }
        public long SocietyId { get; set; }
        public Guid SocietyPublicId { get; set; }
        public string SocietyName { get; set; }
        public string Name { get; set; } = null!;

        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? Mobile { get; set; }
        public short RoleId { get; set; }    
        public Role Role { get; set; }       
        /// <summary>BCrypt hash of the user's password. Never returned in API responses.</summary>
        public string? PasswordHash { get; set; }

        /// <summary>Password reset token hash (SHA256 hashed for security). Cleared after use.</summary>
        public string? PasswordResetTokenHash { get; set; }
        /// <summary>Expiry time for the password reset token. Single-use: cleared after reset.</summary>
        public DateTime? PasswordResetExpiresAt { get; set; }

        public bool IsActive { get; set; } = true;
        /// <summary>When true, the user must change their password on next login.</summary>
        public bool ForcePasswordChange { get; set; }
        public DateTime? LastLogin { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }


    /// <summary>Lookup entity for user roles (admin, treasurer, viewer, etc.).</summary>
    public class Role
    {
        public short Id { get; set; }         
        public string Code { get; set; } = null!;         
        public string DisplayName { get; set; } = null!;  
    }


    /// <summary>Join entity for the many-to-many relationship between users and roles.</summary>
    public class UserRole
    {
        public long UserId { get; set; }
        public User User { get; set; } = null!;
        public int RoleId { get; set; }
        public Role Role { get; set; } = null!;
    }


    /// <summary>
    /// Persisted refresh token for rotating-token authentication.
    /// Revoked on logout; <see cref="ReplacedByToken"/> tracks the rotation chain for audit purposes.
    /// </summary>
    public class RefreshToken
    {
        public long Id { get; set; }
        public string Token { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime CreatedAt { get; set; }
        public long UserId { get; set; }
        public User User { get; set; } = null!;
        /// <summary>Token that superseded this one during rotation. Null for the latest token in the chain.</summary>
        public string? ReplacedByToken { get; set; }
    }
}
