using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Infrastructure.Persistence.Entities;

[Index("public_id", Name = "ux_contact_requests_public_id", IsUnique = true)]
[Index("created_at", Name = "idx_contact_requests_created_at")]
[Index("status", Name = "idx_contact_requests_status")]
public partial class contact_request
{
    [Key]
    public long id { get; set; }

    public Guid public_id { get; set; }

    [StringLength(100)]
    public string name { get; set; } = null!;

    [StringLength(255)]
    public string email { get; set; } = null!;

    [StringLength(200)]
    public string? subject { get; set; }

    [Column(TypeName = "text")]
    public string message { get; set; } = null!;

    [StringLength(20)]
    public string status { get; set; } = "New";

    public DateTime created_at { get; set; }
}
