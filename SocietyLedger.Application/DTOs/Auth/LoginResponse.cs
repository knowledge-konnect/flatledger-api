using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SocietyLedger.Application.DTOs.Auth
{
    public class LoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;

        public DateTime AccessTokenExpiresAt { get; set; }

        /// <summary>
        /// Not serialised — sent as an httpOnly cookie instead.
        /// </summary>
        [JsonIgnore]
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// Not serialised — the cookie expiry carries this information.
        /// </summary>
        [JsonIgnore]
        public DateTime RefreshTokenExpiresAt { get; set; }

        /// <summary>Array of role objects — primary field used by the frontend collectUserRoles helper.</summary>
        public IEnumerable<SocietyLedger.Application.DTOs.RoleDto> Roles { get; set; } = Enumerable.Empty<SocietyLedger.Application.DTOs.RoleDto>();

        public Guid UserPublicId { get; set; }
        public Guid SocietyPublicId { get; set; }
        public string SocietyName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;      
        public string? Role { get; set; } 
        public string? RoleDisplayName { get; set; }
        public bool ForcePasswordChange { get; set; }
    }
}
