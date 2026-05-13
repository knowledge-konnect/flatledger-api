using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SocietyLedger.Infrastructure.Persistence.Entities;

[Index("plan_group", "duration_months", Name = "ux_plans_group_duration", IsUnique = true)]
public partial class plan
{
    [Key]
    public Guid id { get; set; }

    [StringLength(100)]
    public string name { get; set; } = null!;

    [Precision(10, 2)]
    public decimal price { get; set; }

    public int max_flats { get; set; }

    public int display_order { get; set; }

    public bool is_popular { get; set; }

    [StringLength(500)]
    public string? description { get; set; }

    public int? discount_percentage { get; set; }

    [StringLength(100)]
    public string plan_group { get; set; } = null!;

    [StringLength(3)]
    public string currency { get; set; } = null!;

    public bool? is_active { get; set; }

    public DateTime? created_at { get; set; }

    public int duration_months { get; set; }

    [InverseProperty("plan")]
    public virtual ICollection<subscription> subscriptions { get; set; } = new List<subscription>();
}
