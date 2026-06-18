using System.Text.Json.Serialization;

namespace SocietyLedger.Application.DTOs.Dashboard
{
    public class FlatSummaryDto
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("occupied")]
        public int Occupied { get; set; }

        [JsonPropertyName("vacant")]
        public int Vacant { get; set; }

        [JsonPropertyName("rented")]
        public int Rented { get; set; }

        [JsonPropertyName("zero_amount_count")]
        public int ZeroAmountCount { get; set; }
    }
}
