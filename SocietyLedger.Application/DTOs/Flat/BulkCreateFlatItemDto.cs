namespace SocietyLedger.Application.DTOs.Flat
{
    /// <summary>
    /// DTO used for each item in a bulk flat create request.
    /// maintenanceAmount is intentionally excluded — it is managed via maintenance config, not user input.
    /// </summary>
    public record BulkCreateFlatItemDto(
        string FlatNo,
        string? OwnerName,
        string? ContactMobile,
        string? ContactEmail,
        string? StatusCode
    );
}
