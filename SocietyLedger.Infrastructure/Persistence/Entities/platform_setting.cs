using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Infrastructure.Persistence.Entities;

[Index("key", Name = "ux_platform_settings_key", IsUnique = true)]
public partial class platform_setting
{
    [Key]
    public long id { get; set; }

    [StringLength(100)]
    public string key { get; set; } = null!;

    public string? value { get; set; }

    public string? description { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }
}
