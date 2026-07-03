using System.ComponentModel.DataAnnotations;

namespace SocietyLedger.Shared
{
    public class CorsSettings
    {
        [Required]
        [MinLength(1)]
        public string[] AllowedOrigins { get; set; } = new string[0];
    }
}
