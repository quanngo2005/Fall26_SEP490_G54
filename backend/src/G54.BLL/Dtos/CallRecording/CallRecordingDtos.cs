using Microsoft.AspNetCore.Http;

namespace G54.BLL.Dtos.CallRecording;

public sealed class UploadCallRecordingRequestDto
{
    public IFormFile? File { get; set; }

    /// <summary>Optional, at most 32 characters; defaults to the product code of the work's type.</summary>
    public string? ProductCode { get; set; }
}

public sealed record CallRecordingDto(
    Guid Id,
    Guid ResultSubmissionId,
    Guid WorkId,
    string FileName,
    string MimeType,
    long SizeBytes,
    int? DurationMs,
    string? ProductCode,
    string? TransactionRef,
    string ProcessingStatus,
    string? ErrorCode,
    DateTimeOffset UploadedAt,
    DateTimeOffset? ProcessedAt,
    int FlagCount);

/// <summary>The caller owns <see cref="Content"/> and must dispose it.</summary>
public sealed record CallRecordingStreamDto(Stream Content, string ContentType);
