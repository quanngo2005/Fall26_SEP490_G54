using System.Globalization;
using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Reconciliation;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// See <see cref="IReconciliationService"/>. A transaction code is compared with the imported Core Banking file by the same rules
/// as <see cref="WorkTransactionService"/>; nothing is called out to Core Banking.
/// </summary>
public sealed partial class ReconciliationService(
    AppDbContext db,
    TimeProvider time,
    IPermissionService permissions,
    IOptions<SystemParametersOptions> parameters) : IReconciliationService
{
    public const string NoImportedTransaction = "NO_IMPORTED_TRANSACTION";

    private const int MaxPageSize = 100;
    private const int MaxExportRows = 10_000;
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);

    /// <summary>Every reason a code can carry, as a filter value.</summary>
    public static readonly IReadOnlyList<string> Reasons =
    [
        "EMPLOYEE_MISMATCH",
        "PRODUCT_MISMATCH",
        "CUSTOMER_MISMATCH",
        "REFERRER_MISMATCH",
        "TRANSACTION_BEFORE_HANDOVER",
        "METRIC_TYPE_MISMATCH",
        NoImportedTransaction,
        "KPI_TARGET_NOT_ASSIGNED",
        "KPI_AGGREGATION_NOT_SUPPORTED",
        "PERIOD_LOCKED",
        "REFERRER_NOT_CREDITED",
    ];

    private static readonly string[] Statuses =
    [
        WorkTransactionStatus.NotMatched,
        WorkTransactionStatus.InReview,
        WorkTransactionStatus.Matched,
    ];

    public async Task<ReconciliationListDto> ListAsync(
        Guid userId,
        ReconciliationFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var status = ParseStatus(filter.Status);
        var scope = await ResolveScopeAsync(userId, cancellationToken);

        var filtered = await FilteredAsync(filter, scope.Departments, cancellationToken);

        var byStatus = await filtered
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(string value) => byStatus.Where(x => x.Status == value).Sum(x => x.Count);
        var counts = new ReconciliationCountsDto(
            byStatus.Sum(x => x.Count),
            Count(WorkTransactionStatus.NotMatched),
            Count(WorkTransactionStatus.InReview),
            Count(WorkTransactionStatus.Matched));

        var inStatus = status is null ? filtered : filtered.Where(t => t.Status == status);
        var totalCount = status is null ? counts.All : Count(status);

        // Computed as long: a huge page number would overflow int and reach SQL as a negative OFFSET.
        var offset = (long)(page - 1) * pageSize;
        var items = offset >= totalCount
            ? []
            : await LoadItemsAsync(inStatus, (int)offset, pageSize, cancellationToken);

        return new ReconciliationListDto(
            items,
            totalCount,
            page,
            pageSize,
            counts,
            await OptionsAsync(scope.Departments, cancellationToken));
    }

    public async Task<ReconciliationDetailDto> GetDetailAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var tx = await db.WorkTransactions
            .AsNoTracking()
            .Include(t => t.Work).ThenInclude(w => w.WorkType)
            .Include(t => t.Work).ThenInclude(w => w.AssigneeEmployee).ThenInclude(e => e.JobPosition)
            .Include(t => t.Work).ThenInclude(w => w.AssigneeEmployee).ThenInclude(e => e.Department)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        var work = tx?.Work;
        if (tx is null || work is null
            || (scope.Departments is not null && !scope.Departments.Contains(work.AssigneeEmployee.DepartmentId)))
        {
            // A code outside the caller's scope looks the same as a missing one.
            throw new NotFoundException("Giao dịch đối soát", id);
        }

        var assignee = work.AssigneeEmployee;
        var imported = await db.ImportedTransactions
            .AsNoTracking()
            .Include(i => i.ImportBatch)
            .Include(i => i.Employee)
            .FirstOrDefaultAsync(i => i.TransactionCode == tx.TransactionCode, cancellationToken);
        var importer = imported is null
            ? null
            : await db.Employees.AsNoTracking()
                .Where(e => e.Id == imported.ImportBatch.ImportedBy)
                .Select(e => e.FullName)
                .FirstOrDefaultAsync(cancellationToken);

        var handover = await db.TaskHandovers
            .AsNoTracking()
            .Include(h => h.FromEmployee)
            .Where(h => h.WorkId == work.Id && h.Status == HandoverStatus.Accepted)
            .OrderByDescending(h => h.AcceptedAt ?? h.HandoverAt)
            .FirstOrDefaultAsync(cancellationToken);

        var fields = FieldChecks(tx.TransactionCode, work, assignee, imported, handover);
        var compared = fields.Count(f => f.Matched is not null);
        var matched = fields.Count(f => f.Matched == true);

        var item = ToItem(
            tx,
            work,
            assignee,
            imported?.CustomerCode,
            imported?.ReferEmployeeCode,
            imported?.ProductCode ?? work.WorkType?.ProductCode,
            imported?.Amount,
            imported?.TransactionDate);

        var siblings = await db.WorkTransactions
            .AsNoTracking()
            .Where(t => t.WorkId == work.Id)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.TransactionCode)
            .Select(t => new ReconciliationSiblingDto(t.Id, t.TransactionCode, t.Status, EffectiveReason(t.Status, t.Reason)))
            .ToListAsync(cancellationToken);

        var submissions = await db.ResultSubmissions
            .AsNoTracking()
            .Include(s => s.SubmitterEmployee)
            .Include(s => s.Verifications)
            .Include(s => s.Appeals)
            .Where(s => s.WorkId == work.Id && s.Status != SubmissionStatus.Draft)
            .OrderByDescending(s => s.SubmissionNo)
            .ToListAsync(cancellationToken);
        var latest = submissions.FirstOrDefault();

        var history = await HistoryAsync(tx, work, assignee, imported, importer, submissions, cancellationToken);
        var actions = await ActionsAsync(userId, work, submissions, cancellationToken);

        return new ReconciliationDetailDto(
            item,
            new ReconciliationWorkDto(
                work.Id,
                work.Code,
                work.Title,
                work.Status.ToEnumString(),
                work.WorkType?.Name,
                work.WorkType?.ProductCode,
                work.CustomerName,
                work.CustomerCif),
            latest is null
                ? null
                : new ReconciliationSubmissionDto(latest.Id, latest.SubmissionNo, latest.Status.ToEnumString(), latest.SubmittedAt, latest.SubmitterEmployee.FullName),
            imported is null
                ? null
                : new ReconciliationImportDto(
                    imported.ImportBatchId,
                    imported.ImportBatch.FileName,
                    imported.ImportBatch.ImportedAt,
                    importer,
                    imported.TransactionDate,
                    imported.Amount),
            fields,
            compared,
            matched,
            siblings,
            history,
            AppealOf(latest, time.GetUtcNow()),
            actions);
    }

    /// <summary>The caller's employee and the departments their <c>reconciliation:read</c> scope covers (null = every department).</summary>
    private async Task<(Employee Caller, HashSet<Guid>? Departments)> ResolveScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var granted = await permissions.GetEffectivePermissionsAsync(caller.Account!.Id, cancellationToken);
        var level = AccessScope.LevelFor(granted, "reconciliation", "read");
        if (level < AccessScope.DeptLevel)
        {
            throw new ForbiddenException("Bạn không có quyền xem màn hình đối soát.");
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        return (caller, AccessScope.DepartmentsInScope(level, caller.DepartmentId, hierarchy));
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

    /// <summary>Applies every filter except <c>status</c> (the status cards count across statuses).</summary>
    private async Task<IQueryable<WorkTransaction>> FilteredAsync(
        ReconciliationFilterDto filter,
        HashSet<Guid>? scope,
        CancellationToken cancellationToken)
    {
        var query = db.WorkTransactions.AsNoTracking().AsQueryable();
        if (scope is not null)
        {
            query = query.Where(t => scope.Contains(t.Work.AssigneeEmployee.DepartmentId));
        }

        var from = filter.From;
        var to = filter.To;
        if (filter.PeriodId is { } periodId)
        {
            var period = await db.EvaluationPeriods.AsNoTracking()
                .Where(p => p.Id == periodId)
                .Select(p => new { p.StartDate, p.EndDate })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", periodId);
            from ??= period.StartDate;
            to ??= period.EndDate;
        }

        if (from is { } start && to is { } end && start > end)
        {
            throw new ValidationException("Ngày bắt đầu không được sau ngày kết thúc.");
        }

        if (from is { } fromDate)
        {
            // Npgsql only accepts offset 0 in a query: the Vietnamese day boundaries are converted to UTC.
            var instant = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
            query = query.Where(t => t.CreatedAt >= instant);
        }

        if (to is { } toDate)
        {
            var instant = new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
            query = query.Where(t => t.CreatedAt < instant);
        }

        if (filter.DepartmentId is { } departmentId)
        {
            var subtree = (await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken)).Subtree(departmentId);
            query = query.Where(t => subtree.Contains(t.Work.AssigneeEmployee.DepartmentId));
        }

        if (filter.JobPositionId is { } jobPositionId)
        {
            query = query.Where(t => t.Work.AssigneeEmployee.JobPositionId == jobPositionId);
        }

        if (!string.IsNullOrWhiteSpace(filter.ProductCode))
        {
            var product = filter.ProductCode.Trim();
            // The imported product wins; a code still waiting for its file shows the work type's product.
            query = query.Where(t =>
                db.ImportedTransactions.Any(i => i.TransactionCode == t.TransactionCode && i.ProductCode == product)
                || (t.Work.WorkType != null && t.Work.WorkType.ProductCode == product
                    && !db.ImportedTransactions.Any(i => i.TransactionCode == t.TransactionCode)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Reason))
        {
            var reason = filter.Reason.Trim().ToUpperInvariant();
            if (!Reasons.Contains(reason))
            {
                throw new ValidationException($"Lý do '{filter.Reason}' không hợp lệ.");
            }

            query = reason == NoImportedTransaction
                ? query.Where(t => t.Status == WorkTransactionStatus.InReview && t.Reason == null)
                : query.Where(t => t.Reason == reason);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // ILike is PostgreSQL's case-insensitive LIKE; the user's text is escaped so % and _ match literally.
            var pattern = LikePattern.Contains(filter.Search.Trim());
            query = query.Where(t =>
                EF.Functions.ILike(t.TransactionCode, pattern, LikePattern.Escape)
                || EF.Functions.ILike(t.Work.Code, pattern, LikePattern.Escape)
                || EF.Functions.ILike(t.Work.Title, pattern, LikePattern.Escape)
                || (t.Work.CustomerCif != null && EF.Functions.ILike(t.Work.CustomerCif, pattern, LikePattern.Escape))
                || EF.Functions.ILike(t.Work.AssigneeEmployee.EmployeeCode, pattern, LikePattern.Escape)
                || EF.Functions.ILike(t.Work.AssigneeEmployee.FullName, pattern, LikePattern.Escape)
                || db.ImportedTransactions.Any(i => i.TransactionCode == t.TransactionCode
                    && ((i.CustomerCode != null && EF.Functions.ILike(i.CustomerCode, pattern, LikePattern.Escape))
                        || (i.ReferEmployeeCode != null && EF.Functions.ILike(i.ReferEmployeeCode, pattern, LikePattern.Escape)))));
        }

        return query;
    }

    private async Task<List<ReconciliationItemDto>> LoadItemsAsync(
        IQueryable<WorkTransaction> query,
        int offset,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip(offset)
            .Take(take)
            .Select(t => new
            {
                Tx = t,
                Work = t.Work,
                Assignee = t.Work.AssigneeEmployee,
                JobTitle = t.Work.AssigneeEmployee.JobPosition.Title,
                DepartmentName = t.Work.AssigneeEmployee.Department.Name,
                WorkProduct = t.Work.WorkType != null ? t.Work.WorkType.ProductCode : null,
            })
            .ToListAsync(cancellationToken);

        // The imported rows of the page come in one query; a code with no row yet has none.
        var codes = rows.Select(r => r.Tx.TransactionCode).Distinct().ToList();
        var imported = (await db.ImportedTransactions
                .AsNoTracking()
                .Where(i => codes.Contains(i.TransactionCode))
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.TransactionCode)
            .ToDictionary(g => g.Key, g => g.First());

        return rows
            .Select(r =>
            {
                var row = imported.GetValueOrDefault(r.Tx.TransactionCode);
                return ToItem(
                    r.Tx,
                    r.Work,
                    r.Assignee,
                    row?.CustomerCode,
                    row?.ReferEmployeeCode,
                    row?.ProductCode ?? r.WorkProduct,
                    row?.Amount,
                    row?.TransactionDate,
                    r.JobTitle,
                    r.DepartmentName);
            })
            .ToList();
    }

    private async Task<ReconciliationFilterOptionsDto> OptionsAsync(HashSet<Guid>? scope, CancellationToken cancellationToken)
    {
        var people = db.Employees.AsNoTracking().Where(e => e.Works.Any());
        if (scope is not null)
        {
            people = people.Where(e => scope.Contains(e.DepartmentId));
        }

        var departments = await people
            .Select(e => new { e.Department.Id, e.Department.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        var jobPositions = await people
            .Select(e => new { e.JobPosition.Id, e.JobPosition.Title })
            .Distinct()
            .ToListAsync(cancellationToken);
        var products = await db.WorkTypes.AsNoTracking()
            .Where(w => w.ProductCode != null)
            .Select(w => new { Code = w.ProductCode!, w.Name })
            .Distinct()
            .ToListAsync(cancellationToken);

        return new ReconciliationFilterOptionsDto(
            departments.OrderBy(d => d.Name, StringComparer.CurrentCulture)
                .Select(d => new ReconciliationDepartmentOptionDto(d.Id, d.Name))
                .ToList(),
            jobPositions.OrderBy(j => j.Title, StringComparer.CurrentCulture)
                .Select(j => new ReconciliationJobPositionOptionDto(j.Id, j.Title))
                .ToList(),
            products.GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ReconciliationProductOptionDto(g.Key, g.First().Name))
                .OrderBy(p => p.Code, StringComparer.Ordinal)
                .ToList(),
            Reasons);
    }

    private static ReconciliationItemDto ToItem(
        WorkTransaction tx,
        Work work,
        Employee assignee,
        string? importedCustomer,
        string? importedRefer,
        string? product,
        decimal? amount,
        DateOnly? transactionDate,
        string? jobTitle = null,
        string? departmentName = null) => new(
            tx.Id,
            tx.TransactionCode,
            work.Id,
            work.Code,
            work.Title,
            assignee.Id,
            assignee.EmployeeCode,
            assignee.FullName,
            jobTitle ?? assignee.JobPosition?.Title,
            assignee.DepartmentId,
            departmentName ?? assignee.Department?.Name,
            string.IsNullOrWhiteSpace(importedCustomer) ? work.CustomerCif : importedCustomer,
            importedRefer,
            product,
            amount,
            transactionDate,
            tx.Status,
            EffectiveReason(tx.Status, tx.Reason),
            tx.CreatedAt,
            tx.UpdatedAt);

    /// <summary>A code still waiting for a file has no reason of its own: it is reported as NO_IMPORTED_TRANSACTION.</summary>
    internal static string? EffectiveReason(string status, string? reason) =>
        status == WorkTransactionStatus.InReview && string.IsNullOrWhiteSpace(reason) ? NoImportedTransaction : reason;

    private static string? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return Statuses.Contains(normalized)
            ? normalized
            : throw new ValidationException("Trạng thái đối soát chỉ được là NOT_MATCHED, IN_REVIEW hoặc MATCHED.");
    }

    /// <summary>
    /// Same rules as <see cref="WorkTransactionService"/>: the employee by id, the product ignoring case, the customer when the
    /// work has a CIF, the referrer and the date against the accepted handover. The amount is never declared, so it is only shown.
    /// </summary>
    private static List<ReconciliationFieldDto> FieldChecks(
        string transactionCode,
        Work work,
        Employee assignee,
        ImportedTransaction? imported,
        TaskHandover? handover)
    {
        var expectedProduct = work.WorkType?.ProductCode;
        var fields = new List<ReconciliationFieldDto>
        {
            new(ReconciliationField.TransactionCode, transactionCode, imported?.TransactionCode, imported is null ? null : true),
            new(
                ReconciliationField.Customer,
                work.CustomerCif,
                imported?.CustomerCode,
                imported is null || string.IsNullOrWhiteSpace(work.CustomerCif) || string.IsNullOrWhiteSpace(imported.CustomerCode)
                    ? null
                    : string.Equals(work.CustomerCif.Trim(), imported.CustomerCode.Trim(), StringComparison.OrdinalIgnoreCase)),
            new(ReconciliationField.Employee, assignee.EmployeeCode, imported?.Employee?.EmployeeCode, imported is null ? null : imported.EmployeeId == assignee.Id),
            new(
                ReconciliationField.Product,
                expectedProduct,
                imported?.ProductCode,
                imported is null ? null : string.Equals(expectedProduct, imported.ProductCode, StringComparison.OrdinalIgnoreCase)),
        };

        var fileReferrer = string.IsNullOrWhiteSpace(imported?.ReferEmployeeCode) ? null : imported!.ReferEmployeeCode!.Trim();
        // The processor's own code in the Refer ID column is not a referral.
        if (string.Equals(fileReferrer, assignee.EmployeeCode, StringComparison.OrdinalIgnoreCase))
        {
            fileReferrer = null;
        }

        bool? referralMatched = null;
        if (imported is not null && handover is not null)
        {
            referralMatched = fileReferrer is null
                || string.Equals(fileReferrer, handover.FromEmployee.EmployeeCode, StringComparison.OrdinalIgnoreCase);
        }

        fields.Add(new(ReconciliationField.Referral, handover?.FromEmployee.EmployeeCode, imported?.ReferEmployeeCode, referralMatched));

        if (handover is not null)
        {
            var handoverDate = DateOnly.FromDateTime(handover.HandoverAt.ToOffset(BusinessOffset).DateTime);
            fields.Add(new(
                ReconciliationField.TransactionDate,
                handoverDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                imported?.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                imported is null ? null : imported.TransactionDate >= handoverDate));
        }

        fields.Add(new(
            ReconciliationField.Amount,
            null,
            imported?.Amount.ToString("0.####", CultureInfo.InvariantCulture),
            null));
        return fields;
    }

    private async Task<List<ReconciliationHistoryDto>> HistoryAsync(
        WorkTransaction tx,
        Work work,
        Employee assignee,
        ImportedTransaction? imported,
        string? importer,
        IReadOnlyList<ResultSubmission> submissions,
        CancellationToken cancellationToken)
    {
        var history = new List<ReconciliationHistoryDto>
        {
            new(ReconciliationHistoryKind.CodeAdded, tx.CreatedAt, null, null, assignee.FullName, tx.TransactionCode),
            new(ReconciliationHistoryKind.Reconciled, tx.UpdatedAt, tx.Status, EffectiveReason(tx.Status, tx.Reason), null, null),
        };

        if (imported is not null)
        {
            history.Add(new(ReconciliationHistoryKind.Imported, imported.ImportBatch.ImportedAt, null, null, importer, imported.ImportBatch.FileName));
        }

        var flags = await db.QualityFlags.AsNoTracking()
            .Include(f => f.FlaggedByEmployee)
            .Where(f => f.ResultSubmission.WorkId == work.Id)
            .ToListAsync(cancellationToken);
        history.AddRange(flags.Select(f => new ReconciliationHistoryDto(
            ReconciliationHistoryKind.QualityFlag,
            f.CreatedAt,
            f.Status.ToEnumString(),
            null,
            f.FlaggedByEmployee.FullName,
            $"QA-{f.CodeNo.ToString("D4", CultureInfo.InvariantCulture)}")));

        var risks = await db.RiskEvents.AsNoTracking()
            .Include(r => r.RecordedByNavigation)
            .Where(r => r.WorkId == work.Id)
            .ToListAsync(cancellationToken);
        history.AddRange(risks.Select(r => new ReconciliationHistoryDto(
            ReconciliationHistoryKind.RiskEvent,
            r.RecordedAt,
            r.Status.ToEnumString(),
            null,
            r.RecordedByNavigation.FullName,
            $"RSK-{r.CodeNo.ToString("D4", CultureInfo.InvariantCulture)}")));

        history.AddRange(submissions.SelectMany(s => s.Appeals).Select(a => new ReconciliationHistoryDto(
            ReconciliationHistoryKind.Appeal,
            a.FiledAt,
            a.Status.ToEnumString(),
            null,
            null,
            a.Id.ToString())));

        return history.OrderByDescending(h => h.At).ThenBy(h => h.Kind, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The appeal window of the work's latest submission. An appeal exists only for a rejected submission (APPEAL_CONTRACT.md),
    /// so a code that did not match has no appeal of its own: the employee fixes the code instead.
    /// </summary>
    private ReconciliationAppealDto AppealOf(ResultSubmission? latest, DateTimeOffset now)
    {
        if (latest is null)
        {
            return new ReconciliationAppealDto(ReconciliationAppealState.NotApplicable, null, null, null, null);
        }

        var open = latest.Appeals.FirstOrDefault(a => a.Status is AppealStatus.Open or AppealStatus.UnderReview);
        var rejectedAt = latest.Verifications
            .Where(v => v.Decision == VerificationDecision.Rejected)
            .Max(v => v.VerifiedAt);
        var deadline = latest.Status == SubmissionStatus.Rejected
            ? (rejectedAt ?? latest.UpdatedAt).AddHours(parameters.Value.AppealRejectedHours)
            : (DateTimeOffset?)null;

        if (open is not null)
        {
            return new ReconciliationAppealDto(ReconciliationAppealState.InProgress, deadline, open.Id, open.Status.ToEnumString(), latest.Id);
        }

        if (deadline is { } limit)
        {
            return new ReconciliationAppealDto(
                now <= limit ? ReconciliationAppealState.Available : ReconciliationAppealState.Expired,
                limit,
                null,
                null,
                latest.Id);
        }

        var decided = latest.Appeals.OrderByDescending(a => a.FiledAt).FirstOrDefault();
        return decided is null
            ? new ReconciliationAppealDto(ReconciliationAppealState.NotApplicable, null, null, null, latest.Id)
            : new ReconciliationAppealDto(ReconciliationAppealState.Decided, null, decided.Id, decided.Status.ToEnumString(), latest.Id);
    }

    private async Task<ReconciliationActionsDto> ActionsAsync(
        Guid userId,
        Work work,
        IReadOnlyList<ResultSubmission> submissions,
        CancellationToken cancellationToken)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var granted = await permissions.GetEffectivePermissionsAsync(caller.Account!.Id, cancellationToken);
        var mayFlag = AccessScope.LevelFor(granted, "quality_flag", "create") > 0;
        var mayRecordRisk = AccessScope.LevelFor(granted, "risk_event", "create") > 0 && caller.Id != work.AssigneeEmployeeId;

        // QA can only flag an approved result (QUALITY_ASSURANCE_CONTRACT.md): the newest approved version of the work.
        var approved = submissions.FirstOrDefault(s => s.Status == SubmissionStatus.Approved);
        string? blocked = null;
        if (!mayFlag)
        {
            blocked = ReconciliationFlagBlocked.NoPermission;
        }
        else if (approved is null)
        {
            blocked = ReconciliationFlagBlocked.NoApprovedSubmission;
        }
        else if (approved.SubmitterEmployeeId == caller.Id)
        {
            blocked = ReconciliationFlagBlocked.OwnWork;
        }
        else if (await db.QualityFlags.AnyAsync(
                     f => f.ResultSubmissionId == approved.Id && f.Status == QualityFlagStatus.Open, cancellationToken))
        {
            blocked = ReconciliationFlagBlocked.OpenFlag;
        }

        return new ReconciliationActionsDto(blocked is null, blocked is null ? approved!.Id : null, blocked, mayRecordRisk);
    }
}

internal static class ReconciliationField
{
    public const string TransactionCode = "TRANSACTION_CODE";
    public const string Customer = "CUSTOMER";
    public const string Employee = "EMPLOYEE";
    public const string Product = "PRODUCT";
    public const string Referral = "REFERRAL";
    public const string TransactionDate = "TRANSACTION_DATE";
    public const string Amount = "AMOUNT";
}

internal static class ReconciliationHistoryKind
{
    public const string CodeAdded = "CODE_ADDED";
    public const string Imported = "IMPORTED";
    public const string Reconciled = "RECONCILED";
    public const string QualityFlag = "QUALITY_FLAG";
    public const string RiskEvent = "RISK_EVENT";
    public const string Appeal = "APPEAL";
}

internal static class ReconciliationAppealState
{
    public const string NotApplicable = "NOT_APPLICABLE";
    public const string Available = "AVAILABLE";
    public const string Expired = "EXPIRED";
    public const string InProgress = "IN_PROGRESS";
    public const string Decided = "DECIDED";
}

internal static class ReconciliationFlagBlocked
{
    public const string NoPermission = "NO_PERMISSION";
    public const string NoApprovedSubmission = "NO_APPROVED_SUBMISSION";
    public const string OwnWork = "OWN_WORK";
    public const string OpenFlag = "OPEN_FLAG";
}
