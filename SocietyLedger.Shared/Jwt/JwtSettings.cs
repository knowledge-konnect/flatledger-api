using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocietyLedger.Shared.Jwt
{
    public class JwtSettings
    {
        [Required]
        public string Issuer { get; set; } = string.Empty;

        [Required]
        public string Audience { get; set; } = string.Empty;

        [Required]
        [MinLength(32)]
        public string Key { get; set; } = string.Empty;

        public int AccessTokenExpirationMinutes { get; set; }

        public int RefreshTokenExpirationDays { get; set; }
    }
}
