using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Verification;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Department scope of verification:decide. The submitter sits in Phòng A; Phòng B is a sibling under the same branch.
/// </summary>
public sealed class VerificationScopeTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static VerificationService CreateService(AppDbContext context, params UserPermissionDto[] grants) =>
        new(context, NullLogger<VerificationService>.Instance, TimeProvider.System, new FakePermissionService(null, grants));

    private static UserPermissionDto Decide(string scope) => new("verification", "decide", scope, "VERIFICATION_DECIDE");

    private sealed record Scenario(Guid SubmissionId, Account SiblingVerifierAccount, Account SubmitterDeptVerifierAccount);

    private static async Task<Scenario> SeedAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;

        var branch = NewDepartment("CN", DepartmentLevelType.Branch, null, now);
        var deptA = NewDepartment("A", DepartmentLevelType.Department, branch.Id, now);
        var deptB = NewDepartment("B", DepartmentLevelType.Department, branch.Id, now);
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

        var submitter = NewEmployee("EMP-A1", deptA.Id, position.Id, now);
        var deptAVerifier = NewEmployee("EMP-A2", deptA.Id, position.Id, now);
        var siblingVerifier = NewEmployee("EMP-B1", deptB.Id, position.Id, now);
        context.Employees.AddRange(submitter, deptAVerifier, siblingVerifier);

        var deptAVerifierAccount = NewAccount(deptAVerifier, now);
        var siblingVerifierAccount = NewAccount(siblingVerifier, now);
        context.Accounts.AddRange(deptAVerifierAccount, siblingVerifierAccount);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WRK-SCOPE-001",
            Title = "Công việc kiểm thử phạm vi",
            AssigneeEmployeeId = submitter.Id,
            CreatedBy = submitter.Id,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = now,
        };
        context.Works.Add(work);

        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = submitter.Id,
            SubmissionNo = 1,
            Comment = "Kết quả nộp.",
            Status = SubmissionStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
        };
        context.ResultSubmissions.Add(submission);

        await context.SaveChangesAsync();
        return new Scenario(submission.Id, siblingVerifierAccount, deptAVerifierAccount);
    }

    private static Department NewDepartment(string code, DepartmentLevelType level, Guid? parentId, DateTimeOffset now) => new()
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

    private static Employee NewEmployee(string code, Guid departmentId, Guid positionId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = $"Nhân viên {code}",
        Email = $"{code.ToLowerInvariant()}@bpms.com",
        DepartmentId = departmentId,
        JobPositionId = positionId,
        Status = EmployeeStatus.Active,
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private static Account NewAccount(Employee employee, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employee.Id,
        Username = employee.Email,
        PasswordHash = "hash",
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private static readonly SubmitVerificationDecisionDto Approve = new()
    {
        Decision = "APPROVED",
        VerifiedValue = 15m,
        Feedback = "Hồ sơ đầy đủ.",
    };

    [Fact]
    public async Task GetDossier_DeptScopeVerifierOutsideSubmitterDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context, Decide("Dept"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDossierAsync(scenario.SubmissionId, scenario.SiblingVerifierAccount.Id));
    }

    [Fact]
    public async Task GetHistory_DeptScopeVerifierOutsideSubmitterDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context, Decide("Dept"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetHistoryAsync(scenario.SubmissionId, scenario.SiblingVerifierAccount.Id));
    }

    [Fact]
    public async Task SubmitDecision_DeptScopeVerifierOutsideSubmitterDepartment_ThrowsForbiddenAndLeavesSubmissionUntouched()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context, Decide("Dept"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SubmitDecisionAsync(scenario.SubmissionId, scenario.SiblingVerifierAccount.Id, Approve));

        var submission = await context.ResultSubmissions.AsNoTracking().SingleAsync(s => s.Id == scenario.SubmissionId);
        Assert.Equal(SubmissionStatus.Submitted, submission.Status);
        Assert.Empty(await context.Verifications.ToListAsync());
    }

    [Fact]
    public async Task GetDossier_DeptScopeVerifierInSubmitterDepartment_Succeeds()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context, Decide("Dept"));

        var dossier = await service.GetDossierAsync(scenario.SubmissionId, scenario.SubmitterDeptVerifierAccount.Id);

        Assert.Equal(scenario.SubmissionId, dossier.SubmissionId);
    }

    [Fact]
    public async Task GetDossier_BranchScopeVerifierInSiblingDepartment_Succeeds()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context, Decide("Branch"));

        var dossier = await service.GetDossierAsync(scenario.SubmissionId, scenario.SiblingVerifierAccount.Id);

        Assert.Equal(scenario.SubmissionId, dossier.SubmissionId);
    }

    [Fact]
    public async Task GetDossier_OwnScopeVerifierInSubmitterDepartment_ThrowsForbidden()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context, Decide("Own"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDossierAsync(scenario.SubmissionId, scenario.SubmitterDeptVerifierAccount.Id));
    }

    [Fact]
    public async Task GetDossier_CallerWithoutDecidePermission_ThrowsForbidden()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetDossierAsync(scenario.SubmissionId, scenario.SubmitterDeptVerifierAccount.Id));
    }

    [Fact]
    public async Task GetDossier_AllScopeVerifierOutsideSubmitterDepartment_Succeeds()
    {
        using var context = CreateContext();
        var scenario = await SeedAsync(context);
        var service = CreateService(context, Decide(PermissionScopes.All));

        var dossier = await service.GetDossierAsync(scenario.SubmissionId, scenario.SiblingVerifierAccount.Id);

        Assert.Equal(scenario.SubmissionId, dossier.SubmissionId);
    }
}
