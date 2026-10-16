using G54.BLL.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class StorageService(
    IConfiguration configuration,
    ILogger<StorageService> logger) : IStorageService
{
    public const long DefaultMaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".xlsx",
        ".xls",
        ".docx",
        ".doc",
        ".png",
        ".jpg",
        ".jpeg",
    };

    private static readonly Action<ILogger, string, Exception?> LogTraversalAttempt =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "PathTraversalAttempt"), "Potential path traversal attempt detected for filename: {FileName}");

    private static readonly Action<ILogger, string, string, long, Exception?> LogFileStored =
        LoggerMessage.Define<string, string, long>(LogLevel.Information, new EventId(2, "FileStored"), "Stored evidence file {OriginalName} as {StoredPath} ({Bytes} bytes)");

    public async Task<IReadOnlyList<StoredFileMetadata>> UploadEvidenceFilesAsync(
        IEnumerable<IFormFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        var configuredFolder = configuration["Storage:EvidencePath"] ?? "uploads/evidence";
        var baseDirectory = Path.IsPathRooted(configuredFolder)
            ? configuredFolder
            : Path.Combine(AppContext.BaseDirectory, configuredFolder);

        Directory.CreateDirectory(baseDirectory);
        var canonicalBaseDir = Path.GetFullPath(baseDirectory);
        if (!canonicalBaseDir.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalBaseDir += Path.DirectorySeparatorChar;
        }

        var results = new List<StoredFileMetadata>();

        foreach (var file in files)
        {
            if (file.Length <= 0)
            {
                throw new ValidationException($"Tệp tin '{file.FileName}' rỗng (0 bytes).");
            }

            if (file.Length > DefaultMaxFileSizeBytes)
            {
                throw new ValidationException(
                    $"Tệp tin '{file.FileName}' vượt quá dung lượng tối đa cho phép (25 MB).");
            }

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            {
                throw new ValidationException(
                    $"Định dạng tệp '{extension}' không được hỗ trợ. Chỉ chấp nhận các định dạng: {string.Join(", ", AllowedExtensions)}.");
            }

            var sanitizedOriginalName = Path.GetFileName(file.FileName);
            var safeStoredFileName = $"{Guid.NewGuid():N}_{sanitizedOriginalName}";
            var destinationPath = Path.Combine(baseDirectory, safeStoredFileName);
            var canonicalDestination = Path.GetFullPath(destinationPath);

            // SEC-BE-H06: Prevent path traversal attacks
            if (!canonicalDestination.StartsWith(canonicalBaseDir, StringComparison.OrdinalIgnoreCase))
            {
                LogTraversalAttempt(logger, file.FileName, null);
                throw new ValidationException("Đường dẫn lưu trữ tệp không hợp lệ.");
            }

            await using (var fileStream = new FileStream(
                canonicalDestination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await file.CopyToAsync(fileStream, cancellationToken);
            }

            var relativeStoredPath = Path.Combine(configuredFolder, safeStoredFileName).Replace('\\', '/');

            results.Add(new StoredFileMetadata(
                FileName: sanitizedOriginalName,
                FilePath: relativeStoredPath,
                MimeType: string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize: file.Length));

            LogFileStored(logger, sanitizedOriginalName, relativeStoredPath, file.Length, null);
        }

        return results;
    }
}
