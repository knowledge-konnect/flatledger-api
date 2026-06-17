using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocietyLedger.Infrastructure.Persistence.Entities
{
    [Table("email_notification_logs")]
    public class email_notification_log
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("notification_type")]
        public string notification_type { get; set; } = null!;

        [Column("recipient_email")]
        public string recipient_email { get; set; } = null!;

        [Column("recipient_name")]
        public string? recipient_name { get; set; }

        [Column("subject")]
        public string subject { get; set; } = null!;

        [Column("sent_at")]
        public DateTime sent_at { get; set; }

        [Column("sent_by_system")]
        public bool sent_by_system { get; set; }

        [Column("status")]
        public string status { get; set; } = null!;

        [Column("error_message")]
        public string? error_message { get; set; }

        [Column("society_id")]
        public long? society_id { get; set; }

        [Column("user_id")]
        public long? user_id { get; set; }

        [Column("metadata")]
        public string? metadata { get; set; }

        // Navigation properties
        [ForeignKey("society_id")]
        public society? society { get; set; }

        [ForeignKey("user_id")]
        public user? user { get; set; }
    }
}