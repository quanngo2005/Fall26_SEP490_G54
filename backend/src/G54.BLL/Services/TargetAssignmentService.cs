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
    IRepository<EvaluationPeriod> periodRepository,
    IPermissionService permissions,
    TimeProvider time) : ITargetAssignmentService
{
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

        var children = dto.Allocations!.Select(item =>
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

        return [.. children.Select(ToDto)];
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

        return [.. children.Select(ToDto)];
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

        return [.. targets.Select(ToDto)];
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

    private static TargetAssignmentDto ToDto(TargetAssignment t) => new(
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
        t.UpdatedAt);
}
