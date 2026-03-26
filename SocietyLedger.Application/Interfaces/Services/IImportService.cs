using Microsoft.AspNetCore.Http;
using SocietyLedger.Application.DTOs.Import;
using SocietyLedger.Application.DTOs.User;

namespace SocietyLedger.Application.Interfaces.Services;

public interface IFileImportService
{
    Task<ServiceResult<FileImportPreviewResponse>> PreviewFileAsync(IFormFile file, string traceId);
    Task<ServiceResult<FileImportResultResponse>> CommitImportAsync(FileImportCommitRequest request, long userId, string traceId);
}
