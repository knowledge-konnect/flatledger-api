using Newtonsoft.Json;
using System.Globalization;
using System.Text.Json;
using STJJsonConverter = System.Text.Json.Serialization.JsonConverterAttribute;
using STJJsonPropertyName = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace SocietyLedger.Application.DTOs.Reports
{
    public class MonthlyReportDto
    {
        [JsonProperty("society_name")]
        [STJJsonPropertyName("society_name")]
        public string SocietyName { get; set; } = string.Empty;

        [JsonProperty("period_label")]
        [STJJsonPropertyName("period_label")]
        public string PeriodLabel { get; set; } = string.Empty;

        [JsonProperty("summary")]
        [STJJsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonProperty("alerts")]
        [STJJsonPropertyName("alerts")]
        public List<string> Alerts { get; set; } = new();

        [JsonProperty("payment_summary")]
        [STJJsonPropertyName("payment_summary")]
        public PaymentSummaryDto PaymentSummary { get; set; } = new();

        [JsonProperty("fund_position")]
        [STJJsonPropertyName("fund_position")]
        public FundPositionDto FundPosition { get; set; } = new();

        [JsonProperty("flat_details")]
        [STJJsonPropertyName("flat_details")]
        public List<FlatDetailDto> FlatDetails { get; set; } = new();

        [JsonProperty("expenses")]
        [STJJsonPropertyName("expenses")]
        public List<ExpenseDto> Expenses { get; set; } = new();
    }

    public class FundPositionDto
    {
        [JsonProperty("opening_balance")]
        [STJJsonPropertyName("opening_balance")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal OpeningBalance { get; set; }

        [JsonProperty("collected")]
        [STJJsonPropertyName("collected")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal Collected { get; set; }

        [JsonProperty("expenses")]
        [STJJsonPropertyName("expenses")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal Expenses { get; set; }

        [JsonProperty("closing_balance")]
        [STJJsonPropertyName("closing_balance")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal ClosingBalance { get; set; }
    }

    public class PaymentSummaryDto
    {
        [JsonProperty("total_flats")]
        [STJJsonPropertyName("total_flats")]
        public int TotalFlats { get; set; }

        [JsonProperty("paid")]
        [STJJsonPropertyName("paid")]
        public int Paid { get; set; }

        [JsonProperty("pending")]
        [STJJsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonProperty("total_billed")]
        [STJJsonPropertyName("total_billed")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal TotalBilled { get; set; }

        [JsonProperty("total_collected")]
        [STJJsonPropertyName("total_collected")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal TotalCollected { get; set; }

        [JsonProperty("pending_amount")]
        [STJJsonPropertyName("pending_amount")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal PendingAmount { get; set; }

        [JsonProperty("collection_efficiency")]
        [STJJsonPropertyName("collection_efficiency")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal CollectionEfficiency { get; set; }
    }

    public class FlatDetailDto
    {
        [JsonProperty("flat_no")]
        [STJJsonPropertyName("flat_no")]
        public string FlatNo { get; set; } = string.Empty;

        [JsonProperty("owner_name")]
        [STJJsonPropertyName("owner_name")]
        public string? OwnerName { get; set; }

        [JsonProperty("opening_balance")]
        [STJJsonPropertyName("opening_balance")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal OpeningBalance { get; set; }

        [JsonProperty("current_bill")]
        [STJJsonPropertyName("current_bill")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal CurrentBill { get; set; }

        [JsonProperty("current_paid")]
        [STJJsonPropertyName("current_paid")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal CurrentPaid { get; set; }

        [JsonProperty("total_due")]
        [STJJsonPropertyName("total_due")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal TotalDue { get; set; }

        [JsonProperty("balance_amount")]
        [STJJsonPropertyName("balance_amount")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal BalanceAmount { get; set; }

        [JsonProperty("status")]
        [STJJsonPropertyName("status")]
        public string? Status { get; set; }
    }

    public class DefaulterDto
    {
        [JsonProperty("flat_no")]
        [STJJsonPropertyName("flat_no")]
        public string FlatNo { get; set; } = string.Empty;

        [JsonProperty("owner_name")]
        [STJJsonPropertyName("owner_name")]
        public string? OwnerName { get; set; }

        [JsonProperty("pending")]
        [STJJsonPropertyName("pending")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal Pending { get; set; }
    }

    public class ExpenseDto
    {
        [JsonProperty("category_name")]
        [STJJsonPropertyName("category_name")]
        public string CategoryName { get; set; } = string.Empty;

        [JsonProperty("total_amount")]
        [STJJsonPropertyName("total_amount")]
        [STJJsonConverter(typeof(ZeroOnNullDecimalConverter))]
        public decimal TotalAmount { get; set; }
    }

    internal sealed class ZeroOnNullDecimalConverter : System.Text.Json.Serialization.JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return 0m;

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number))
                return number;

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    return 0m;

                if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;

                if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                    return parsed;
            }

            throw new System.Text.Json.JsonException($"Unable to parse decimal value from token type {reader.TokenType}.");
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
}
