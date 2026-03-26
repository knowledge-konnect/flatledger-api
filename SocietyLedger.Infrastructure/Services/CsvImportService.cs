using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SocietyLedger.Application.DTOs.Import;
using SocietyLedger.Application.DTOs.User;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services.Common;
using System.Text;

namespace SocietyLedger.Infrastructure.Services;

public class FileImportService : IFileImportService
{
    private readonly AppDbContext _db;
    private readonly IUserContext _userContext;
    private readonly ILogger _logger;
    private const int MaxPreviewRows = 10;
    private const int MaxFileSizeBytes = 2 * 1024 * 1024; // 2MB

    public FileImportService(AppDbContext db, IUserContext userContext)
    {
        _db = db;
        _userContext = userContext;
        _logger = Log.Logger;
    }

    public async Task<ServiceResult<FileImportPreviewResponse>> PreviewFileAsync(IFormFile file, string traceId)
    {
        if (file == null || file.Length == 0)
            return ServiceResult<FileImportPreviewResponse>.Failure("NO_FILE", "No file uploaded", 400);
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ServiceResult<FileImportPreviewResponse>.Failure("INVALID_FILE_TYPE", "Only CSV files are supported", 400);
        if (file.Length > MaxFileSizeBytes)
            return ServiceResult<FileImportPreviewResponse>.Failure("FILE_TOO_LARGE", "File size exceeds 2MB limit", 400);

        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            
            // Read CSV manually without CsvHelper dependency
            var headers = new List<string>();
            var previewRows = new List<Dictionary<string, string>>();
            int lineNumber = 0;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null && lineNumber < MaxPreviewRows + 1)
            {
                var values = line.Split(',').Select(v => v.Trim()).ToList();

                if (lineNumber == 0)
                {
                    headers = values;
                }
                else if (values.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < headers.Count && i < values.Count; i++)
                    {
                        row[headers[i]] = values[i];
                    }
                    previewRows.Add(row);
                }
                lineNumber++;
            }

            if (headers.Count == 0)
                return ServiceResult<FileImportPreviewResponse>.Failure("INVALID_CSV", "CSV file missing headers", 400);

            return ServiceResult<FileImportPreviewResponse>.Success(new FileImportPreviewResponse
            {
                Headers = headers,
                PreviewRows = previewRows.Take(MaxPreviewRows).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "File preview failed. TraceId: {TraceId}", traceId);
            return ServiceResult<FileImportPreviewResponse>.Failure("PREVIEW_ERROR", "Failed to parse file: " + ex.Message, 400);
        }
    }

    public async Task<ServiceResult<FileImportResultResponse>> CommitImportAsync(FileImportCommitRequest request, long userId, string traceId)
    {
        var result = new FileImportResultResponse();
        if (request == null || request.DataRows == null || request.DataRows.Count == 0)
            return ServiceResult<FileImportResultResponse>.Failure("NO_ROWS", "No data rows provided", 400);
        if (request.ColumnMappings == null || request.ColumnMappings.Count == 0)
            return ServiceResult<FileImportResultResponse>.Failure("NO_MAPPINGS", "No column mappings provided", 400);

        // Validate required mappings exist upfront
        var flatNumberKey = request.ColumnMappings.FirstOrDefault(x => x.Value == "FlatNumber").Key;
        var ownerNameKey = request.ColumnMappings.FirstOrDefault(x => x.Value == "OwnerName").Key;
        if (string.IsNullOrEmpty(flatNumberKey))
            return ServiceResult<FileImportResultResponse>.Failure("MISSING_MAPPING", "Column mapping for 'FlatNumber' is required", 400);
        if (string.IsNullOrEmpty(ownerNameKey))
            return ServiceResult<FileImportResultResponse>.Failure("MISSING_MAPPING", "Column mapping for 'OwnerName' is required", 400);

        var phoneKey = request.ColumnMappings.FirstOrDefault(x => x.Value == "PhoneNumber").Key;
        var statusKey = request.ColumnMappings.FirstOrDefault(x => x.Value == "StatusCode").Key;

        // Resolve society from authenticated user
        var societyId = await _userContext.GetSocietyIdAsync(userId);
        var now = DateTime.UtcNow;

        // Cache all flat statuses once for lookup
        var allStatuses = await _db.flat_statuses.AsNoTracking().ToListAsync();

        for (int i = 0; i < request.DataRows.Count; i++)
        {
            var row = request.DataRows[i];
            try
            {
                // Clean and map
                string flatNumber = row.TryGetValue(flatNumberKey, out var fn) ? fn?.Trim() ?? string.Empty : string.Empty;
                string ownerName = row.TryGetValue(ownerNameKey, out var on) ? on?.Trim() ?? string.Empty : string.Empty;
                string phone = !string.IsNullOrEmpty(phoneKey) && row.TryGetValue(phoneKey, out var ph) ? ph?.Trim() ?? string.Empty : string.Empty;
                string statusCode = !string.IsNullOrEmpty(statusKey) && row.TryGetValue(statusKey, out var sc) ? sc?.Trim() ?? string.Empty : string.Empty;

                // Validation
                if (string.IsNullOrWhiteSpace(flatNumber))
                {
                    result.ImportErrors.Add(new FileImportRowError { RowNumber = i + 1, ErrorMessage = "Flat number missing" });
                    continue;
                }
                if (string.IsNullOrWhiteSpace(ownerName))
                {
                    result.ImportErrors.Add(new FileImportRowError { RowNumber = i + 1, ErrorMessage = "Owner name missing" });
                    continue;
                }

                short? statusId = null;
                if (!string.IsNullOrWhiteSpace(statusCode))
                {
                    var status = allStatuses.FirstOrDefault(s => s.code.Equals(statusCode, StringComparison.OrdinalIgnoreCase));
                    if (status == null)
                    {
                        result.ImportErrors.Add(new FileImportRowError { RowNumber = i + 1, ErrorMessage = $"Invalid status code '{statusCode}'. Valid values: owner_occupied, tenant_occupied, vacant, under_maintenance" });
                        continue;
                    }
                    statusId = status.id;
                }

                // Check for duplicate flat within this society (exclude soft-deleted)
                var flatExists = await _db.flats.AnyAsync(f =>
                    f.flat_no == flatNumber && f.society_id == societyId && !f.is_deleted);
                if (flatExists)
                    continue; // Skip duplicate silently

                // Insert Flat with all required fields
                var flat = new Infrastructure.Persistence.Entities.flat
                {
                    public_id = Guid.NewGuid(),
                    society_id = societyId,
                    flat_no = flatNumber,
                    owner_name = ownerName,
                    contact_mobile = string.IsNullOrWhiteSpace(phone) ? null : phone,
                    status_id = statusId,
                    maintenance_amount = 0m,
                    created_at = now,
                    updated_at = now,
                    is_deleted = false
                };
                _db.flats.Add(flat);
                await _db.SaveChangesAsync();
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Import row failed. TraceId: {TraceId}, Row: {RowNumber}", traceId, i + 1);
                result.ImportErrors.Add(new FileImportRowError { RowNumber = i + 1, ErrorMessage = "Unexpected error: " + ex.Message });
            }
        }
        result.FailedCount = result.ImportErrors.Count;
        return ServiceResult<FileImportResultResponse>.Success(result);
    }
}
