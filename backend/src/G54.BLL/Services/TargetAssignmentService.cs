using System.Globalization;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.TargetAssignment;
using G54.DAL.Entities;
using G54.DAL.Repositories;

namespace G54.BLL.Services;

/// <summary>
/// Branch / department / employee target allocation. Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.
/// Hierarchy, sum, uniqueness and value rules are enforced by the database; the frontend validates the rest.
/// </summary>
public sealed class TargetAssignmentService(
    ITargetAssignmentRepository targetRepository,
    ITargetAssignmentFeedbackRepository feedbackRepository,
    IRepository<EvaluationPeriod> periodRepository,
    IPermissionService permissions,
    IWorkingDayCalendar workingDayCalendar,
    TimeProvider time) : ITargetAssignmentService
{
    /// <summary>
    /// Whole-number (integer) KPI units, e.g. "Hợp đồng", "Khách hàng". KpiDefinition has no explicit
    /// decimal-places field, so this is a documented assumption: any other unit (VNĐ, %, ...) is treated
    /// as decimal and rounded to 4 dp in the breakdown. See docs/api/TARGET_ASSIGNMENT_CONTRACT.md.
    /// KpiDefinition.Unit là free-text do người dùng nhập, nên liệt kê cả dạng có dấu và không dấu
    /// (so khớp không phân biệt hoa thường, sau Trim). Unit không có trong danh sách → coi là số thập phân.
    /// </summary>
    private static readonly HashSet<string> WholeNumberUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "hợp đồng", "hop dong", "khách hàng", "khach hang", "giao dịch", "giao dich",
        "contract", "contracts", "customer", "customers", "cái", "cai", "lần", "lan",
    };
    public async Task<TargetAssignmentDto> CreateRootAsync(Guid accountId, CreateTargetAssignmentDto dto)
    {
        if (!await IsAdminAsync(accountId))
        {
            throw new ForbiddenException("Chỉ quản trị viên mới được giao target gốc cho chi nhánh.");
        }

        PeriodLockGuard.EnsureWritable(await periodRepository.GetByIdAsync(dto.EvaluationPeriodId!.Value));

        var target = NewTarget(dto.KpiDefinitionId!.Value, dto.EvaluationPeriodId!.Value, dto.TargetValue!.Value, dto.ChangeReason);
        target.DepartmentId = dto.DepartmentId;
        await targetRepository.AddAsync(target);
        await targetRepository.SaveChangesAsync();

        return ToDto(target);
    }

    public async Task<IReadOnlyList<TargetAssignmentDto>> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto)
    {
        var parent = await LoadAccessibleAsync(accountId, parentAssignmentId);
        PeriodLockGuard.EnsureWritable(await periodRepository.GetByIdAsync(parent.EvaluationPeriodId));
        var allocations = dto.Allocations!;

        await ValidateOwnersAsync(parent, allocations);

        var children = await NewChildrenAsync(parent, allocations, dto.ChangeReason);

        await targetRepository.AddRangeAsync(children);
        await targetRepository.SaveChangesAsync();

        // A brand-new employee target has no feedback yet.
        return [.. children.Select(c => ToDtoWithFeedback(c, null))];
    }

    /// <summary>Checks who would receive a share of <paramref name="parent"/>: one owner per row, no repeats, employees and departments inside it, nobody already holding an active target.</summary>
    private async Task ValidateOwnersAsync(TargetAssignment parent, IReadOnlyCollection<TargetAllocationItemDto> allocations)
    {
        var departmentIds = new List<Guid>();
        var employeeIds = new List<Guid>();
        foreach (var item in allocations)
        {
            var hasDepartment = item.DepartmentId.HasValue;
            var hasEmployee = item.EmployeeId.HasValue;
            if (hasDepartment == hasEmployee)
            {
                throw new ValidationException("Mỗi dòng phân bổ phải có đúng một trong hai: phòng ban hoặc nhân viên.");
            }

            if (hasDepartment)
            {
                departmentIds.Add(item.DepartmentId!.Value);
            }
            else
            {
                employeeIds.Add(item.EmployeeId!.Value);
            }
        }

        if (departmentIds.Count != departmentIds.Distinct().Count())
        {
            throw new ValidationException("Phòng ban không được trùng nhau trong danh sách phân bổ.");
        }

        if (employeeIds.Count != employeeIds.Distinct().Count())
        {
            throw new ValidationException("Nhân viên không được trùng nhau trong danh sách phân bổ.");
        }

        if (employeeIds.Count > 0)
        {
            if (parent.DepartmentId is null)
            {
                throw new ValidationException("Chỉ có thể phân bổ target cho nhân viên từ target của phòng ban.");
            }

            var descendantDepartmentIds = await targetRepository.GetDescendantDepartmentIdsAsync(parent.DepartmentId.Value);
            var distinctEmployeeIds = employeeIds.Distinct().ToList();
            var employees = await targetRepository.GetEmployeesAsync(distinctEmployeeIds);
            var employeeMap = employees.ToDictionary(e => e.Id);

            foreach (var employeeId in distinctEmployeeIds)
            {
                if (!employeeMap.TryGetValue(employeeId, out var employee))
                {
                    throw new NotFoundException("Nhân viên", employeeId);
                }

                if (employee.Status != EmployeeStatus.Active)
                {
                    throw new ValidationException($"Nhân viên {employee.FullName} không ở trạng thái hoạt động.");
                }

                if (!descendantDepartmentIds.Contains(employee.DepartmentId))
                {
                    throw new ValidationException($"Nhân viên {employee.FullName} không thuộc phòng ban của target cha.");
                }
            }

            // The database allows one ACTIVE target per KPI/period/employee; report it as 409 instead of a 500.
            var taken = await targetRepository.GetActiveOwnerTargetsAsync(parent.KpiDefinitionId, parent.EvaluationPeriodId, [], distinctEmployeeIds);
            var takenEmployee = taken.FirstOrDefault(t => t.EmployeeId.HasValue);
            if (takenEmployee is not null)
            {
                throw new ConflictException(
                    $"Nhân viên {employeeMap[takenEmployee.EmployeeId!.Value].FullName} đã có target đang áp dụng cho KPI và kỳ này.");
            }
        }

        if (departmentIds.Count > 0)
        {
            var taken = await targetRepository.GetActiveOwnerTargetsAsync(parent.KpiDefinitionId, parent.EvaluationPeriodId, departmentIds, []);
            if (taken.Count > 0)
            {
                throw new ConflictException($"Phòng ban {taken[0].DepartmentId} đã có target đang áp dụng cho KPI và kỳ này.");
            }
        }
    }

    public async Task<IReadOnlyList<TargetAssignmentDto>> ReallocateAsync(Guid accountId, Guid parentAssignmentId, ReallocateTargetDto dto)
    {
        var parent = await LoadAccessibleAsync(accountId, parentAssignmentId);
        PeriodLockGuard.EnsureWritable(await periodRepository.GetByIdAsync(parent.EvaluationPeriodId));

        var current = (await targetRepository.GetCurrentChildrenAsync(parentAssignmentId))
            .Where(child => child.Status == TargetAssignmentStatus.Active)
            .ToDictionary(OwnerOf);
        var owners = dto.Allocations!
            .Select(item => item.EmployeeId.HasValue == item.DepartmentId.HasValue ? null : item.EmployeeId ?? item.DepartmentId)
            .ToList();
        if (owners.Contains(null) || owners.Distinct().Count() != owners.Count || !current.Keys.All(key => owners.Contains(key)))
        {
            throw new ValidationException("Danh sách điều chỉnh phải gồm mọi đối tượng đã được phân bổ, mỗi đối tượng một lần.");
        }

        var requested = dto.Allocations!.ToDictionary(item => (item.EmployeeId ?? item.DepartmentId)!.Value, item => item.TargetValue!.Value);
        var added = dto.Allocations!.Where(item => !current.ContainsKey((item.EmployeeId ?? item.DepartmentId)!.Value)).ToList();
        if (added.Count > 0)
        {
            await ValidateOwnersAsync(parent, added);
        }

        var now = time.GetUtcNow();
        var replacements = new List<TargetAssignment>();
        foreach (var (owner, child) in current)
        {
            var value = requested[owner];
            if (value == child.TargetValue)
            {
                continue;
            }

            child.Status = TargetAssignmentStatus.Superseded;
            child.UpdatedAt = now;
            foreach (var descendant in await targetRepository.GetActiveDescendantsAsync(child.Id))
            {
                descendant.Status = TargetAssignmentStatus.Superseded;
                descendant.UpdatedAt = now;
            }

            var next = NewTarget(child.KpiDefinitionId, child.EvaluationPeriodId, value, dto.ChangeReason);
            next.Version = child.Version + 1;
            next.BusinessObjectiveId = child.BusinessObjectiveId;
            next.ParentAssignmentId = child.ParentAssignmentId;
            next.SplitDimension = child.SplitDimension;
            next.DepartmentId = child.DepartmentId;
            next.EmployeeId = child.EmployeeId;
            replacements.Add(next);
        }

        replacements.AddRange(await NewChildrenAsync(parent, added, dto.ChangeReason));

        await targetRepository.AddRangeAsync(replacements);
        await targetRepository.SaveChangesAsync();

        return [.. replacements.Select(next => ToDtoWithFeedback(next, null))];
    }

    /// <summary>
    /// ACTIVE shares of <paramref name="parent"/> for departments or employees. An owner that held a share of this KPI and
    /// period before (now superseded) gets the next version, as the database allows each version only once.
    /// </summary>
    private async Task<List<TargetAssignment>> NewChildrenAsync(
        TargetAssignment parent,
        IReadOnlyCollection<TargetAllocationItemDto> items,
        string? reason)
    {
        var owners = items.Select(item => (item.EmployeeId ?? item.DepartmentId)!.Value).ToList();
        var versions = await targetRepository.GetLatestVersionsAsync(parent.KpiDefinitionId, parent.EvaluationPeriodId, owners);
        return [.. items.Select(item =>
        {
            var child = NewTarget(parent.KpiDefinitionId, parent.EvaluationPeriodId, item.TargetValue!.Value, reason);
            child.Version = versions.GetValueOrDefault((item.EmployeeId ?? item.DepartmentId)!.Value) + 1;
            child.BusinessObjectiveId = parent.BusinessObjectiveId;
            child.ParentAssignmentId = parent.Id;
            child.SplitDimension = TargetSplitDimension.Org;
            child.DepartmentId = item.DepartmentId;
            child.EmployeeId = item.EmployeeId;
            return child;
        })];
    }

    private static Guid OwnerOf(TargetAssignment target) => (target.EmployeeId ?? target.DepartmentId)!.Value;

    public async Task<IReadOnlyList<TargetAssignmentDto>> AllocateByPeriodAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetByPeriodDto dto)
    {
        var parent = await LoadAccessibleAsync(accountId, parentAssignmentId);

        // Check for duplicate evaluationPeriodId in request
        var periodIds = dto.Allocations!.Select(a => a.EvaluationPeriodId!.Value).ToList();
        if (periodIds.Count != periodIds.Distinct().Count())
        {
            throw new ValidationException("Kỳ đánh giá không được trùng nhau trong danh sách.");
        }

        // Load all evaluation periods to check hierarchy
        var allPeriods = await periodRepository.ListAsync();
        var periodMap = allPeriods.ToDictionary(p => p.Id);

        // New rows are written under the parent period and into every requested period, so none of them may be locked.
        PeriodLockGuard.EnsureWritable(periodMap.GetValueOrDefault(parent.EvaluationPeriodId));

        foreach (var periodId in periodIds)
        {
            if (!periodMap.TryGetValue(periodId, out var targetPeriod))
            {
                throw new NotFoundException("Kỳ đánh giá", periodId);
            }

            PeriodLockGuard.EnsureWritable(targetPeriod);

            // Check if the period is a descendant of the parent period
            var current = periodId;
            var found = false;
            var maxIterations = 100; // Prevent infinite loops
            var iterations = 0;

            while (iterations < maxIterations && periodMap.TryGetValue(current, out var period))
            {
                iterations++;
                if (current == parent.EvaluationPeriodId)
                {
                    found = true;
                    break;
                }

                if (period.ParentPeriodId.HasValue)
                {
                    current = period.ParentPeriodId.Value;
                }
                else
                {
                    break;
                }
            }

            if (!found || current == periodId)
            {
                throw new ValidationException("Kỳ đánh giá phải là hậu duệ của kỳ cha.");
            }
        }

        var children = dto.Allocations!.Select(item =>
        {
            var child = NewTarget(parent.KpiDefinitionId, item.EvaluationPeriodId!.Value, item.TargetValue!.Value, dto.ChangeReason);
            child.BusinessObjectiveId = parent.BusinessObjectiveId;
            child.ParentAssignmentId = parent.Id;
            child.SplitDimension = TargetSplitDimension.Period;
            child.DepartmentId = parent.DepartmentId;
            child.EmployeeId = parent.EmployeeId;
            return child;
        }).ToList();

        await targetRepository.AddRangeAsync(children);
        await targetRepository.SaveChangesAsync();

        return [.. children.Select(c => ToDto(c))];
    }

    public async Task<IReadOnlyList<TargetAssignmentDto>> ListAsync(Guid accountId, TargetAssignmentQueryDto query)
    {
        // Parse status if provided
        TargetAssignmentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            status = EnumApiFormat.ParseEnum<TargetAssignmentStatus>(query.Status, "status");
        }

        // Determine accessible departments
        IReadOnlyCollection<Guid>? ownerDepartmentIds = null;

        if (!await IsAdminAsync(accountId))
        {
            var callerDepartmentId = await targetRepository.GetAccountDepartmentIdAsync(accountId)
                ?? throw new ForbiddenException("Bạn không có quyền xem target.");
            ownerDepartmentIds = await targetRepository.GetDescendantDepartmentIdsAsync(callerDepartmentId);
        }

        // If departmentId is specified, validate it
        if (query.DepartmentId.HasValue)
        {
            if (!await targetRepository.DepartmentExistsAsync(query.DepartmentId.Value))
            {
                throw new NotFoundException("Phòng ban", query.DepartmentId.Value);
            }

            if (ownerDepartmentIds is not null && !ownerDepartmentIds.Contains(query.DepartmentId.Value))
            {
                throw new ForbiddenException("Bạn không có quyền truy cập phòng ban này.");
            }

            ownerDepartmentIds = [query.DepartmentId.Value];
        }

        var targets = await targetRepository.ListAsync(
            query.KpiDefinitionId,
            query.EvaluationPeriodId,
            status,
            ownerDepartmentIds);

        var employeeOwnedIds = targets.Where(t => t.EmployeeId.HasValue).Select(t => t.Id).ToList();
        var latestFeedback = await feedbackRepository.GetLatestForTargetsAsync(employeeOwnedIds);

        return [.. targets.Select(t => ToDtoWithFeedback(t, latestFeedback.GetValueOrDefault(t.Id)))];
    }

    public async Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId) =>
        ToDto(await LoadAccessibleAsync(accountId, assignmentId));

    private async Task<TargetAssignment> LoadAccessibleAsync(Guid accountId, Guid id)
    {
        var target = await targetRepository.GetWithOwnerAsync(id)
            ?? throw new NotFoundException("Target", id);

        if (!await IsAdminAsync(accountId))
        {
            // A manager owns the targets of their own department and of every department below it.
            var callerDepartmentId = await targetRepository.GetAccountDepartmentIdAsync(accountId);
            IReadOnlyList<Guid> scope = callerDepartmentId is null
                ? []
                : await targetRepository.GetDescendantDepartmentIdsAsync(callerDepartmentId.Value);
            if (!scope.Contains(target.DepartmentId ?? target.Employee!.DepartmentId))
            {
                throw new ForbiddenException("Bạn không có quyền với target của đơn vị này.");
            }
        }

        return target;
    }

    public async Task<TargetAssignmentBreakdownDto> GetBreakdownAsync(Guid accountId, Guid assignmentId, string? granularity)
    {
        var effectiveGranularity = NormalizeGranularity(granularity);

        var target = await targetRepository.GetWithOwnerAndContextAsync(assignmentId)
            ?? throw new NotFoundException("Target", assignmentId);

        await EnsureCanViewAsync(accountId, target);

        var start = target.EvaluationPeriod.StartDate;
        var end = target.EvaluationPeriod.EndDate;
        var workingDays = await workingDayCalendar.GetWorkingDaysAsync(target.EmployeeId, start, end);
        var isWholeNumber = WholeNumberUnits.Contains(target.KpiDefinition.Unit.Trim());

        var daySplit = TargetBreakdownCalculator.SplitByMonthThenDay(target.TargetValue, workingDays, isWholeNumber);
        var items = TargetBreakdownCalculator.Aggregate(daySplit, effectiveGranularity);

        return new TargetAssignmentBreakdownDto(
            target.Id,
            start,
            end,
            target.TargetValue,
            workingDays.Count,
            effectiveGranularity,
            items);
    }

    public async Task<SuggestedDailyTargetDto> GetSuggestedDailyAsync(Guid accountId, Guid employeeId, Guid evaluationPeriodId)
    {
        var owner = (await targetRepository.GetEmployeesAsync([employeeId])).SingleOrDefault()
            ?? throw new NotFoundException("Nhân viên", employeeId);
        var period = await periodRepository.GetByIdAsync(evaluationPeriodId)
            ?? throw new NotFoundException("Kỳ đánh giá", evaluationPeriodId);

        await EnsureCanViewAsync(accountId, employeeId, owner.DepartmentId);

        var workingDays = await workingDayCalendar.GetWorkingDaysAsync(employeeId, period.StartDate, period.EndDate);
        var targets = await targetRepository.ListActiveWithKpiForEmployeeAsync(employeeId, evaluationPeriodId);

        var items = targets
            .OrderBy(t => t.KpiDefinition.Code, StringComparer.Ordinal)
            .Select(t => ToSuggestedDailyItem(t, workingDays))
            .ToList();

        return new SuggestedDailyTargetDto(employeeId, evaluationPeriodId, period.StartDate, period.EndDate, workingDays.Count, items);
    }

    /// <summary>
    /// Only a SUM KPI where more is better has a target that is "spread over the days"; every other KPI (average,
    /// ratio, lower-is-better, on-target) keeps its target value and gets no per-day figures.
    /// </summary>
    private static SuggestedDailyTargetItemDto ToSuggestedDailyItem(TargetAssignment target, IReadOnlyList<DateOnly> workingDays)
    {
        var kpi = target.KpiDefinition;
        var isWholeNumber = WholeNumberUnits.Contains(kpi.Unit.Trim());
        var spreadable = kpi.AggregationType == KpiAggregationType.Sum
            && kpi.Direction == KpiDirection.HigherBetter
            && workingDays.Count > 0;
        if (!spreadable)
        {
            return new SuggestedDailyTargetItemDto(
                target.Id, kpi.Id, kpi.Code, kpi.Unit, isWholeNumber, target.TargetValue, null, null, []);
        }

        var daySplit = TargetBreakdownCalculator.SplitByMonthThenDay(target.TargetValue, workingDays, isWholeNumber);
        var months = TargetBreakdownCalculator.Aggregate(daySplit, "MONTH")
            .Select(ToSuggestedDailyMonth)
            .ToList();
        var daily = PerDay(target.TargetValue, workingDays.Count);

        return new SuggestedDailyTargetItemDto(
            target.Id, kpi.Id, kpi.Code, kpi.Unit, isWholeNumber, target.TargetValue, daily, PerWeek(daily), months);
    }

    private static SuggestedDailyMonthDto ToSuggestedDailyMonth(TargetBreakdownItemDto month)
    {
        var daily = PerDay(month.Value, month.WorkingDays);
        return new SuggestedDailyMonthDto(month.Start.ToString("yyyy-MM", CultureInfo.InvariantCulture), month.WorkingDays, month.Value, daily, PerWeek(daily));
    }

    /// <summary>Display figure only (4 dp): the stored split is whole units, so an integer KPI may show a fraction here.</summary>
    private static decimal PerDay(decimal value, int workingDays) =>
        Math.Round(value / workingDays, 4, MidpointRounding.AwayFromZero);

    private static decimal PerWeek(decimal daily) => daily * 5;

    public async Task<IReadOnlyList<TargetAssignmentDto>> GetMineAsync(Guid accountId, MyTargetAssignmentQueryDto query)
    {
        var employee = await targetRepository.GetAccountEmployeeAsync(accountId)
            ?? throw new ForbiddenException("Tài khoản chưa gắn với nhân viên.");

        var targets = await targetRepository.ListForEmployeeAsync(employee.Id, query.EvaluationPeriodId, query.KpiDefinitionId);
        var latestFeedback = await feedbackRepository.GetLatestForTargetsAsync([.. targets.Select(t => t.Id)]);

        return [.. targets.Select(t => ToDtoWithFeedback(t, latestFeedback.GetValueOrDefault(t.Id)))];
    }

    public async Task<TargetAssignmentFeedbackDto> ConfirmAsync(Guid accountId, Guid assignmentId)
    {
        var (target, employee) = await LoadOwnedByCallerAsync(accountId, assignmentId);

        var latest = await feedbackRepository.GetLatestAsync(target.Id);
        if (latest is { Type: TargetAssignmentFeedbackType.Confirm } && latest.CreatedAt >= target.UpdatedAt)
        {
            throw new ConflictException("Target này đã được xác nhận.");
        }

        var feedback = new TargetAssignmentFeedback
        {
            TargetAssignmentId = target.Id,
            EmployeeId = employee.Id,
            Type = TargetAssignmentFeedbackType.Confirm,
            CreatedAt = time.GetUtcNow(),
        };
        await feedbackRepository.AddAsync(feedback);
        await feedbackRepository.SaveChangesAsync();

        return ToFeedbackDto(feedback);
    }

    public async Task<TargetAssignmentFeedbackDto> CommentAsync(Guid accountId, Guid assignmentId, CreateTargetFeedbackCommentDto dto)
    {
        var (target, employee) = await LoadOwnedByCallerAsync(accountId, assignmentId);

        if (string.IsNullOrWhiteSpace(dto.Comment) || dto.Comment.Length > 500)
        {
            throw new ValidationException("Góp ý là bắt buộc và không vượt quá 500 ký tự.");
        }

        var feedback = new TargetAssignmentFeedback
        {
            TargetAssignmentId = target.Id,
            EmployeeId = employee.Id,
            Type = TargetAssignmentFeedbackType.Comment,
            Comment = dto.Comment,
            CreatedAt = time.GetUtcNow(),
        };
        await feedbackRepository.AddAsync(feedback);
        await feedbackRepository.SaveChangesAsync();

        return ToFeedbackDto(feedback);
    }

    public async Task<IReadOnlyList<TargetAssignmentFeedbackDto>> GetFeedbackAsync(Guid accountId, Guid assignmentId)
    {
        var target = await targetRepository.GetWithOwnerAsync(assignmentId)
            ?? throw new NotFoundException("Target", assignmentId);

        await EnsureCanViewAsync(accountId, target);

        var feedback = await feedbackRepository.ListForTargetAsync(target.Id);
        return [.. feedback.Select(ToFeedbackDto)];
    }

    private async Task<(TargetAssignment Target, Employee Employee)> LoadOwnedByCallerAsync(Guid accountId, Guid assignmentId)
    {
        var target = await targetRepository.GetWithOwnerAsync(assignmentId)
            ?? throw new NotFoundException("Target", assignmentId);

        var employee = await targetRepository.GetAccountEmployeeAsync(accountId)
            ?? throw new ForbiddenException("Tài khoản chưa gắn với nhân viên.");

        if (target.EmployeeId != employee.Id)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác với target này.");
        }

        return (target, employee);
    }

    private Task EnsureCanViewAsync(Guid accountId, TargetAssignment target) =>
        EnsureCanViewAsync(accountId, target.EmployeeId, target.DepartmentId ?? target.Employee?.DepartmentId);

    private async Task EnsureCanViewAsync(Guid accountId, Guid? ownerEmployeeId, Guid? ownerDepartmentId)
    {
        if (await IsAdminAsync(accountId))
        {
            return;
        }

        var employee = await targetRepository.GetAccountEmployeeAsync(accountId);
        if (employee is not null && ownerEmployeeId == employee.Id)
        {
            return;
        }

        // Peers in the same department must not read each other's targets: the org path is for managers only.
        var isManager = await permissions.HasPermissionAsync(accountId, "target", "manage", PermissionScopes.Dept, default);
        if (!isManager || employee is null || ownerDepartmentId is null ||
            !(await targetRepository.GetDescendantDepartmentIdsAsync(employee.DepartmentId)).Contains(ownerDepartmentId.Value))
        {
            throw new ForbiddenException("Bạn không có quyền với target của đơn vị này.");
        }
    }

    private static string NormalizeGranularity(string? granularity)
    {
        if (string.IsNullOrWhiteSpace(granularity))
        {
            return "MONTH";
        }

        var normalized = granularity.Trim().ToUpperInvariant();
        if (normalized is not ("MONTH" or "WEEK" or "DAY"))
        {
            throw new ValidationException("Granularity phải là MONTH, WEEK hoặc DAY.");
        }

        return normalized;
    }

    /// <summary>NOT_VIEWED | CONFIRMED | COMMENTED. CONFIRMED only counts when the confirmation is newer than the target's last update.</summary>
    private static string? ComputeFeedbackStatus(TargetAssignment target, TargetAssignmentFeedback? latest)
    {
        if (target.EmployeeId is null)
        {
            return null;
        }

        if (latest is null)
        {
            return "NOT_VIEWED";
        }

        if (latest.Type == TargetAssignmentFeedbackType.Confirm && latest.CreatedAt >= target.UpdatedAt)
        {
            return "CONFIRMED";
        }

        return latest.Type == TargetAssignmentFeedbackType.Comment ? "COMMENTED" : "NOT_VIEWED";
    }

    private static TargetAssignmentFeedbackDto ToFeedbackDto(TargetAssignmentFeedback f) => new(
        f.Id, f.TargetAssignmentId, f.EmployeeId, f.Type.ToEnumString(), f.Comment, f.CreatedAt);

    private Task<bool> IsAdminAsync(Guid accountId) =>
        permissions.HasPermissionAsync(accountId, "target", "manage", "All", default);

    private TargetAssignment NewTarget(Guid kpiId, Guid periodId, decimal value, string? reason)
    {
        var now = time.GetUtcNow();
        return new TargetAssignment
        {
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = periodId,
            Version = 1,
            TargetValue = value,
            Status = TargetAssignmentStatus.Active,
            AssignedAt = now,
            ApprovedAt = now,
            ChangeReason = reason,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Employee-owned rows carry the feedback status and, once answered, when.</summary>
    private static TargetAssignmentDto ToDtoWithFeedback(TargetAssignment t, TargetAssignmentFeedback? latest)
    {
        var status = ComputeFeedbackStatus(t, latest);
        return ToDto(t, status, status is "CONFIRMED" or "COMMENTED" ? latest?.CreatedAt : null);
    }

    private static TargetAssignmentDto ToDto(
        TargetAssignment t,
        string? feedbackStatus = null,
        DateTimeOffset? feedbackAt = null) => new(
        t.Id,
        t.KpiDefinitionId,
        t.EvaluationPeriodId,
        t.DepartmentId,
        t.EmployeeId,
        t.ParentAssignmentId,
        t.SplitDimension?.ToEnumString(),
        t.Version,
        t.TargetValue,
        t.Status.ToEnumString(),
        t.AssignedAt,
        t.ApprovedAt,
        t.ChangeReason,
        t.CreatedAt,
        t.UpdatedAt,
        feedbackStatus,
        feedbackAt);
}
