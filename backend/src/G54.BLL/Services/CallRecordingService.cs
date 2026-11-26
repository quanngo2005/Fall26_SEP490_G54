using System.Globalization;
using System.Linq.Expressions;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.CallRecording;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

public sealed class CallRecordingService(
    AppDbContext db,
    IPermissionService permissions,
    IStorageService storage,
    IOptions<CallRecordingOptions> options,
    TimeProvider time,
    ILogger<CallRecordingService> logger) : ICallRecordingService
{
    private const int MaxProductCodeLength = 32;
    private const int MaxFileNameLength = 255; // call_recording.file_name column

    // Ids and sizes only: client file names and audio content never reach the logs.
    private static readonly Action<ILogger, Guid, Guid, long, Exception?> LogUploaded =
        LoggerMessage.Define<Guid, Guid, long>(
            LogLevel.Information,
            new EventId(1, "CallRecordingUploaded"),
            "Call recording {RecordingId} uploaded for submission {SubmissionId} ({Bytes} bytes)");

    public async Task<CallRecordingDto> UploadAsync(
        Guid submissionId,
        Guid userId,
        UploadCallRecordingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var submission = await db.ResultSubmissions.AsNoTracking()
            .Include(s => s.Work)
                .ThenInclude(w => w.WorkType)
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken)
            ?? throw new NotFoundException("Đợt nộp kết quả", submissionId);

        if (submission.SubmitterEmployeeId != caller.Id)
        {
            throw new ForbiddenException("Bạn không phải là người đã nộp báo cáo kết quả này.");
        }

        if (submission.Status == SubmissionStatus.Withdrawn)
        {
            throw new BusinessRuleException(ErrorCodes.CallRecordingSubmissionClosed, statusCode: StatusCodes.Status409Conflict);
        }

        var file = request.File ?? throw new BusinessRuleException(ErrorCodes.CallRecordingInvalidFile);
        var extension = await ValidateFileAsync(file, cancellationToken);
        var productCode = ResolveProductCode(request.ProductCode, submission.Work.WorkType?.ProductCode);
        var transactionRef = await db.WorkTransactions.AsNoTracking()
            .Where(t => t.WorkId == submission.WorkId && t.Status == WorkTransactionStatus.Matched)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.TransactionCode)
            .FirstOrDefaultAsync(cancellationToken);

        var now = time.GetUtcNow();
        var id = Guid.NewGuid();
        var relativePath = string.Create(CultureInfo.InvariantCulture, $"call-recordings/{now:yyyy}/{now:MM}/{id}{extension}");
        var sha256 = await storage.StoreCallRecordingAsync(file, relativePath, cancellationToken);
        var recording = new CallRecording
        {
            Id = id,
            ResultSubmissionId = submission.Id,
            WorkId = submission.WorkId,
            TransactionRef = transactionRef,
            ProductCode = productCode,
            FilePath = relativePath,
            FileName = Path.GetFileName(file.FileName),
            MimeType = CallRecordingFileValidator.ContentTypeFor(extension),
            SizeBytes = file.Length,
            Sha256 = sha256,
            ProcessingStatus = CallRecordingStatus.Pending,
            UploadedBy = caller.Id,
            UploadedAt = now,
            RetainUntil = now.AddYears(1),
        };

        try
        {
            db.CallRecordings.Add(recording);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The file is already on disk; without a row nothing would ever reference it.
            storage.DeleteCallRecording(relativePath);
            throw;
        }

        LogUploaded(logger, recording.Id, submission.Id, recording.SizeBytes, null);
        return ToDto(recording);
    }

    public async Task<IReadOnlyList<CallRecordingDto>> GetBySubmissionAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var submitterId = await db.ResultSubmissions.AsNoTracking()
            .Where(s => s.Id == submissionId)
            .Select(s => (Guid?)s.SubmitterEmployeeId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Đợt nộp kết quả", submissionId);
        await EnsureCanPlayAsync(caller, submitterId, cancellationToken);

        return await db.CallRecordings.AsNoTracking()
            .Where(r => r.ResultSubmissionId == submissionId)
            .OrderByDescending(r => r.UploadedAt)
            .Select(Projection)
            .ToListAsync(cancellationToken);
    }

    public async Task<CallRecordingDto> GetAsync(Guid recordingId, Guid userId, CancellationToken cancellationToken = default)
    {
        var recording = await LoadAuthorizedAsync(recordingId, userId, cancellationToken);
        return await db.CallRecordings.AsNoTracking()
            .Where(r => r.Id == recording.Id)
            .Select(Projection)
            .SingleAsync(cancellationToken);
    }

    public async Task<CallRecordingStreamDto> OpenStreamAsync(Guid recordingId, Guid userId, CancellationToken cancellationToken = default)
    {
        var recording = await LoadAuthorizedAsync(recordingId, userId, cancellationToken);
        if (recording.ProcessingStatus != CallRecordingStatus.Ready)
        {
            throw new BusinessRuleException(ErrorCodes.CallRecordingNotReady, statusCode: StatusCodes.Status409Conflict);
        }

        var content = await storage.OpenCallRecordingReadAsync(recording.FilePath, cancellationToken);
        return new CallRecordingStreamDto(content, recording.MimeType);
    }

    private static readonly Expression<Func<CallRecording, CallRecordingDto>> Projection = r => new CallRecordingDto(
        r.Id,
        r.ResultSubmissionId,
        r.WorkId,
        r.FileName,
        r.MimeType,
        r.SizeBytes,
        r.DurationMs,
        r.ProductCode,
        r.TransactionRef,
        r.ProcessingStatus.ToEnumString(),
        r.ErrorCode,
        r.UploadedAt,
        r.ProcessedAt,
        r.Flags.Count);

    private static readonly Func<CallRecording, CallRecordingDto> ToDto = Projection.Compile();

    private async Task<CallRecording> LoadAuthorizedAsync(
        Guid recordingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var recording = await db.CallRecordings.AsNoTracking()
            .Include(r => r.ResultSubmission)
            .FirstOrDefaultAsync(r => r.Id == recordingId, cancellationToken)
            ?? throw new NotFoundException("Ghi âm cuộc gọi", recordingId);
        await EnsureCanPlayAsync(caller, recording.ResultSubmission.SubmitterEmployeeId, cancellationToken);
        return recording;
    }

    /// <summary>The submitter, or a holder of <c>call_recording:play</c> at All scope (contract §2).</summary>
    private async Task EnsureCanPlayAsync(Employee caller, Guid submitterId, CancellationToken cancellationToken)
    {
        if (caller.Id == submitterId)
        {
            return;
        }

        var granted = await permissions.GetEffectivePermissionsAsync(caller.Account!.Id, cancellationToken);
        if (AccessScope.LevelFor(granted, "call_recording", "play") < AccessScope.AllLevel)
        {
            throw new ForbiddenException("Bạn không có quyền nghe ghi âm này.");
        }
    }

    /// <summary>Checks size, name, extension and magic bytes; returns the lower-case extension.</summary>
    private async Task<string> ValidateFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(name)?.ToLowerInvariant();
        if (file.Length <= 0
            || file.Length > options.Value.MaxBytes
            || string.IsNullOrWhiteSpace(name)
            || name.Length > MaxFileNameLength
            || !CallRecordingFileValidator.IsKnownExtension(extension))
        {
            throw new BusinessRuleException(ErrorCodes.CallRecordingInvalidFile);
        }

        var header = new byte[CallRecordingFileValidator.HeaderLength];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        if (!CallRecordingFileValidator.HasValidSignature(extension, header.AsSpan(0, read)))
        {
            throw new BusinessRuleException(ErrorCodes.CallRecordingInvalidFile);
        }

        return extension!;
    }

    private string? ResolveProductCode(string? requested, string? workTypeProductCode)
    {
        var productCode = string.IsNullOrWhiteSpace(requested) ? workTypeProductCode : requested.Trim();
        if (productCode is { Length: > MaxProductCodeLength })
        {
            throw new ValidationException($"Mã sản phẩm tối đa {MaxProductCodeLength} ký tự.");
        }

        if (productCode is null && options.Value.RequireProductCode)
        {
            throw new ValidationException("Cần mã sản phẩm cho ghi âm cuộc gọi.");
        }

        return productCode;
    }

    private async Task<Employee> ResolveCallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);
        if (caller?.Account is null || caller.Status != EmployeeStatus.Active || caller.Account.IsLocked)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return caller;
    }
}
