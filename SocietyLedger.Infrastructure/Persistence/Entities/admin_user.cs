using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Infrastructure.Persistence.Entities;

[Index("email", Name = "ux_admin_users_email", IsUnique = true)]
[Index("public_id", Name = "ux_admin_users_public_id", IsUnique = true)]
public partial class admin_user
{
    [Key]
    public long id { get; set; }

    public Guid public_id { get; set; }

    public string email { get; set; } = null!;

    public string password_hash { get; set; } = null!;

    public string name { get; set; } = null!;

    public bool is_active { get; set; }

    public DateTime? last_login { get; set; }

    public DateTime created_at { get; set; }

    public DateTime updated_at { get; set; }
}
