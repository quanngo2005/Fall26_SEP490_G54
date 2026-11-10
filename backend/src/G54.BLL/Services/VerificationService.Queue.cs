using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Verification;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

// Verification queue (GET /verifications): submissions waiting for a decision, limited to the caller's verification:decide scope.
public sealed partial class VerificationService
{
    private const int MaxQueuePageSize = 100;

    private static readonly SubmissionStatus[] QueueStatuses = [SubmissionStatus.Submitted, SubmissionStatus.UnderReview];

    private static readonly string[] MatchStatuses =
    [
        VerificationMatchStatus.None,
        VerificationMatchStatus.Matched,
        VerificationMatchStatus.NotMatched,
        VerificationMatchStatus.InReview,
    ];

    public async Task<PagedResult<VerificationQueueItemDto>> GetQueueAsync(
        Guid actorAccountId,
        VerificationQueueFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxQueuePageSize);
        var statuses = ParseQueueStatuses(filter.Status);
        var matchStatus = ParseMatchStatus(filter.MatchStatus);

        var (callerEmployeeId, departments) = await ResolveQueueScopeAsync(actorAccountId, filter.DepartmentId, cancellationToken);

        // Maker-Checker (BR-02): the caller cannot decide their own submission, so it never shows up in their queue.
        var query = db.ResultSubmissions
            .AsNoTracking()
            .Where(s => statuses.Contains(s.Status) && s.SubmitterEmployeeId != callerEmployeeId);

        if (departments is not null)
        {
            query = query.Where(s => departments.Contains(s.SubmitterEmployee.DepartmentId));
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            // ILike is PostgreSQL's case-insensitive LIKE; the user's text is escaped so % and _ match literally.
            var pattern = ContainsPattern(filter.Keyword.Trim());
            query = query.Where(s => EF.Functions.ILike(s.Work.Code, pattern, "\\")
                || EF.Functions.ILike(s.Work.Title, pattern, "\\")
                || EF.Functions.ILike(s.SubmitterEmployee.EmployeeCode, pattern, "\\")
                || EF.Functions.ILike(s.SubmitterEmployee.FullName, pattern, "\\"));
        }

        var rows = query.Select(s => new
        {
            s.Id,
            s.SubmissionNo,
            s.WorkId,
            WorkCode = s.Work.Code,
            WorkTitle = s.Work.Title,
            WorkTypeName = s.Work.WorkType != null ? s.Work.WorkType.Name : null,
            s.SubmitterEmployeeId,
            SubmitterCode = s.SubmitterEmployee.EmployeeCode,
            SubmitterName = s.SubmitterEmployee.FullName,
            DepartmentId = s.SubmitterEmployee.DepartmentId,
            DepartmentName = s.SubmitterEmployee.Department.Name,
            s.SubmittedAt,
            s.Status,
            Total = db.WorkTransactions.Count(t => t.WorkId == s.WorkId),
            Matched = db.WorkTransactions.Count(t => t.WorkId == s.WorkId && t.Status == WorkTransactionStatus.Matched),
            NotMatched = db.WorkTransactions.Count(t => t.WorkId == s.WorkId && t.Status == WorkTransactionStatus.NotMatched),
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
            return new PagedResult<VerificationQueueItemDto>([], totalCount, page, pageSize);
        }

        var pageRows = await rows
            .OrderBy(r => r.SubmittedAt)
            .ThenBy(r => r.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = pageRows
            .Select(r => new VerificationQueueItemDto(
                r.Id,
                SubmissionCode(r.WorkCode, r.SubmissionNo),
                r.SubmissionNo,
                r.WorkId,
                r.WorkCode,
                r.WorkTitle,
                r.WorkTypeName,
                r.SubmitterEmployeeId,
                r.SubmitterCode,
                r.SubmitterName,
                r.DepartmentId,
                r.DepartmentName,
                r.SubmittedAt,
                r.Status.ToEnumString(),
                MatchStatusOf(r.Total, r.Matched, r.NotMatched),
                r.Total,
                r.Matched))
            .ToList();

        return new PagedResult<VerificationQueueItemDto>(items, totalCount, page, pageSize);
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

    private static SubmissionStatus[] ParseQueueStatuses(string? value)
    {
        var status = EnumApiFormat.ParseEnum<SubmissionStatus>(value, "status");
        if (status is null)
        {
            return QueueStatuses;
        }

        return QueueStatuses.Contains(status.Value)
            ? [status.Value]
            : throw new ValidationException("Trạng thái hàng chờ thẩm định chỉ được là SUBMITTED hoặc UNDER_REVIEW.");
    }

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

    /// <summary>
    /// The caller's employee and the departments whose submitters they may verify (null = every department).
    /// Own scope or no grant cannot verify anyone, so the queue is refused like the dossier.
    /// </summary>
    private async Task<(Guid EmployeeId, HashSet<Guid>? Departments)> ResolveQueueScopeAsync(
        Guid actorAccountId,
        Guid? requestedDepartmentId,
        CancellationToken cancellationToken)
    {
        var caller = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == actorAccountId && !a.IsLocked && a.Employee.Status == EmployeeStatus.Active)
            .Select(a => new { a.EmployeeId, a.Employee.DepartmentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedException("Không xác định được thông tin nhân sự thẩm định từ tài khoản hiện tại.");

        var granted = await permissions.GetEffectivePermissionsAsync(actorAccountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "verification", "decide");
        if (level < AccessScope.DeptLevel)
        {
            throw new ForbiddenException("Bạn không có quyền thẩm định kết quả của nhân sự trong phạm vi phòng ban nào.");
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        var inScope = AccessScope.DepartmentsInScope(level, caller.DepartmentId, hierarchy);
        if (requestedDepartmentId is not { } departmentId)
        {
            return (caller.EmployeeId, inScope);
        }

        if (!hierarchy.Nodes.ContainsKey(departmentId))
        {
            throw new NotFoundException("Phòng ban", departmentId);
        }

        if (inScope is not null && !inScope.Contains(departmentId))
        {
            throw new ForbiddenException("Bạn không có quyền thẩm định kết quả của phòng ban này.");
        }

        return (caller.EmployeeId, hierarchy.Subtree(departmentId).ToHashSet());
    }
}
