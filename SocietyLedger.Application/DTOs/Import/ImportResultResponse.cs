namespace SocietyLedger.Application.DTOs.Import;

public class FileImportResultResponse
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<FileImportRowError> ImportErrors { get; set; } = new();
}

public class FileImportRowError
{
    public int RowNumber { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
