namespace SocietyLedger.Api.Extensions;

public class RateLimitingSettings
{
    public int GlobalPermitLimit { get; set; } = 100;
    public int AuthPermitLimit { get; set; } = 5;
    public int PaymentPermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
}

public class DatabaseWarmupSettings
{
    public int MaxAttempts { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 30;
    public int StatementTimeoutSeconds { get; set; } = 120;
}
