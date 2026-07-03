using System.ComponentModel.DataAnnotations;

namespace SocietyLedger.Shared
{
    public class DatabaseSettings
    {
        [Required]
        public string DefaultConnection { get; set; } = string.Empty;
    }
}
