using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.TargetAssignment;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// Branch / department / employee target allocation. Contract: docs/api/TARGET_ASSIGNMENT_CONTRACT.md.
/// Hierarchy, sum, uniqueness and value rules are enforced by the database; the frontend validates the rest.
/// </summary>
public sealed class TargetAssignmentService(
    AppDbContext db,
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
        db.TargetAssignments.Add(target);
        await db.SaveChangesAsync();

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

        db.TargetAssignments.AddRange(children);
        await db.SaveChangesAsync();

        return [.. children.Select(ToDto)];
    }

    public async Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId) =>
        ToDto(await LoadAccessibleAsync(accountId, assignmentId));

    private async Task<TargetAssignment> LoadAccessibleAsync(Guid accountId, Guid id)
    {
        var target = await db.TargetAssignments.AsNoTracking()
            .Include(t => t.Employee)
            .SingleOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("Target", id);

        if (!await IsAdminAsync(accountId))
        {
            var callerDepartmentId = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => a.Employee.DepartmentId)
                .SingleOrDefaultAsync();
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
