using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Infrastructure.Persistence.Entities;

[Index("key", "society_id", Name = "ux_feature_flags_key_scope", IsUnique = true)]
public partial class feature_flag
{
    [Key]
    public long id { get; set; }

    [StringLength(100)]
    public string key { get; set; } = null!;

    public string? description { get; set; }

    public bool is_enabled { get; set; }

    public long? society_id { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }

    [ForeignKey("society_id")]
    [InverseProperty("feature_flags")]
    public virtual society? society { get; set; }
}
