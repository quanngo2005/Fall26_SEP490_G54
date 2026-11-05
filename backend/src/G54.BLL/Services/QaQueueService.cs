using System.Globalization;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Quality;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// The QA queue screen (SCR-REV-06): quality flags and 5% samples in one list with one set of tabs and filters.
/// The queue is small (QA reviews a sample, not everything), so both sources are loaded filtered and merged in memory.
/// </summary>
public sealed class QaQueueService(AppDbContext db) : IQaQueueService
{
    public const string PendingTab = "PENDING";
    public const string ConfirmedTab = "CONFIRMED";
    public const string RejectedTab = "REJECTED";

    /// <summary>Days QA has to handle an item, counted from the moment it was raised.</summary>
    public const int ReviewDays = 5;

    private const int MaxPageSize = 100;
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);

    public async Task<QaQueueDto> GetQueueAsync(QaQueueFilterDto filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var tab = ParseTab(filter.Tab);
        var kind = ParseKind(filter.Kind);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var departmentIds = filter.DepartmentId is { } departmentId
            ? (await DepartmentHierarchy.LoadAsync(db.Departments, cancellationToken)).Subtree(departmentId)
            : null;
        (DateTimeOffset From, DateTimeOffset To)? range = null;
        if (filter.PeriodId is { } periodId)
        {
            var period = await db.EvaluationPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", periodId);
            range = (
                new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), BusinessOffset),
                new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset));
        }

        var items = new List<QaQueueItemDto>();
        var flagQuery = FlagQuery();
        if (departmentIds is not null)
        {
            flagQuery = flagQuery.Where(f => departmentIds.Contains(f.Employee.DepartmentId));
        }

        if (range is { } flagRange)
        {
            flagQuery = flagQuery.Where(f => f.CreatedAt >= flagRange.From && f.CreatedAt < flagRange.To);
        }

        items.AddRange((await flagQuery.ToListAsync(cancellationToken)).Select(ToItem));

        // A sample that produced a flag is shown through that flag.
        var sampleQuery = SampleQuery().Where(x => x.QualityFlag == null);
        if (departmentIds is not null)
        {
            sampleQuery = sampleQuery.Where(x => departmentIds.Contains(x.SubmitterEmployee.DepartmentId));
        }

        if (range is { } sampleRange)
        {
            sampleQuery = sampleQuery.Where(x => x.SampledAt >= sampleRange.From && x.SampledAt < sampleRange.To);
        }

        items.AddRange((await sampleQuery.ToListAsync(cancellationToken)).Select(ToItem));

        var scoped = items.Where(i => (kind is null || i.Type == kind) && Matches(i, filter.Search)).ToList();

        var counts = new QaQueueCountsDto(
            scoped.Count(i => i.Tab == PendingTab),
            scoped.Count(i => i.Tab == ConfirmedTab),
            scoped.Count(i => i.Tab == RejectedTab));
        var inTab = scoped.Where(i => i.Tab == tab)
            .OrderBy(i => tab == PendingTab ? i.DueAt : DateTimeOffset.MaxValue)
            .ThenByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .ToList();
        var pageItems = inTab.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new QaQueueDto(pageItems, inTab.Count, page, pageSize, counts, await DepartmentOptionsAsync(cancellationToken));
    }

    private async Task<List<QaDepartmentOptionDto>> DepartmentOptionsAsync(CancellationToken cancellationToken)
    {
        // Project to anonymous types: a DTO constructor inside Distinct() cannot be translated to SQL.
        var fromFlags = await db.QualityFlags.AsNoTracking()
            .Select(f => new { f.Employee.Department.Id, f.Employee.Department.Name })
            .Distinct().ToListAsync(cancellationToken);
        var fromSamples = await db.QaSamples.AsNoTracking()
            .Select(x => new { x.SubmitterEmployee.Department.Id, x.SubmitterEmployee.Department.Name })
            .Distinct().ToListAsync(cancellationToken);
        return fromFlags.Concat(fromSamples)
            .DistinctBy(d => d.Id)
            .OrderBy(d => d.Name, StringComparer.CurrentCulture)
            .Select(d => new QaDepartmentOptionDto(d.Id, d.Name))
            .ToList();
    }

    public async Task<QaQueueDetailDto> GetItemAsync(string kind, Guid id, CancellationToken cancellationToken = default)
    {
        var parsedKind = ParseKind(kind) ?? throw new ValidationException("Loại mục QA phải là FLAG hoặc SAMPLE.");
        QaQueueItemDto item;
        ResultSubmission submission;
        string? reason;
        string? reworkComment = null;
        DateTimeOffset? reviewedAt;
        string? reviewedBy = null;
        if (parsedKind == "FLAG")
        {
            var flag = await FlagQuery().FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException("Cờ chất lượng", id);
            item = ToItem(flag);
            submission = flag.ResultSubmission;
            reason = flag.Reason;
            reworkComment = flag.ReworkComment;
            reviewedAt = flag.Status == QualityFlagStatus.Open ? null : flag.UpdatedAt;
        }
        else
        {
            var sample = await SampleQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new NotFoundException("Mẫu kiểm tra QA", id);
            item = ToItem(sample);
            submission = sample.ResultSubmission;
            reason = sample.Comment;
            reviewedAt = sample.ReviewedAt;
            reviewedBy = sample.ReviewedByEmployee?.FullName;
        }

        var evidences = await db.Evidences.AsNoTracking()
            .Where(e => e.ResultSubmissionId == submission.Id)
            .OrderBy(e => e.FileName)
            .ToListAsync(cancellationToken);
        var transactions = await db.WorkTransactions.AsNoTracking()
            .Include(t => t.ImportedTransaction).ThenInclude(i => i!.ImportBatch)
            .Where(t => t.WorkId == submission.WorkId)
            .OrderBy(t => t.TransactionCode)
            .ToListAsync(cancellationToken);
        var jobTitle = await db.Employees.AsNoTracking()
            .Where(e => e.Id == item.EmployeeId)
            .Select(e => e.JobPosition.Title)
            .FirstOrDefaultAsync(cancellationToken);

        return new QaQueueDetailDto(
            item,
            reason,
            reworkComment,
            reviewedAt,
            reviewedBy,
            submission.Comment,
            submission.SubmittedAt,
            jobTitle,
            evidences.Select(e => new QaEvidenceDto(e.Id, e.FileName, e.FileSize ?? 0, e.MimeType ?? "application/octet-stream")).ToList(),
            transactions.Select(t => new QaTransactionDto(
                t.TransactionCode,
                t.Status,
                t.Reason,
                t.ImportedTransaction?.ImportBatch.FileName,
                t.ImportedTransaction?.ProductCode,
                t.ImportedTransaction?.Amount,
                t.ImportedTransaction?.TransactionDate)).ToList());
    }

    private IQueryable<QualityFlag> FlagQuery() => db.QualityFlags.AsNoTracking()
        .Include(f => f.ResultSubmission).ThenInclude(s => s.Work)
        .Include(f => f.Employee).ThenInclude(e => e.Department)
        .Include(f => f.QaSample);

    private IQueryable<QaSample> SampleQuery() => db.QaSamples.AsNoTracking()
        .Include(x => x.ResultSubmission).ThenInclude(s => s.Work)
        .Include(x => x.SubmitterEmployee).ThenInclude(e => e.Department)
        .Include(x => x.ReviewedByEmployee)
        .Include(x => x.QualityFlag);

    private static QaQueueItemDto ToItem(QualityFlag f) => new(
        "FLAG",
        f.QaSample is null ? "FLAG" : "SAMPLE",
        f.Id,
        Code(f.QaSample?.CodeNo ?? f.CodeNo),
        f.QaSample is null ? "MANUAL_FLAG" : "SAMPLE_FAILED",
        f.ResultSubmissionId,
        f.ResultSubmission.SubmissionNo,
        f.ResultSubmission.WorkId,
        f.ResultSubmission.Work.Code,
        f.ResultSubmission.Work.Title,
        f.EmployeeId,
        f.Employee.EmployeeCode,
        f.Employee.FullName,
        f.Employee.Department?.Name,
        f.Status switch
        {
            QualityFlagStatus.Open => PendingTab,
            QualityFlagStatus.Dismissed => ConfirmedTab,
            _ => RejectedTab,
        },
        f.Status.ToEnumString(),
        f.Severity.ToEnumString(),
        f.CreatedAt,
        f.CreatedAt.AddDays(ReviewDays));

    private static QaQueueItemDto ToItem(QaSample x) => new(
        "SAMPLE",
        "SAMPLE",
        x.Id,
        Code(x.CodeNo),
        "RANDOM_SAMPLE",
        x.ResultSubmissionId,
        x.ResultSubmission.SubmissionNo,
        x.ResultSubmission.WorkId,
        x.ResultSubmission.Work.Code,
        x.ResultSubmission.Work.Title,
        x.SubmitterEmployeeId,
        x.SubmitterEmployee.EmployeeCode,
        x.SubmitterEmployee.FullName,
        x.SubmitterEmployee.Department?.Name,
        x.Status switch
        {
            QaSampleStatus.Pending => PendingTab,
            QaSampleStatus.Passed => ConfirmedTab,
            _ => RejectedTab,
        },
        x.Status.ToEnumString(),
        null,
        x.SampledAt,
        x.SampledAt.AddDays(ReviewDays));

    private static string Code(long no) => $"QA-{no.ToString("D4", CultureInfo.InvariantCulture)}";

    private static bool Matches(QaQueueItemDto item, string? search)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var digits = text.StartsWith("QA-", StringComparison.OrdinalIgnoreCase) ? text[3..] : text;
        if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && string.Equals(item.Code, Code(number), StringComparison.Ordinal))
        {
            return true;
        }

        return new[] { item.Code, item.WorkCode, item.WorkTitle, item.EmployeeCode, item.EmployeeName }
            .Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ParseKind(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim().ToUpperInvariant() is var k && k is "FLAG" or "SAMPLE"
            ? k
            : throw new ValidationException("Loại mục QA phải là FLAG hoặc SAMPLE.");

    private static string ParseTab(string? value) => string.IsNullOrWhiteSpace(value)
        ? PendingTab
        : value.Trim().ToUpperInvariant() is var t && t is PendingTab or ConfirmedTab or RejectedTab
            ? t
            : throw new ValidationException("Tab phải là PENDING, CONFIRMED hoặc REJECTED.");
}
