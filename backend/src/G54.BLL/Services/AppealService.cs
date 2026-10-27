using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Appeal;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

public sealed class AppealService(
    AppDbContext db,
    IPermissionService permissions,
    IStorageService storage,
    ILogger<AppealService> logger,
    TimeProvider time,
    IOptions<SystemParametersOptions> parameters) : IAppealService
{
    private const int MaxReasonLength = 2000;
    private const int MaxFileCount = 10;
    private static readonly int DepartmentScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Dept);
    private static readonly int BranchScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Branch);
    private static readonly int AllScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.All);

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> LogAppealFiled =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, "AppealFiled"),
            "Appeal audit: Action=FILE, AppealId={AppealId}, SubmissionId={SubmissionId}, EmployeeId={EmployeeId}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> LogAppealWithdrawn =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(2, "AppealWithdrawn"),
            "Appeal audit: Action=WITHDRAW, AppealId={AppealId}, SubmissionId={SubmissionId}, EmployeeId={EmployeeId}");

    public async Task<AppealDetailDto> CreateAppealAsync(
        Guid submissionId,
        Guid userId,
        CreateAppealRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new ValidationException("Lý do khiếu nại là bắt buộc.");
        }

        if (reason.Length > MaxReasonLength)
        {
            throw new ValidationException($"Lý do khiếu nại tối đa {MaxReasonLength} ký tự.");
        }

        if (request.Files is { Count: > MaxFileCount })
        {
            throw new ValidationException($"Số lượng tệp đính kèm tối đa là {MaxFileCount} tệp.");
        }

        var submission = await db.ResultSubmissions.AsNoTracking()
            .Include(s => s.Verifications)
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken)
            ?? throw new NotFoundException("Đợt nộp kết quả", submissionId);

        if (submission.SubmitterEmployeeId != caller.Employee.Id)
        {
            throw new ForbiddenException("Chỉ người đã nộp kết quả mới có quyền khiếu nại.");
        }

        if (submission.Status != SubmissionStatus.Rejected)
        {
            throw new ConflictException(
                $"Chỉ có thể khiếu nại kết quả đã bị từ chối (REJECTED). Trạng thái hiện tại: {submission.Status.ToEnumString()}.",
                ErrorCodes.AppealInvalidState);
        }

        var now = time.GetUtcNow();
        var rejectedAt = submission.Verifications
            .Where(v => v.Decision == VerificationDecision.Rejected)
            .Max(v => v.VerifiedAt)
            ?? submission.UpdatedAt;
        var appealWindowHours = parameters.Value.AppealRejectedHours;
        var appealWindow = TimeSpan.FromHours(appealWindowHours);
        if (now - rejectedAt > appealWindow)
        {
            throw new BusinessRuleException(
                ErrorCodes.AppealWindowExpired,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (await HasAppealInProgressAsync(submissionId, cancellationToken))
        {
            throw AlreadyOpen();
        }

        IReadOnlyList<StoredFileMetadata> uploadedFiles = Array.Empty<StoredFileMetadata>();
        if (request.Files is { Count: > 0 })
        {
            uploadedFiles = await storage.UploadEvidenceFilesAsync(request.Files, cancellationToken);
        }

        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = caller.Employee.Id,
            ResultSubmissionId = submissionId,
            Reason = reason,
            Status = AppealStatus.Open,
            FiledAt = now,
            UpdatedAt = now,
        };
        db.Appeals.Add(appeal);

        foreach (var file in uploadedFiles)
        {
            db.Evidences.Add(new Evidence
            {
                Id = Guid.NewGuid(),
                AppealId = appeal.Id,
                FileName = file.FileName,
                FilePath = file.FilePath,
                MimeType = file.MimeType,
                FileSize = file.FileSize,
                UploadedAt = now,
                UpdatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The files are already on disk; without an appeal row nothing would ever reference them.
            await storage.DeleteEvidenceFilesAsync(uploadedFiles, CancellationToken.None);
            throw;
        }

        LogAppealFiled(logger, appeal.Id, submissionId, caller.Employee.Id, null);

        var created = await LoadAppealAsync(appeal.Id, cancellationToken);
        return MapDetail(created!);
    }

    public async Task<AppealDetailDto> WithdrawAppealAsync(
        Guid appealId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var appeal = await db.Appeals
            .Include(a => a.Employee)
            .Include(a => a.ResolvedByNavigation)
            .Include(a => a.ResultSubmission!)
                .ThenInclude(s => s.Work)
            .Include(a => a.Evidences)
            .FirstOrDefaultAsync(a => a.Id == appealId && a.ResultSubmissionId != null, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);

        if (appeal.EmployeeId != caller.Employee.Id)
        {
            throw new ForbiddenException("Chỉ người đã gửi khiếu nại mới có quyền rút khiếu nại.");
        }

        if (appeal.Status != AppealStatus.Open)
        {
            throw new ConflictException(
                $"Chỉ có thể rút khiếu nại đang mở (OPEN). Trạng thái hiện tại: {appeal.Status.ToEnumString()}.",
                ErrorCodes.AppealInvalidState);
        }

        appeal.Status = AppealStatus.Withdrawn;
        appeal.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        LogAppealWithdrawn(logger, appeal.Id, appeal.ResultSubmissionId!.Value, caller.Employee.Id, null);

        return MapDetail(appeal);
    }

    public async Task<PagedResult<AppealListItemDto>> GetAppealsAsync(
        Guid userId,
        AppealFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var status = EnumApiFormat.ParseEnum<AppealStatus>(filter.Status, "status");
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var visibility = await ResolveVisibilityAsync(caller, cancellationToken);

        var query = db.Appeals.AsNoTracking().Where(a => a.ResultSubmissionId != null);
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (!visibility.SeesAll)
        {
            var ownId = caller.Employee.Id;
            var departmentIds = visibility.DepartmentIds;
            query = query.Where(a => a.EmployeeId == ownId || departmentIds.Contains(a.Employee.DepartmentId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(a => a.FiledAt)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                ResultSubmissionId = a.ResultSubmissionId!.Value,
                a.ResultSubmission!.WorkId,
                WorkCode = a.ResultSubmission.Work.Code,
                WorkTitle = a.ResultSubmission.Work.Title,
                a.ResultSubmission.SubmissionNo,
                a.EmployeeId,
                a.Employee.EmployeeCode,
                EmployeeName = a.Employee.FullName,
                a.Status,
                a.FiledAt,
                a.DecidedAt,
                a.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new AppealListItemDto(
                r.Id,
                r.ResultSubmissionId,
                r.WorkId,
                r.WorkCode,
                r.WorkTitle,
                r.SubmissionNo,
                r.EmployeeId,
                r.EmployeeCode,
                r.EmployeeName,
                r.Status.ToEnumString(),
                r.FiledAt,
                r.DecidedAt,
                r.UpdatedAt))
            .ToList();

        return new PagedResult<AppealListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<AppealDetailDto> GetAppealByIdAsync(
        Guid appealId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var appeal = await LoadAppealAsync(appealId, cancellationToken)
            ?? throw new NotFoundException("Khiếu nại", appealId);

        var visibility = await ResolveVisibilityAsync(caller, cancellationToken);
        var canSee = visibility.SeesAll
            || appeal.EmployeeId == caller.Employee.Id
            || visibility.DepartmentIds.Contains(appeal.Employee.DepartmentId);
        if (!canSee)
        {
            throw new ForbiddenException("Bạn không có quyền xem khiếu nại này.");
        }

        return MapDetail(appeal);
    }

    private static ConflictException AlreadyOpen()
        => new(ErrorCodes.GetDefaultMessage(ErrorCodes.AppealAlreadyOpen), ErrorCodes.AppealAlreadyOpen);

    private Task<bool> HasAppealInProgressAsync(Guid submissionId, CancellationToken cancellationToken)
        => db.Appeals.AnyAsync(
            a => a.ResultSubmissionId == submissionId
                && (a.Status == AppealStatus.Open || a.Status == AppealStatus.UnderReview),
            cancellationToken);

    private Task<Appeal?> LoadAppealAsync(Guid appealId, CancellationToken cancellationToken)
        => db.Appeals.AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.ResolvedByNavigation)
            .Include(a => a.ResultSubmission!)
                .ThenInclude(s => s.Work)
            .Include(a => a.Evidences)
            .FirstOrDefaultAsync(a => a.Id == appealId && a.ResultSubmissionId != null, cancellationToken);

    private static AppealDetailDto MapDetail(Appeal appeal)
    {
        var submission = appeal.ResultSubmission!;
        var evidences = appeal.Evidences
            .OrderBy(e => e.UploadedAt)
            .ThenBy(e => e.FileName, StringComparer.Ordinal)
            .Select(e => new AppealEvidenceDto(
                e.Id,
                e.FileName,
                e.FileSize,
                e.MimeType,
                $"/evidence/{e.Id}/preview",
                $"/evidence/{e.Id}/download"))
            .ToList();

        return new AppealDetailDto(
            appeal.Id,
            submission.Id,
            submission.WorkId,
            submission.Work.Code,
            submission.Work.Title,
            submission.SubmissionNo,
            appeal.EmployeeId,
            appeal.Employee.EmployeeCode,
            appeal.Employee.FullName,
            appeal.Status.ToEnumString(),
            appeal.FiledAt,
            appeal.DecidedAt,
            appeal.UpdatedAt,
            appeal.Reason,
            appeal.Note,
            appeal.ResolvedBy,
            appeal.ResolvedByNavigation?.FullName,
            evidences);
    }

    private async Task<Caller> ResolveCallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);

        if (employee is null || employee.Status != EmployeeStatus.Active)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return new Caller(employee);
    }

    /// <summary>
    /// What the caller may see: always their own appeals, plus those of employees inside the department scope of their
    /// <c>verification:decide</c> permission (Dept = own department tree, Branch = nearest branch tree, All = everyone).
    /// </summary>
    private async Task<Visibility> ResolveVisibilityAsync(Caller caller, CancellationToken cancellationToken)
    {
        var employee = caller.Employee;
        if (employee.Account is null)
        {
            return Visibility.OwnOnly;
        }

        var granted = await permissions.GetEffectivePermissionsAsync(employee.Account.Id, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, "verification", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, "decide", StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level >= AllScopeLevel)
        {
            return Visibility.Everything;
        }

        if (level < DepartmentScopeLevel)
        {
            return Visibility.OwnOnly;
        }

        var departments = await db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => new DepartmentLink(d.ParentDepartmentId, d.LevelType), cancellationToken);
        if (!departments.ContainsKey(employee.DepartmentId))
        {
            return Visibility.OwnOnly;
        }

        var rootId = level >= BranchScopeLevel ? BranchRoot(departments, employee.DepartmentId) : employee.DepartmentId;
        var inScope = departments.Keys
            .Where(departmentId => IsSelfOrDescendant(departments, departmentId, rootId))
            .ToList();
        return new Visibility(false, inScope);
    }

    /// <summary>Nearest BRANCH ancestor-or-self; the root-most ancestor when none exists.</summary>
    private static Guid BranchRoot(Dictionary<Guid, DepartmentLink> departments, Guid departmentId)
    {
        var current = departmentId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            var link = departments[current];
            if (link.Level == DepartmentLevelType.Branch
                || link.ParentId is not { } parent
                || !departments.ContainsKey(parent))
            {
                return current;
            }

            current = parent;
        }

        return current;
    }

    /// <summary>True when <paramref name="rootId"/> is on the path from <paramref name="departmentId"/> up to the top (cycle-safe).</summary>
    private static bool IsSelfOrDescendant(Dictionary<Guid, DepartmentLink> departments, Guid departmentId, Guid rootId)
    {
        var current = departmentId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            if (current == rootId)
            {
                return true;
            }

            if (!departments.TryGetValue(current, out var link) || link.ParentId is not { } parent)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private sealed record Caller(Employee Employee);

    private sealed record Visibility(bool SeesAll, IReadOnlyList<Guid> DepartmentIds)
    {
        public static Visibility Everything { get; } = new(true, Array.Empty<Guid>());

        public static Visibility OwnOnly { get; } = new(false, Array.Empty<Guid>());
    }

    private readonly record struct DepartmentLink(Guid? ParentId, DepartmentLevelType Level);
}
