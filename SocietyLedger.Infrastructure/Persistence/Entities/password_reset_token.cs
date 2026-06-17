using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocietyLedger.Infrastructure.Persistence.Entities
{
    [Table("password_reset_tokens")]
    public class password_reset_token
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("user_id")]
        public long user_id { get; set; }

        [Column("token_hash")]
        public string token_hash { get; set; } = null!;

        [Column("expires_at")]
        public DateTime expires_at { get; set; }

        [Column("created_at")]
        public DateTime created_at { get; set; }

        [Column("created_by_ip")]
        public string? created_by_ip { get; set; }

        [Column("is_used")]
        public bool is_used { get; set; }

        [Column("used_at")]
        public DateTime? used_at { get; set; }

        // Navigation property
        public user? user { get; set; }
    }
}