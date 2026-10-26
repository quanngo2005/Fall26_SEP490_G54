using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Scope checks of EvaluationService. Phòng A (manager + staff) and Phòng B (staff) are siblings under one branch.
/// Gated calls that are allowed fail later on the missing evaluation period, which proves the scope gate passed.
/// </summary>
public sealed class EvaluationScopeTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EvaluationService CreateService(AppDbContext context, params UserPermissionDto[] grants)
    {
        DowngradeTestData.Seed(context);
        return new(context, NullLogger<EvaluationService>.Instance, new FakePermissionService(null, grants));
    }

    private static UserPermissionDto Grant(string action, string scope) => new("evaluation", action, scope, "EVALUATION_" + action.ToUpperInvariant());

    private sealed record Scenario(
        Guid DeptAId,
        Guid DeptBId,
        Employee ManagerA,
        Employee StaffA,
        Employee StaffB,
        Account ManagerAAccount,
        Account StaffAAccount);

    private static async Task<Scenario> SeedAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        Department Dept(string code, DepartmentLevelType level, Guid? parentId) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Đơn vị {code}",
            LevelType = level,
            ParentDepartmentId = parentId,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var branch = Dept("CN", DepartmentLevelType.Branch, null);
        var deptA = Dept("A", DepartmentLevelType.Department, branch.Id);
        var deptB = Dept("B", DepartmentLevelType.Department, branch.Id);
        context.Departments.AddRange(branch, deptA, deptB);

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "P-SCOPE",
            Title = "Chuyên viên",
            Status = RecordStatus.Active,
            UpdatedAt = now,
        };
        context.JobPositions.Add(position);

        Employee Emp(string code, Guid departmentId) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Nhân viên {code}",
            Email = $"{code.ToLowerInvariant()}@bpms.com",
            DepartmentId = departmentId,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var managerA = Emp("MGR-A", deptA.Id);
        var staffA = Emp("STF-A", deptA.Id);
        var staffB = Emp("STF-B", deptB.Id);
        context.Employees.AddRange(managerA, staffA, staffB);

        Account Acc(Employee e) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = e.Id,
            Username = e.Email,
            PasswordHash = "hash",
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var managerAAccount = Acc(managerA);
        var staffAAccount = Acc(staffA);
        context.Accounts.AddRange(managerAAccount, staffAAccount, Acc(staffB));

        await context.SaveChangesAsync();
        return new Scenario(deptA.Id, deptB.Id, managerA, staffA, staffB, managerAAccount, staffAAccount);
    }

    [Fact]
    public async Task GetEvaluationForm_EmployeeReadingColleague_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Own));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetEvaluationFormAsync(s.StaffB.Id, Guid.NewGuid(), s.StaffAAccount.Id));
    }

    [Fact]
    public async Task GetEvaluationForm_EmployeeReadingOwnForm_PassesScopeGate()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Own));

        // The period is missing on purpose: reaching the period lookup means the scope gate allowed the read.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetEvaluationFormAsync(s.StaffA.Id, Guid.NewGuid(), s.StaffAAccount.Id));
    }

    [Fact]
    public async Task GetEvaluationForm_DeptManagerReadingStaffOutsideDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetEvaluationFormAsync(s.StaffB.Id, Guid.NewGuid(), s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetEvaluationForm_DeptManagerReadingStaffInDepartment_PassesScopeGate()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetEvaluationFormAsync(s.StaffA.Id, Guid.NewGuid(), s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task SubmitEvaluation_DeptManagerForStaffOutsideDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("submit", PermissionScopes.Dept));

        var request = new SubmitEvaluationRequestDto(s.StaffB.Id, Guid.NewGuid(), Comment: "Nhận xét");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitEvaluationAsync(request, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task SubmitEvaluation_CallerWithoutSubmitPermission_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        var request = new SubmitEvaluationRequestDto(s.StaffA.Id, Guid.NewGuid(), Comment: "Nhận xét");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitEvaluationAsync(request, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEmployees_DeptManagerRequestingOtherDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentEmployeesForEvaluationAsync(Guid.NewGuid(), s.DeptBId, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentEmployees_DeptManagerOwnDepartment_Succeeds()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        var rows = await service.GetDepartmentEmployeesForEvaluationAsync(Guid.NewGuid(), null, s.ManagerAAccount.Id);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(s.DeptAId, row.DepartmentId));
    }

    [Fact]
    public async Task GetDepartmentEmployees_EmployeeWithOwnScope_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Own));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentEmployeesForEvaluationAsync(Guid.NewGuid(), null, s.StaffAAccount.Id));
    }

    [Fact]
    public async Task SubmitDepartmentCalibration_DeptManagerForOtherDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("calibrate", PermissionScopes.Dept));

        var request = new SubmitCalibrationRequestDto(s.DeptBId, Guid.NewGuid(), [], IsDraft: true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitDepartmentCalibrationAsync(request, s.ManagerAAccount.Id));
    }

    [Fact]
    public async Task GetDepartmentCalibration_DeptManagerForOtherDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var service = CreateService(context, Grant("read", PermissionScopes.Dept));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDepartmentCalibrationAsync(Guid.NewGuid(), s.DeptBId, null, s.ManagerAAccount.Id));
    }
}
