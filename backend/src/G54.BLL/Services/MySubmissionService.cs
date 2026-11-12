using System.Globalization;
using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Submission;
using G54.BLL.Dtos.Verification;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// The caller's own submission history (<c>GET /submissions/mine</c>) and the reconciliation of one submission
/// (<c>GET /submissions/{id}/reconciliation</c>). Only the submitter can read their submissions.
/// </summary>
public sealed class MySubmissionService(
    AppDbContext db,
    TimeProvider time,
    IOptions<SystemParametersOptions> parameters) : IMySubmissionService
{
    private const int MaxPageSize = 100;

    private static readonly string[] MatchStatuses =
    [
        VerificationMatchStatus.None,
        VerificationMatchStatus.Matched,
        VerificationMatchStatus.NotMatched,
        VerificationMatchStatus.InReview,
    ];

    public async Task<PagedResult<MySubmissionListItemDto>> GetMineAsync(
        Guid userId,
        MySubmissionFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var matchStatus = ParseMatchStatus(filter.MatchStatus);
        var employeeId = await ResolveEmployeeIdAsync(userId, cancellationToken);

        // Only the latest version of each work: earlier versions are reachable from the detail's version list.
        var query = db.ResultSubmissions
            .AsNoTracking()
            .Where(s => s.SubmitterEmployeeId == employeeId
                && s.Status != SubmissionStatus.Draft
                && s.SubmissionNo == db.ResultSubmissions
                    .Where(x => x.WorkId == s.WorkId && x.SubmitterEmployeeId == employeeId && x.Status != SubmissionStatus.Draft)
                    .Max(x => x.SubmissionNo));

        if (filter.PeriodId is { } periodId)
        {
            var period = await db.EvaluationPeriods
                .AsNoTracking()
                .Where(p => p.Id == periodId)
                .Select(p => new { p.StartDate, p.EndDate })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", periodId);

            var from = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var toExclusive = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(s => s.SubmittedAt >= from && s.SubmittedAt < toExclusive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            // ILike is PostgreSQL's case-insensitive LIKE; the user's text is escaped so % and _ match literally.
            var pattern = ContainsPattern(filter.Keyword.Trim());
            query = query.Where(s => EF.Functions.ILike(s.Work.Code, pattern, "\\")
                || EF.Functions.ILike(s.Work.Title, pattern, "\\")
                || db.WorkTransactions.Any(t => t.WorkId == s.WorkId && EF.Functions.ILike(t.TransactionCode, pattern, "\\")));
        }

        var rows = query.Select(s => new
        {
            s.Id,
            s.SubmissionNo,
            s.WorkId,
            WorkCode = s.Work.Code,
            WorkTitle = s.Work.Title,
            WorkTypeName = s.Work.WorkType != null ? s.Work.WorkType.Name : null,
            s.SubmittedAt,
            s.UpdatedAt,
            s.Status,
            Total = db.WorkTransactions.Count(t => t.WorkId == s.WorkId),
            Matched = db.WorkTransactions.Count(t => t.WorkId == s.WorkId && t.Status == WorkTransactionStatus.Matched),
            NotMatched = db.WorkTransactions.Count(t => t.WorkId == s.WorkId && t.Status == WorkTransactionStatus.NotMatched),
            FirstCode = db.WorkTransactions
                .Where(t => t.WorkId == s.WorkId)
                .OrderBy(t => t.CreatedAt)
                .ThenBy(t => t.TransactionCode)
                .Select(t => t.TransactionCode)
                .FirstOrDefault(),
            RejectedAt = s.Verifications
                .Where(v => v.Decision == VerificationDecision.Rejected)
                .Max(v => v.VerifiedAt),
            AppealInProgress = s.Appeals.Any(a => a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview),
        });

        rows = matchStatus switch
        {
            VerificationMatchStatus.None => rows.Where(r => r.Total == 0),
            VerificationMatchStatus.Matched => rows.Where(r => r.Total > 0 && r.Matched == r.Total),
            VerificationMatchStatus.NotMatched => rows.Where(r => r.NotMatched > 0),
            VerificationMatchStatus.InReview => rows.Where(r => r.NotMatched == 0 && r.Matched < r.Total),
            _ => rows,
        };

        var totalCount = await rows.CountAsync(cancellationToken);

        // Computed as long: a huge page number would overflow int and reach SQL as a negative OFFSET.
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
        {
            return new PagedResult<MySubmissionListItemDto>([], totalCount, page, pageSize);
        }

        var pageRows = await rows
            .OrderByDescending(r => r.SubmittedAt)
            .ThenBy(r => r.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        var items = pageRows
            .Select(r =>
            {
                var appeal = AppealOf(r.Status, r.RejectedAt, r.UpdatedAt, r.AppealInProgress, null, now);
                return new MySubmissionListItemDto(
                    r.Id,
                    SubmissionCode(r.WorkCode, r.SubmissionNo),
                    r.SubmissionNo,
                    r.WorkId,
                    r.WorkCode,
                    r.WorkTitle,
                    r.WorkTypeName,
                    r.SubmittedAt,
                    r.Status.ToEnumString(),
                    MatchStatusOf(r.Total, r.Matched, r.NotMatched),
                    r.Total,
                    r.Matched,
                    r.FirstCode,
                    appeal.State == MySubmissionAppealState.Available ? appeal.Deadline : null);
            })
            .ToList();

        return new PagedResult<MySubmissionListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<MySubmissionDetailDto> GetDetailAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var employeeId = await ResolveEmployeeIdAsync(userId, cancellationToken);

        var submission = await db.ResultSubmissions
            .AsNoTracking()
            .Include(s => s.Work).ThenInclude(w => w.WorkType)
            .Include(s => s.Work).ThenInclude(w => w.AssigneeEmployee)
            .Include(s => s.Evidences)
            .Include(s => s.Verifications)
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.Status != SubmissionStatus.Draft, cancellationToken)
            ?? throw new NotFoundException("Đợt nộp kết quả", submissionId);

        if (submission.SubmitterEmployeeId != employeeId)
        {
            throw new ForbiddenException("Bạn chỉ xem được các đợt nộp kết quả của chính mình.");
        }

        var work = submission.Work;
        var transactions = await db.WorkTransactions
            .AsNoTracking()
            .Include(t => t.ImportedTransaction!).ThenInclude(i => i.Employee)
            .Where(t => t.WorkId == work.Id)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.TransactionCode)
            .ToListAsync(cancellationToken);

        var importedByCode = await FindImportedByCodeAsync(transactions, cancellationToken);

        var matchStatus = MatchStatusOf(
            transactions.Count,
            transactions.Count(t => t.Status == WorkTransactionStatus.Matched),
            transactions.Count(t => t.Status == WorkTransactionStatus.NotMatched));

        var expectedProduct = work.WorkType?.ProductCode;
        var transactionDtos = transactions
            .Select(t => new MySubmissionTransactionDto(
                t.TransactionCode,
                t.Status,
                t.Reason,
                FieldChecks(
                    t.TransactionCode,
                    t.ImportedTransaction ?? importedByCode.GetValueOrDefault(t.TransactionCode),
                    work.AssigneeEmployee,
                    expectedProduct)))
            .ToList();

        var versions = await db.ResultSubmissions
            .AsNoTracking()
            .Where(s => s.WorkId == work.Id && s.SubmitterEmployeeId == employeeId && s.Status != SubmissionStatus.Draft)
            .OrderByDescending(s => s.SubmissionNo)
            .Select(s => new { s.Id, s.SubmissionNo, s.SubmittedAt, s.Status })
            .ToListAsync(cancellationToken);

        // The match status is per work, so every version shows the work's current reconciliation.
        var versionDtos = versions
            .Select(v => new MySubmissionVersionDto(v.Id, v.SubmissionNo, v.SubmittedAt, v.Status.ToEnumString(), matchStatus))
            .ToList();

        var rejectedAt = submission.Verifications
            .Where(v => v.Decision == VerificationDecision.Rejected)
            .Max(v => v.VerifiedAt);
        var inProgress = await db.Appeals
            .AsNoTracking()
            .Where(a => a.ResultSubmissionId == submission.Id
                && (a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview))
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var window = AppealOf(submission.Status, rejectedAt, submission.UpdatedAt, inProgress is not null, inProgress, time.GetUtcNow());
        var appeal = new MySubmissionAppealDto(window.State, window.Deadline, window.AppealId);

        return new MySubmissionDetailDto(
            submission.Id,
            SubmissionCode(work.Code, submission.SubmissionNo),
            submission.SubmissionNo,
            work.Id,
            work.Code,
            work.Title,
            work.WorkType?.Name,
            submission.SubmittedAt,
            submission.Status.ToEnumString(),
            matchStatus,
            submission.Evidences
                .Select(e => new SubmissionEvidenceDto(e.Id, e.FileName, e.FilePath, e.MimeType, e.FileSize, e.UploadedAt))
                .ToList(),
            transactionDtos,
            versionDtos,
            appeal);
    }

    /// <summary>
    /// Only a MATCHED code is linked to its imported row, so a code rejected for the employee or the product
    /// (and one waiting for a target) is looked up by its code, the same way the reconcile itself finds it.
    /// </summary>
    private async Task<Dictionary<string, ImportedTransaction>> FindImportedByCodeAsync(
        IReadOnlyList<WorkTransaction> transactions,
        CancellationToken cancellationToken)
    {
        var unlinked = transactions
            .Where(t => t.ImportedTransaction is null)
            .Select(t => t.TransactionCode)
            .Distinct()
            .ToList();
        if (unlinked.Count == 0)
        {
            return [];
        }

        var rows = await db.ImportedTransactions
            .AsNoTracking()
            .Include(i => i.Employee)
            .Where(i => unlinked.Contains(i.TransactionCode))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(i => i.TransactionCode).ToDictionary(g => g.Key, g => g.First());
    }

    private static IReadOnlyList<MySubmissionFieldCheckDto> FieldChecks(
        string transactionCode,
        ImportedTransaction? imported,
        Employee assignee,
        string? expectedProduct)
    {
        if (imported is null)
        {
            // Not in any import yet: nothing to compare, and the UI shows "waiting for data".
            return
            [
                new(MySubmissionField.TransactionCode, transactionCode, null, null),
                new(MySubmissionField.Employee, assignee.EmployeeCode, null, null),
                new(MySubmissionField.Product, expectedProduct, null, null),
                new(MySubmissionField.Amount, null, null, null),
            ];
        }

        return
        [
            new(MySubmissionField.TransactionCode, transactionCode, imported.TransactionCode,
                string.Equals(transactionCode, imported.TransactionCode, StringComparison.Ordinal)),
            // Same rules as WorkTransactionService.ReconcileAsync: the employee by id, the product ignoring case.
            new(MySubmissionField.Employee, assignee.EmployeeCode, imported.Employee.EmployeeCode,
                imported.EmployeeId == assignee.Id),
            new(MySubmissionField.Product, expectedProduct, imported.ProductCode,
                string.Equals(expectedProduct, imported.ProductCode, StringComparison.OrdinalIgnoreCase)),
            new(MySubmissionField.Amount, null, imported.Amount.ToString("0.####", CultureInfo.InvariantCulture), null),
        ];
    }

    /// <summary>Rejected submissions can be appealed until <c>AppealRejectedHours</c> after the rejection.</summary>
    private (string State, DateTimeOffset? Deadline, Guid? AppealId) AppealOf(
        SubmissionStatus status,
        DateTimeOffset? rejectedAt,
        DateTimeOffset updatedAt,
        bool appealInProgress,
        Guid? appealId,
        DateTimeOffset now)
    {
        if (status != SubmissionStatus.Rejected)
        {
            return (MySubmissionAppealState.NotApplicable, null, null);
        }

        var deadline = (rejectedAt ?? updatedAt).AddHours(parameters.Value.AppealRejectedHours);
        if (appealInProgress)
        {
            return (MySubmissionAppealState.InProgress, deadline, appealId);
        }

        return now <= deadline
            ? (MySubmissionAppealState.Available, deadline, null)
            : (MySubmissionAppealState.Expired, deadline, null);
    }

    private async Task<Guid> ResolveEmployeeIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employeeId = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == userId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => (Guid?)a.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        return employeeId ?? throw new UnauthorizedException("Không xác định được nhân sự từ tài khoản hiện tại.");
    }

    /// <summary>LIKE pattern matching <paramref name="text"/> anywhere, with backslash as the escape character.</summary>
    private static string ContainsPattern(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private static string SubmissionCode(string workCode, int submissionNo) => $"#SUB-{workCode}-V{submissionNo}";

    private static string MatchStatusOf(int total, int matched, int notMatched) =>
        total == 0 ? VerificationMatchStatus.None
        : notMatched > 0 ? VerificationMatchStatus.NotMatched
        : matched == total ? VerificationMatchStatus.Matched
        : VerificationMatchStatus.InReview;

    private static string? ParseMatchStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return MatchStatuses.Contains(normalized)
            ? normalized
            : throw new ValidationException("Trạng thái đối soát chỉ được là NONE, MATCHED, NOT_MATCHED hoặc IN_REVIEW.");
    }
}
