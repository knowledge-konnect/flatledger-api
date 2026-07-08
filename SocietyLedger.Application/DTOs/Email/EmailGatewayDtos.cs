namespace SocietyLedger.Application.DTOs.Email
{
    public class GenericEmailRequest
    {
        public string ToEmail { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string HtmlContent { get; set; } = string.Empty;
    }

    public class EmailGatewayResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
