namespace SocietyLedger.Application.DTOs.Import;

public class FileImportPreviewResponse
{
    public List<string> Headers { get; set; } = new();
    public List<Dictionary<string, string>> PreviewRows { get; set; } = new();
}
