namespace SocietyLedger.Application.DTOs.Import;

public class FileImportCommitRequest
{
    public Dictionary<string, string> ColumnMappings { get; set; } = new();
    public List<Dictionary<string, string>> DataRows { get; set; } = new();
}
