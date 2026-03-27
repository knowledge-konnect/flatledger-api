using System.Text.Json.Serialization;

namespace SocietyLedger.Application.DTOs.OpeningBalance
{
    public class OpeningBalanceRequest
    {
        /// <summary>
        /// The financial date of the opening entry (e.g. the actual start-of-books date).
        /// Must be >= society.onboarding_date and <= today.
        /// Stored as transaction_date — never derived from created_at.
        /// </summary>
        [JsonPropertyName("transactionDate")]
        public DateOnly TransactionDate { get; set; }

        [JsonPropertyName("society_opening_amount")]
        public decimal SocietyOpeningAmount { get; set; }

        [JsonPropertyName("items")]
        public List<OpeningBalanceItemDto> Items { get; set; } = new List<OpeningBalanceItemDto>();
    }
}
