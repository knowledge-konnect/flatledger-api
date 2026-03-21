namespace SocietyLedger.Application.DTOs.Billing
{
    /// <summary>
    /// Request to generate a bill for a specific flat for the current month.
    /// FlatPublicId is the public-facing UUID returned when the flat was created.
    /// Society is resolved from the authenticated user's JWT.
    /// </summary>
    public record GenerateBillForFlatRequest(Guid FlatPublicId);
}
