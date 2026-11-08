using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

public sealed class KpiCatalogServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private KpiGroup _group = null!;
    private JobPosition _teller = null!;
    private JobPosition _sales = null!;
    private EvaluationPeriod _q3 = null!;
    private EvaluationPeriod _q4 = null!;
    private EvaluationPeriod _q1Next = null!;
    private EvaluationPeriod _locked = null!;

    public void Dispose() => _db.Dispose();

    private KpiDefinitionService Service() => new(_db, new FixedTime());

    private async Task ArrangeAsync()
    {
        _group = new KpiGroup { Id = Guid.NewGuid(), Code = "KD", Name = "Kinh doanh", Status = KpiStatus.Active, UpdatedAt = Now };
        _teller = NewPosition("TELLER", RecordStatus.Active);
        _sales = NewPosition("SALES", RecordStatus.Active);
        _q3 = NewQuarter("2026-Q3", new DateOnly(2026, 7, 1), PeriodStatus.Open);
        _q4 = NewQuarter("2026-Q4", new DateOnly(2026, 10, 1), PeriodStatus.Open);
        _q1Next = NewQuarter("2027-Q1", new DateOnly(2027, 1, 1), PeriodStatus.Open);
        _locked = NewQuarter("2025-Q4", new DateOnly(2025, 10, 1), PeriodStatus.Locked);
        _db.AddRange(_group, _teller, _sales, _q3, _q4, _q1Next, _locked);
        await _db.SaveChangesAsync();
    }

    private static JobPosition NewPosition(string code, RecordStatus status) =>
        new() { Id = Guid.NewGuid(), Code = code, Title = $"Title {code}", Status = status, LastSyncAt = Now, UpdatedAt = Now };

    private static EvaluationPeriod NewQuarter(string code, DateOnly start, PeriodStatus status, PeriodType type = PeriodType.Quarter) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = $"Quý {code}",
        PeriodType = type,
        StartDate = start,
        EndDate = start.AddMonths(3).AddDays(-1),
        Status = status,
        UpdatedAt = Now,
    };

    private KpiDefinitionSaveDto Save(Func<KpiDefinitionSaveDto, KpiDefinitionSaveDto>? change = null)
    {
        var dto = new KpiDefinitionSaveDto
        {
            KpiGroupId = _group.Id,
            Code = "KPI-DEP-R",
            Name = "Tiền gửi - giới thiệu",
            Unit = "VND",
            AggregationType = "SUM",
            Direction = "HIGHER_BETTER",
            CreditRole = "REFERRER",
            DataSource = "TRANSACTION",
            ProductName = "Tiền gửi có kỳ hạn",
            JobPositionIds = [_teller.Id],
            EffectiveFromPeriodId = _q1Next.Id,
        };
        return change is null ? dto : change(dto);
    }

    private async Task<KpiDefinition> AddKpiAsync(string code, KpiStatus status, Action<KpiDefinition>? change = null)
    {
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = _group.Id,
            Code = code,
            Name = $"KPI {code}",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = status,
            UpdatedAt = Now,
        };
        change?.Invoke(kpi);
        _db.KpiDefinitions.Add(kpi);
        await _db.SaveChangesAsync();
        return kpi;
    }

    [Fact]
    public async Task Create_StoresTheCatalogFields_AndReturnsThem()
    {
        await ArrangeAsync();

        var result = await Service().CreateKpiAsync(Save());

        Assert.Equal("REFERRER", result.CreditRole);
        Assert.Equal("TRANSACTION", result.DataSource);
        Assert.Equal("Tiền gửi có kỳ hạn", result.ProductName);
        Assert.Equal([_teller.Id], result.JobPositions!.Select(p => p.Id));
        Assert.Equal("2027-Q1", result.EffectiveFrom!.Code);
        Assert.Null(result.EffectiveTo);
        Assert.Equal("DRAFT", result.Status);
    }

    [Fact]
    public async Task Create_WithoutACode_GeneratesItFromTheName()
    {
        await ArrangeAsync();

        var first = await Service().CreateKpiAsync(Save(d => d with { Code = string.Empty, Name = "Tiền gửi – giới thiệu" }));
        var second = await Service().CreateKpiAsync(Save(d => d with { Code = "  ", Name = "Tiền gửi - giới thiệu" }));
        var explicitCode = await Service().CreateKpiAsync(Save(d => d with { Code = "MY-CODE", Name = "Khác" }));

        Assert.Equal("TIEN_GUI_GIOI_THIEU", first.Code);
        Assert.Equal("TIEN_GUI_GIOI_THIEU_2", second.Code);
        Assert.Equal("MY-CODE", explicitCode.Code);
    }

    [Fact]
    public async Task Update_StillNeedsTheCode()
    {
        await ArrangeAsync();
        var created = await Service().CreateKpiAsync(Save());

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().UpdateKpiAsync(created.Id, Save(d => d with { Code = string.Empty })));
    }

    [Fact]
    public async Task SuggestCode_NumbersACodeThatIsTaken_AndRefusesAnEmptyName()
    {
        await ArrangeAsync();
        await AddKpiAsync("THE_TIN_DUNG", KpiStatus.Active);
        await AddKpiAsync("THE_TIN_DUNG_2", KpiStatus.Active);

        Assert.Equal("THE_TIN_DUNG_3", (await Service().SuggestCodeAsync("Thẻ tín dụng")).Code);
        Assert.Equal("HUY_DONG", (await Service().SuggestCodeAsync("Huy động")).Code);
        await Assert.ThrowsAsync<ValidationException>(() => Service().SuggestCodeAsync("   "));
        await Assert.ThrowsAsync<ValidationException>(() => Service().SuggestCodeAsync(null));
    }

    [Theory]
    [InlineData("Tiền gửi có kỳ hạn", "TIEN_GUI_CO_KY_HAN")]
    [InlineData("Đơn vị Đặc biệt", "DON_VI_DAC_BIET")]
    [InlineData("  Mở thẻ - giới thiệu (Q4)!  ", "MO_THE_GIOI_THIEU_Q4")]
    [InlineData("KPI-OPS", "KPI_OPS")]
    [InlineData("%%%", "KPI")]
    [InlineData("", "KPI")]
    public void CodeGenerator_BuildsAnAsciiUpperCaseCode(string name, string expected)
    {
        Assert.Equal(expected, KpiCodeGenerator.FromName(name));
    }

    [Fact]
    public void CodeGenerator_LeavesRoomForANumberSuffix()
    {
        var code = KpiCodeGenerator.FromName(new string('a', 30) + " " + new string('b', 30));

        Assert.True(code.Length <= 40);
        Assert.False(code.EndsWith('_'));
    }

    [Fact]
    public async Task Create_WithoutCatalogFields_UsesTheDefaults()
    {
        await ArrangeAsync();

        var result = await Service().CreateKpiAsync(Save(_ => new KpiDefinitionSaveDto
        {
            KpiGroupId = _group.Id,
            Code = "PLAIN",
            Name = "Plain",
            Unit = "VND",
            AggregationType = "SUM",
            Direction = "HIGHER_BETTER",
        }));

        Assert.Equal(("PROCESSOR", "TRANSACTION"), (result.CreditRole, result.DataSource));
        Assert.Empty(result.JobPositions!);
        Assert.Null(result.EffectiveFrom);
    }

    [Fact]
    public async Task Update_ReplacesThePositionsAndPeriods_ButKeepsTheCreditRoleWhenNotSent()
    {
        await ArrangeAsync();
        var created = await Service().CreateKpiAsync(Save());

        var updated = await Service().UpdateKpiAsync(created.Id, Save(d => new KpiDefinitionSaveDto
        {
            KpiGroupId = d.KpiGroupId,
            Code = d.Code,
            Name = d.Name,
            Unit = d.Unit,
            AggregationType = d.AggregationType,
            Direction = d.Direction,
            JobPositionIds = [_sales.Id, _teller.Id],
            EffectiveFromPeriodId = _q4.Id,
            EffectiveToPeriodId = _q1Next.Id,
            BaselineValue = 10m,
        }));

        Assert.Equal("REFERRER", updated.CreditRole);
        Assert.Equal(["SALES", "TELLER"], updated.JobPositions!.Select(p => p.Code));
        Assert.Equal(("2026-Q4", "2027-Q1"), (updated.EffectiveFrom!.Code, updated.EffectiveTo!.Code));
        Assert.Equal(10m, updated.BaselineValue);
        Assert.Null(updated.ProductName);
    }

    [Fact]
    public async Task Create_RejectsBadCatalogValues()
    {
        await ArrangeAsync();
        var inactive = NewPosition("OLD", RecordStatus.Inactive);
        var month = NewQuarter("2026-10", new DateOnly(2026, 10, 1), PeriodStatus.Open, PeriodType.Month);
        _db.AddRange(inactive, month);
        await _db.SaveChangesAsync();

        async Task Fails(Func<KpiDefinitionSaveDto, KpiDefinitionSaveDto> change, string message)
        {
            var error = await Assert.ThrowsAsync<ValidationException>(() => Service().CreateKpiAsync(Save(change)));
            Assert.Contains(message, error.Message);
        }

        await Fails(d => d with { CreditRole = "BOSS" }, "creditRole");
        await Fails(d => d with { DataSource = "FAX" }, "dataSource");
        await Fails(d => d with { ProductName = new string('a', 101) }, "tối đa");
        await Fails(d => d with { BaselineValue = -1m }, "Baseline");
        await Fails(d => d with { JobPositionIds = [Guid.NewGuid()] }, "không tồn tại");
        await Fails(d => d with { JobPositionIds = [inactive.Id] }, "đang hoạt động");
        await Fails(d => d with { EffectiveFromPeriodId = Guid.NewGuid() }, "Kỳ áp dụng không tồn tại");
        await Fails(d => d with { EffectiveFromPeriodId = month.Id }, "kỳ quý");
        await Fails(d => d with { EffectiveFromPeriodId = _locked.Id }, "MSG27");
        await Fails(d => d with { EffectiveFromPeriodId = _q4.Id, EffectiveToPeriodId = _q3.Id }, "MSG27");
        await Fails(d => d with { EffectiveFromPeriodId = null, EffectiveToPeriodId = _q4.Id }, "Từ kỳ");
    }

    [Fact]
    public async Task Update_KeepsAPeriodTheKpiAlreadyUsesEvenAfterItWasLocked()
    {
        await ArrangeAsync();
        var kpi = await AddKpiAsync("OLD", KpiStatus.Active, k => k.EffectiveFromPeriodId = _locked.Id);

        var updated = await Service().UpdateKpiAsync(kpi.Id, Save(d => d with { Code = "OLD", EffectiveFromPeriodId = _locked.Id }));

        Assert.Equal("2025-Q4", updated.EffectiveFrom!.Code);
    }

    [Fact]
    public async Task Submit_RecordsWhenTheKpiWasSent()
    {
        await ArrangeAsync();
        var kpi = await AddKpiAsync("SUB", KpiStatus.Draft);
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV1",
            FullName = "Admin",
            Email = "a@bank.test",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = _teller.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = "a", PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now };
        _db.AddRange(employee, account);
        await _db.SaveChangesAsync();

        await Service().ChangeStatusAsync(account.Id, kpi.Id, new KpiStatusDto { Status = "PENDING_APPROVAL" });

        var stored = await _db.KpiDefinitions.AsNoTracking().SingleAsync(k => k.Id == kpi.Id);
        Assert.Equal(Now, stored.SubmittedAt);
        Assert.Equal(employee.Id, stored.SubmittedBy);
    }

    [Fact]
    public async Task List_FiltersByPositionCreditRoleAndSource_AndGroupsByProduct()
    {
        await ArrangeAsync();
        await AddKpiAsync("B-CARD", KpiStatus.Active, k => { k.ProductName = "Thẻ"; k.JobPositions.Add(_teller); });
        await AddKpiAsync("A-DEP", KpiStatus.Active, k => { k.ProductName = "Tiền gửi"; k.JobPositions.Add(_teller); k.JobPositions.Add(_sales); });
        await AddKpiAsync("C-NONE", KpiStatus.Active, k =>
        {
            k.CreditRole = KpiCreditRole.Referrer;
            k.DataSource = KpiDataSource.ServiceRating;
            k.JobPositions.Add(_sales);
        });

        var all = await Service().GetKpisAsync(new KpiFilterDto());
        var teller = await Service().GetKpisAsync(new KpiFilterDto { JobPositionId = _teller.Id });
        var referrer = await Service().GetKpisAsync(new KpiFilterDto { CreditRole = "referrer" });
        var rating = await Service().GetKpisAsync(new KpiFilterDto { DataSource = "SERVICE_RATING" });

        Assert.Equal(["Thẻ", "Tiền gửi", null], all.Items.Select(i => i.ProductName));
        Assert.Equal(["B-CARD", "A-DEP"], teller.Items.Select(i => i.Code));
        Assert.Equal(["C-NONE"], referrer.Items.Select(i => i.Code));
        Assert.Equal(["C-NONE"], rating.Items.Select(i => i.Code));
        await Assert.ThrowsAsync<ValidationException>(() => Service().GetKpisAsync(new KpiFilterDto { CreditRole = "x" }));
    }

    [Fact]
    public async Task List_FiltersByTheKpisInForceInAPeriod()
    {
        await ArrangeAsync();
        await AddKpiAsync("ALWAYS", KpiStatus.Active);
        await AddKpiAsync("RANGE", KpiStatus.Active, k => { k.EffectiveFromPeriodId = _q3.Id; k.EffectiveToPeriodId = _q4.Id; });
        await AddKpiAsync("FUTURE", KpiStatus.Active, k => k.EffectiveFromPeriodId = _q1Next.Id);
        await AddKpiAsync("ENDED", KpiStatus.Active, k => { k.EffectiveFromPeriodId = _locked.Id; k.EffectiveToPeriodId = _q3.Id; });

        var inQ4 = await Service().GetKpisAsync(new KpiFilterDto { EffectiveAtPeriodId = _q4.Id });
        var inNext = await Service().GetKpisAsync(new KpiFilterDto { EffectiveAtPeriodId = _q1Next.Id });

        Assert.Equal(["ALWAYS", "RANGE"], inQ4.Items.Select(i => i.Code));
        Assert.Equal(["ALWAYS", "FUTURE"], inNext.Items.Select(i => i.Code));
        await Assert.ThrowsAsync<NotFoundException>(() => Service().GetKpisAsync(new KpiFilterDto { EffectiveAtPeriodId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Summary_CountsEachStatus()
    {
        await ArrangeAsync();
        await AddKpiAsync("A", KpiStatus.Active);
        await AddKpiAsync("B", KpiStatus.Active);
        await AddKpiAsync("C", KpiStatus.PendingApproval);
        await AddKpiAsync("D", KpiStatus.Draft);
        await AddKpiAsync("E", KpiStatus.Inactive);
        await AddKpiAsync("F", KpiStatus.Rejected);
        await AddKpiAsync("G", KpiStatus.Archived);

        var summary = await Service().GetSummaryAsync();

        Assert.Equal(new KpiSummaryDto(7, 1, 1, 1, 2, 1, 1), summary);
    }

    [Fact]
    public async Task Usage_ListsTheObjectivesTheActiveTargetsAndTheOtherKpisOfTheProduct()
    {
        await ArrangeAsync();
        var processor = await AddKpiAsync("CARD-P", KpiStatus.Active, k => { k.ProductName = "Thẻ"; k.JobPositions.Add(_teller); k.JobPositions.Add(_sales); });
        var referrer = await AddKpiAsync("CARD-R", KpiStatus.Active, k => { k.ProductName = "Thẻ"; k.CreditRole = KpiCreditRole.Referrer; k.JobPositions.Add(_teller); });
        await AddKpiAsync("OTHER", KpiStatus.Active, k => k.ProductName = "Tiền gửi");
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-2026-02",
            Name = "Objective",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = Now,
        };
        _db.BusinessObjectives.Add(objective);
        _db.ObjectiveKpis.Add(new ObjectiveKpi { Id = Guid.NewGuid(), BusinessObjectiveId = objective.Id, KpiDefinitionId = referrer.Id, UpdatedAt = Now });
        _db.TargetAssignments.AddRange(
            NewTarget(referrer.Id, TargetAssignmentStatus.Active),
            NewTarget(referrer.Id, TargetAssignmentStatus.Superseded));
        await _db.SaveChangesAsync();

        var usage = (await Service().GetUsageAsync(referrer.Id))!;

        Assert.Equal(["OBJ-2026-02"], usage.Objectives.Select(o => o.Code));
        Assert.Equal(1, usage.ActiveTargetCount);
        Assert.Equal(["CARD-P", "CARD-R"], usage.RelatedItems.Select(i => i.Code).Order());
        Assert.Equal(["SALES", "TELLER"], usage.RelatedItems.Single(i => i.Id == processor.Id).JobPositions);
        Assert.Null(await Service().GetUsageAsync(Guid.NewGuid()));
    }

    private TargetAssignment NewTarget(Guid kpiId, TargetAssignmentStatus status) => new()
    {
        Id = Guid.NewGuid(),
        KpiDefinitionId = kpiId,
        EvaluationPeriodId = _q4.Id,
        EmployeeId = Guid.NewGuid(),
        TargetValue = 1,
        Status = status,
        UpdatedAt = Now,
    };

    [Fact]
    public async Task Usage_OfAKpiWithoutProduct_HasNoRelatedItems()
    {
        await ArrangeAsync();
        var kpi = await AddKpiAsync("SOLO", KpiStatus.Active);

        var usage = (await Service().GetUsageAsync(kpi.Id))!;

        Assert.Empty(usage.RelatedItems);
        Assert.Empty(usage.Objectives);
    }

    [Fact]
    public async Task History_ReadsTheAuditLogOldestFirstAndFollowsTheStatusStepByStep()
    {
        await ArrangeAsync();
        var kpi = await AddKpiAsync("HIST", KpiStatus.Draft);
        var id = kpi.Id.ToString();

        // The audit log records an update's "old values" with the new ones, so only the new values are read.
        AuditLog Log(int minute, string action, string? changed = null, string? @new = null) => new()
        {
            Id = Guid.NewGuid(),
            EntityName = nameof(KpiDefinition),
            EntityId = id,
            Action = action,
            ChangedColumns = changed,
            OldValues = @new,
            NewValues = @new,
            ActorName = "Nguyễn Văn An",
            OccurredAt = Now.AddMinutes(minute),
        };

        _db.AuditLogs.AddRange(
            Log(0, "Create", null, "{\"Status\":0}"),
            Log(1, "Update", "[\"Status\",\"UpdatedAt\"]", "{\"Status\":4}"),
            Log(2, "Update", "[\"Status\"]", "{\"Status\":5,\"RejectReason\":\"Thiếu công thức\"}"),
            Log(3, "Update", "[\"Name\",\"UpdatedAt\"]", "{}"),
            Log(4, "Update", "[\"Status\"]", "{\"Status\":4}"),
            Log(5, "Update", "[\"Status\"]", "{\"Status\":1}"),
            Log(6, "Update", "[\"Status\"]", "{\"Status\":2}"),
            Log(7, "Update", "[\"Status\"]", "{\"Status\":1}"),
            Log(8, "Update", "[\"Status\"]", "{\"Status\":3}"),
            Log(9, "Update", "[\"UpdatedAt\"]", "{}"),
            Log(10, "Update", "[\"Status\"]", "not json"),
            new AuditLog { Id = Guid.NewGuid(), EntityName = nameof(KpiDefinition), EntityId = Guid.NewGuid().ToString(), Action = "Create", OccurredAt = Now },
            new AuditLog { Id = Guid.NewGuid(), EntityName = "Other", EntityId = id, Action = "Create", OccurredAt = Now });
        await _db.SaveChangesAsync();

        var history = (await Service().GetHistoryAsync(kpi.Id))!;

        Assert.Equal(
            ["CREATE", "SUBMIT", "REJECT", "UPDATE", "SUBMIT", "APPROVE", "DEACTIVATE", "REACTIVATE", "ARCHIVE", "UPDATE"],
            history.Select(e => e.Action));
        Assert.Equal("Từ chối: Thiếu công thức", history[2].Summary);
        Assert.Equal("Chỉnh sửa", history[3].Summary);
        Assert.All(history, e => Assert.Equal("Nguyễn Văn An", e.ActorName));
        Assert.Null(await Service().GetHistoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task History_OfAKpiWithoutACreateEntry_InfersWhereTheFirstLoggedStepCameFrom()
    {
        await ArrangeAsync();
        var kpi = await AddKpiAsync("LOADED", KpiStatus.Rejected);

        AuditLog Step(int minute, string status, string? extra = null) => new()
        {
            Id = Guid.NewGuid(),
            EntityName = nameof(KpiDefinition),
            EntityId = kpi.Id.ToString(),
            Action = "Update",
            ChangedColumns = "[\"Status\"]",
            OldValues = $"{{\"Status\":{status}}}",
            NewValues = $"{{\"Status\":{status}{extra}}}",
            OccurredAt = Now.AddMinutes(minute),
        };

        // The log wrongly keeps the new status as the old one, so "5 to 5" must still read as a rejection.
        _db.AuditLogs.AddRange(Step(0, "5", ",\"RejectReason\":\"Thiếu công thức\""), Step(1, "4"), Step(2, "1"));
        await _db.SaveChangesAsync();

        var history = (await Service().GetHistoryAsync(kpi.Id))!;

        Assert.Equal(["REJECT", "SUBMIT", "APPROVE"], history.Select(e => e.Action));
        Assert.Equal("Từ chối: Thiếu công thức", history[0].Summary);
        Assert.DoesNotContain(history, e => e.Summary.Contains('→'));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
