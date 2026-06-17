using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Infrastructure.Persistence.Entities;

#pragma warning disable CS8981
[Index("name", Name = "ux_plans_name", IsUnique = true)]
public partial class plan
{
    [Key]
    public Guid id { get; set; }

    [StringLength(100)]
    public string name { get; set; } = null!;

    [Precision(10, 2)]
    public decimal price { get; set; }

    [Precision(10, 2)]
    public decimal monthly_amount { get; set; }

    [StringLength(3)]
    public string currency { get; set; } = null!;

    public bool? is_active { get; set; }

    public DateTime? created_at { get; set; }

    public DateTime? updated_at { get; set; }

    public int duration_months { get; set; }

    public int max_flats { get; set; }

    [StringLength(50)]
    public string? plan_group { get; set; }

    public int? discount_percentage { get; set; }

    public int display_order { get; set; }

    public bool is_popular { get; set; }

    public string? description { get; set; }

    [InverseProperty("plan")]
    public virtual ICollection<subscription> subscriptions { get; set; } = new List<subscription>();
}
#pragma warning restore CS8981
