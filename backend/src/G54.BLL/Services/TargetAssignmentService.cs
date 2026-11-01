using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
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

        var target = NewTarget(dto.KpiDefinitionId!.Value, dto.EvaluationPeriodId!.Value, dto.TargetValue!.Value, dto.ChangeReason);
        target.DepartmentId = dto.DepartmentId;
        await targetRepository.AddAsync(target);
        await targetRepository.SaveChangesAsync();

        return ToDto(target);
    }

    public async Task<IReadOnlyList<TargetAssignmentDto>> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto)
    {
        var parent = await LoadAccessibleAsync(accountId, parentAssignmentId);
        var allocations = dto.Allocations!;

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

        var children = allocations.Select(item =>
        {
            var child = NewTarget(parent.KpiDefinitionId, parent.EvaluationPeriodId, item.TargetValue!.Value, dto.ChangeReason);
            child.BusinessObjectiveId = parent.BusinessObjectiveId;
            child.ParentAssignmentId = parent.Id;
            child.SplitDimension = TargetSplitDimension.Org;
            child.DepartmentId = item.DepartmentId;
            child.EmployeeId = item.EmployeeId;
            return child;
        }).ToList();

        await targetRepository.AddRangeAsync(children);
        await targetRepository.SaveChangesAsync();

        // A brand-new employee target has no feedback yet.
        return [.. children.Select(c => ToDto(c, ComputeFeedbackStatus(c, null)))];
    }

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

        foreach (var periodId in periodIds)
        {
            if (!periodMap.ContainsKey(periodId))
            {
                throw new NotFoundException("Kỳ đánh giá", periodId);
            }

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

        return [.. targets.Select(t => ToDto(t, ComputeFeedbackStatus(t, latestFeedback.GetValueOrDefault(t.Id))))];
    }

    public async Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId) =>
        ToDto(await LoadAccessibleAsync(accountId, assignmentId));

    private async Task<TargetAssignment> LoadAccessibleAsync(Guid accountId, Guid id)
    {
        var target = await targetRepository.GetWithOwnerAsync(id)
            ?? throw new NotFoundException("Target", id);

        if (!await IsAdminAsync(accountId))
        {
            var callerDepartmentId = await targetRepository.GetAccountDepartmentIdAsync(accountId);
            if (callerDepartmentId != (target.DepartmentId ?? target.Employee!.DepartmentId))
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

    public async Task<IReadOnlyList<TargetAssignmentDto>> GetMineAsync(Guid accountId, MyTargetAssignmentQueryDto query)
    {
        var employee = await targetRepository.GetAccountEmployeeAsync(accountId)
            ?? throw new ForbiddenException("Tài khoản chưa gắn với nhân viên.");

        var targets = await targetRepository.ListForEmployeeAsync(employee.Id, query.EvaluationPeriodId, query.KpiDefinitionId);
        var latestFeedback = await feedbackRepository.GetLatestForTargetsAsync([.. targets.Select(t => t.Id)]);

        return [.. targets.Select(t => ToDto(t, ComputeFeedbackStatus(t, latestFeedback.GetValueOrDefault(t.Id))))];
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

    private async Task EnsureCanViewAsync(Guid accountId, TargetAssignment target)
    {
        if (await IsAdminAsync(accountId))
        {
            return;
        }

        var employee = await targetRepository.GetAccountEmployeeAsync(accountId);
        if (employee is not null && target.EmployeeId == employee.Id)
        {
            return;
        }

        // Peers in the same department must not read each other's targets: the org path is for managers only.
        var isManager = await permissions.HasPermissionAsync(accountId, "target", "manage", PermissionScopes.Dept, default);
        var ownerDepartmentId = target.DepartmentId ?? target.Employee?.DepartmentId;
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

    private static TargetAssignmentDto ToDto(TargetAssignment t, string? feedbackStatus = null) => new(
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
        feedbackStatus);
}
