using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

/// <summary>KPI request (Branch Manager) and the DRAFT -> PENDING_APPROVAL -> ACTIVE / REJECTED flow.</summary>
public sealed class KpiApprovalServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    private sealed record Env(
        AppDbContext Db,
        KpiDefinitionService Service,
        KpiGroup Group,
        Account Admin,
        Account Manager,
        Account OtherManager);

    private static async Task<Env> CreateEnvAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var department = new Department { Id = Guid.NewGuid(), Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "CV", Title = "Officer", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group", Status = KpiStatus.Active, UpdatedAt = Now };
        db.AddRange(department, position, group);

        Account NewAccount(string code, EmployeeStatus status = EmployeeStatus.Active, bool locked = false)
        {
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = code,
                FullName = code,
                Email = $"{code.ToLowerInvariant()}@bank.com",
                DepartmentId = department.Id,
                JobPositionId = position.Id,
                Status = status,
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            var account = new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "hash", IsLocked = locked, LastSyncAt = Now, UpdatedAt = Now };
            db.AddRange(employee, account);
            return account;
        }

        var admin = NewAccount("ADMIN1");
        var manager = NewAccount("MGR1");
        var otherManager = NewAccount("MGR2");
        await db.SaveChangesAsync();
        return new Env(db, new KpiDefinitionService(db), group, admin, manager, otherManager);
    }

    private static KpiDefinitionSaveDto Kpi(Env env, string code = "NEW-KPI") => new()
    {
        KpiGroupId = env.Group.Id,
        Code = code,
        Name = "New KPI",
        Unit = "VND",
        AggregationType = "SUM",
        Direction = "HIGHER_BETTER",
    };

    private static Task Move(Env env, Account by, Guid id, string status) =>
        env.Service.ChangeStatusAsync(by.Id, id, new KpiStatusDto { Status = status });

    private static async Task<Guid> PendingKpiAsync(Env env, Account maker)
    {
        var created = await env.Service.CreateKpiAsync(Kpi(env));
        await Move(env, maker, created.Id, "PENDING_APPROVAL");
        return created.Id;
    }

    private static int RequestNoteLimit(AppDbContext db) =>
        db.Model.FindEntityType(typeof(KpiDefinition))!.FindProperty(nameof(KpiDefinition.RequestNote))!.GetMaxLength()!.Value;

    private static int RejectReasonLimit(AppDbContext db) =>
        db.Model.FindEntityType(typeof(KpiDefinition))!.FindProperty(nameof(KpiDefinition.RejectReason))!.GetMaxLength()!.Value;

    // ---- request ----------------------------------------------------------------------------------

    [Fact]
    public async Task Request_CreatesADraftKpiRecordingTheManagerAndTheNote()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;

        var created = await env.Service.RequestKpiAsync(
            env.Manager.Id,
            new KpiRequestDto { Kpi = Kpi(env), RequestNote = "  Needed for the new loan product " });

        Assert.Equal("DRAFT", created.Status);
        Assert.Equal("Needed for the new loan product", created.RequestNote);
        Assert.Equal(env.Manager.EmployeeId, created.RequestedBy);
        Assert.Null(created.RejectReason);
        Assert.Equal(created.Id, (await db.KpiDefinitions.SingleAsync()).Id);
    }

    [Fact]
    public async Task Create_ByTheAdmin_HasNoRequester()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;

        var created = await env.Service.CreateKpiAsync(Kpi(env));

        Assert.Null(created.RequestedBy);
        Assert.Null(created.RequestNote);
    }

    [Fact]
    public async Task Request_InvalidInput_IsValidationErrorAndStoresNothing()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var limit = RequestNoteLimit(db);

        var invalid = new[]
        {
            new KpiRequestDto { Kpi = null, RequestNote = "why" },
            new KpiRequestDto { Kpi = Kpi(env), RequestNote = "  " },
            new KpiRequestDto { Kpi = Kpi(env), RequestNote = new string('x', limit + 1) },
        };
        foreach (var dto in invalid)
        {
            await Assert.ThrowsAsync<ValidationException>(() => env.Service.RequestKpiAsync(env.Manager.Id, dto));
        }

        await Assert.ThrowsAsync<ValidationException>(() => env.Service.RequestKpiAsync(
            env.Manager.Id,
            new KpiRequestDto { Kpi = Kpi(env) with { Unit = "" }, RequestNote = "why" }));
        Assert.Empty(db.KpiDefinitions);
    }

    [Fact]
    public async Task Request_NoteAtTheColumnLimit_IsAccepted_AndTheMessageStatesTheRealLimit()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var limit = RequestNoteLimit(db);

        await env.Service.RequestKpiAsync(env.Manager.Id, new KpiRequestDto { Kpi = Kpi(env), RequestNote = new string('x', limit) });
        var error = await Assert.ThrowsAsync<ValidationException>(() => env.Service.RequestKpiAsync(
            env.Manager.Id,
            new KpiRequestDto { Kpi = Kpi(env, "OTHER"), RequestNote = new string('x', limit + 1) }));

        Assert.Contains(limit.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_DuplicateCode_IsConflict()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        await env.Service.CreateKpiAsync(Kpi(env));

        await Assert.ThrowsAsync<ConflictException>(() => env.Service.RequestKpiAsync(
            env.Manager.Id,
            new KpiRequestDto { Kpi = Kpi(env), RequestNote = "why" }));

        Assert.Single(db.KpiDefinitions);
    }

    [Fact]
    public async Task Request_ByAnUnknownLockedOrInactiveAccount_IsForbidden()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var locked = await db.Accounts.SingleAsync(a => a.Id == env.OtherManager.Id);
        locked.IsLocked = true;
        await db.SaveChangesAsync();
        var request = new KpiRequestDto { Kpi = Kpi(env), RequestNote = "why" };

        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service.RequestKpiAsync(Guid.NewGuid(), request));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service.RequestKpiAsync(env.OtherManager.Id, request));

        Assert.Empty(db.KpiDefinitions);
    }

    // ---- status transitions -----------------------------------------------------------------------

    [Theory]
    [InlineData("DRAFT", "ACTIVE")]
    [InlineData("DRAFT", "INACTIVE")]
    [InlineData("PENDING_APPROVAL", "ACTIVE")]
    [InlineData("PENDING_APPROVAL", "REJECTED")]
    [InlineData("PENDING_APPROVAL", "DRAFT")]
    [InlineData("REJECTED", "ACTIVE")]
    [InlineData("ACTIVE", "PENDING_APPROVAL")]
    [InlineData("ACTIVE", "DRAFT")]
    [InlineData("ARCHIVED", "ACTIVE")]
    [InlineData("ARCHIVED", "DRAFT")]
    public async Task StatusEndpoint_RejectsTransitionsThatSkipApproval(string from, string to)
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var created = await env.Service.CreateKpiAsync(Kpi(env));
        (await db.KpiDefinitions.SingleAsync()).Status = Enum.Parse<KpiStatus>(from.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Move(env, env.Admin, created.Id, to));

        Assert.Equal(from, (await env.Service.GetKpiByIdAsync(created.Id))!.Status);
    }

    [Theory]
    [InlineData("DRAFT", "PENDING_APPROVAL")]
    [InlineData("DRAFT", "ARCHIVED")]
    [InlineData("PENDING_APPROVAL", "ARCHIVED")]
    [InlineData("REJECTED", "PENDING_APPROVAL")]
    [InlineData("ACTIVE", "INACTIVE")]
    [InlineData("INACTIVE", "ACTIVE")]
    [InlineData("ACTIVE", "ARCHIVED")]
    public async Task StatusEndpoint_AllowsTheDocumentedTransitions(string from, string to)
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var created = await env.Service.CreateKpiAsync(Kpi(env));
        (await db.KpiDefinitions.SingleAsync()).Status = Enum.Parse<KpiStatus>(from.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);
        await db.SaveChangesAsync();

        await Move(env, env.Admin, created.Id, to);

        Assert.Equal(to, (await env.Service.GetKpiByIdAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task StatusEndpoint_SameStatusIsANoOp_UnknownKpiIsNotFound_UnknownValueIsValidationError()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var created = await env.Service.CreateKpiAsync(Kpi(env));

        await Move(env, env.Admin, created.Id, "draft");
        await Assert.ThrowsAsync<NotFoundException>(() => Move(env, env.Admin, Guid.NewGuid(), "ARCHIVED"));
        await Assert.ThrowsAsync<ValidationException>(() => Move(env, env.Admin, created.Id, "Published"));

        Assert.Equal("DRAFT", (await env.Service.GetKpiByIdAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task SendingForApproval_RecordsTheMakerAndClearsEarlierRejectReason()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var created = await env.Service.CreateKpiAsync(Kpi(env));
        var row = await db.KpiDefinitions.SingleAsync();
        row.Status = KpiStatus.Rejected;
        row.RejectReason = "Unit is wrong";
        await db.SaveChangesAsync();

        await Move(env, env.Admin, created.Id, "PENDING_APPROVAL");

        row = await db.KpiDefinitions.SingleAsync();
        Assert.Equal(env.Admin.EmployeeId, row.SubmittedBy);
        Assert.Null(row.RejectReason);
        Assert.Equal(KpiStatus.PendingApproval, row.Status);
    }

    // ---- approve / reject -------------------------------------------------------------------------

    [Fact]
    public async Task Approve_ActivatesAPendingKpi_AndTheAdminCanThenUseItElsewhere()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var id = await PendingKpiAsync(env, env.Admin);

        await env.Service.ApproveKpiAsync(env.Manager.Id, id);

        Assert.Equal("ACTIVE", (await env.Service.GetKpiByIdAsync(id))!.Status);
    }

    [Fact]
    public async Task Reject_StoresTheTrimmedReasonAndAllowsEditAndResubmission()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var id = await PendingKpiAsync(env, env.Admin);

        await env.Service.RejectKpiAsync(env.Manager.Id, id, new RejectKpiDto { Reason = "  Formula missing " });

        var rejected = (await env.Service.GetKpiByIdAsync(id))!;
        Assert.Equal("REJECTED", rejected.Status);
        Assert.Equal("Formula missing", rejected.RejectReason);

        var edited = await env.Service.UpdateKpiAsync(id, Kpi(env) with { Name = "Fixed" });
        Assert.Equal("Fixed", edited.Name);
        await Move(env, env.Admin, id, "PENDING_APPROVAL");
        var resubmitted = (await env.Service.GetKpiByIdAsync(id))!;
        Assert.Equal("PENDING_APPROVAL", resubmitted.Status);
        Assert.Null(resubmitted.RejectReason);
    }

    [Fact]
    public async Task Reject_ReasonIsRequiredAndLimitedToTheColumn_AndNothingChanges()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var id = await PendingKpiAsync(env, env.Admin);
        var limit = RejectReasonLimit(db);

        await Assert.ThrowsAsync<ValidationException>(() => env.Service.RejectKpiAsync(env.Manager.Id, id, new RejectKpiDto { Reason = " " }));
        var error = await Assert.ThrowsAsync<ValidationException>(
            () => env.Service.RejectKpiAsync(env.Manager.Id, id, new RejectKpiDto { Reason = new string('r', limit + 1) }));

        Assert.Contains(limit.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.Equal("PENDING_APPROVAL", (await env.Service.GetKpiByIdAsync(id))!.Status);
        await env.Service.RejectKpiAsync(env.Manager.Id, id, new RejectKpiDto { Reason = new string('r', limit) });
        Assert.Equal(limit, (await env.Service.GetKpiByIdAsync(id))!.RejectReason!.Length);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("REJECTED")]
    [InlineData("ACTIVE")]
    [InlineData("INACTIVE")]
    [InlineData("ARCHIVED")]
    public async Task ApproveAndReject_OnAKpiThatIsNotPending_AreConflict(string status)
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var created = await env.Service.CreateKpiAsync(Kpi(env));
        (await db.KpiDefinitions.SingleAsync()).Status = Enum.Parse<KpiStatus>(status, ignoreCase: true);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => env.Service.ApproveKpiAsync(env.Manager.Id, created.Id));
        await Assert.ThrowsAsync<ConflictException>(
            () => env.Service.RejectKpiAsync(env.Manager.Id, created.Id, new RejectKpiDto { Reason = "no" }));

        Assert.Equal(status, (await env.Service.GetKpiByIdAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task ApproveAndReject_UnknownKpi_IsNotFound()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;

        await Assert.ThrowsAsync<NotFoundException>(() => env.Service.ApproveKpiAsync(env.Manager.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(
            () => env.Service.RejectKpiAsync(env.Manager.Id, Guid.NewGuid(), new RejectKpiDto { Reason = "no" }));
    }

    [Fact]
    public async Task ApproveAndReject_ByTheMakerWhoSentItForApproval_IsMakerCannotBeChecker()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var id = await PendingKpiAsync(env, env.Manager);

        var approve = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Service.ApproveKpiAsync(env.Manager.Id, id));
        var reject = await Assert.ThrowsAsync<BusinessRuleException>(
            () => env.Service.RejectKpiAsync(env.Manager.Id, id, new RejectKpiDto { Reason = "no" }));

        Assert.Equal(ErrorCodes.MakerCannotBeChecker, approve.ErrorCode);
        Assert.Equal(422, approve.StatusCode);
        Assert.Equal(ErrorCodes.MakerCannotBeChecker, reject.ErrorCode);
        Assert.Equal("PENDING_APPROVAL", (await env.Service.GetKpiByIdAsync(id))!.Status);
    }

    [Fact]
    public async Task Approve_ByTheManagerWhoRequestedTheKpi_IsMakerCannotBeChecker_ButAnotherManagerCan()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var requested = await env.Service.RequestKpiAsync(env.Manager.Id, new KpiRequestDto { Kpi = Kpi(env), RequestNote = "why" });
        await env.Service.UpdateKpiAsync(requested.Id, Kpi(env) with { Name = "Completed by the admin" });
        await Move(env, env.Admin, requested.Id, "PENDING_APPROVAL");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Service.ApproveKpiAsync(env.Manager.Id, requested.Id));
        await env.Service.ApproveKpiAsync(env.OtherManager.Id, requested.Id);

        Assert.Equal(ErrorCodes.MakerCannotBeChecker, error.ErrorCode);
        Assert.Equal("ACTIVE", (await env.Service.GetKpiByIdAsync(requested.Id))!.Status);
    }

    [Fact]
    public async Task Approve_ByAnUnknownOrLockedAccount_IsForbidden()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var id = await PendingKpiAsync(env, env.Admin);
        (await db.Accounts.SingleAsync(a => a.Id == env.OtherManager.Id)).IsLocked = true;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service.ApproveKpiAsync(Guid.NewGuid(), id));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service.ApproveKpiAsync(env.OtherManager.Id, id));

        Assert.Equal("PENDING_APPROVAL", (await env.Service.GetKpiByIdAsync(id))!.Status);
    }

    // ---- editing while under review ---------------------------------------------------------------

    [Fact]
    public async Task Update_WhilePendingApproval_IsConflictAndNothingChanges()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var id = await PendingKpiAsync(env, env.Admin);

        await Assert.ThrowsAsync<ConflictException>(() => env.Service.UpdateKpiAsync(id, Kpi(env) with { Name = "Sneaky change" }));

        Assert.Equal("New KPI", (await env.Service.GetKpiByIdAsync(id))!.Name);
    }

    [Fact]
    public async Task Update_AnActiveKpi_StillTakesEffectAtOnce_KnownLimit()
    {
        var env = await CreateEnvAsync();
        using var db = env.Db;
        var id = await PendingKpiAsync(env, env.Admin);
        await env.Service.ApproveKpiAsync(env.Manager.Id, id);

        var updated = await env.Service.UpdateKpiAsync(id, Kpi(env) with { Name = "Renamed" });

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("ACTIVE", updated.Status);
    }
}
