using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace G54.UnitTests;

/// <summary>Request validation runs before any I/O, so these need no database.</summary>
public sealed class TargetSuggestionValidationTests
{
    private static TargetSuggestionService Service() =>
        new(null!, null!, null!, Options.Create(new SystemParametersOptions()));

    private static SuggestTargetsRequest Request(
        int tier = 1,
        string basis = SuggestionBasis.TargetLastYear,
        string overwrite = SuggestionOverwrite.EmptyOnly,
        SuggestionRoundingDto? rounding = null,
        SuggestionRemainderDto? remainder = null) =>
        new(tier, basis, Guid.NewGuid(), null, null, null, null, overwrite, null, rounding, remainder);

    [Fact]
    public async Task UnknownBasis_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SuggestAsync(2028, Request(basis: "LAST_MONTH"), Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task UnknownOverwrite_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SuggestAsync(2028, Request(overwrite: "ALWAYS"), Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task BufferWithoutUnit_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SuggestAsync(
                2028, Request(remainder: new SuggestionRemainderDto(SuggestionRemainderMethodWire.Buffer, null)),
                Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task UnknownRemainderMethod_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SuggestAsync(
                2028, Request(remainder: new SuggestionRemainderDto("RANDOM", null)), Guid.NewGuid(), CancellationToken.None));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10_000_000, 0)]
    public async Task ActualBasis_NeedsPositiveRoundingSteps(int moneyStep, int countStep) =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SuggestAsync(
                2028,
                Request(basis: SuggestionBasis.ActualLastYear, rounding: new SuggestionRoundingDto(moneyStep, countStep, null)),
                Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task TierOutsideOneToThree_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().SuggestAsync(2028, Request(tier: 4), Guid.NewGuid(), CancellationToken.None));
}

/// <summary>
/// Runs the suggestion engine against the real schema. With no history, tier 1 splits 1:2:3:4, tier 2 follows the
/// eligible headcount and tier 3 is equal; with last year's quarters, tier 1 follows their shape.
/// </summary>
public sealed class TargetSuggestionPostgresTests : IAsyncLifetime
{
    private const int Year = 2028;

    private string _database = null!;
    private ServiceProvider _provider = null!;
    private Guid _adminAccount;
    private Guid _branchId;
    private Guid _deptA;
    private Guid _deptB;
    private Guid _deptC;
    private Guid _employeeA1;
    private Guid _employeeA2;
    private Guid _kpiId;
    private Guid _unstaffedKpiId;
    private Guid _branchYearId;
    private Guid _unstaffedBranchYearId;
    private Guid _deptYearAId;

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_suggest_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(connectionString);
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.AddScoped(provider => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .EnableServiceProviderCaching(false).Options);
        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await SeedAsync(db);
    }

    public async Task DisposeAsync()
    {
        if (_provider is null)
        {
            return;
        }

        await _provider.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Branch with departments A (2 eligible employees), B (1) and C (none); two KPIs, only the first has a work type.</summary>
    private async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var branch = Department("SG-B", DepartmentLevelType.Branch, null, now);
        var deptA = Department("SG-DA", DepartmentLevelType.Department, branch.Id, now);
        var deptB = Department("SG-DB", DepartmentLevelType.Department, branch.Id, now);
        var deptC = Department("SG-DC", DepartmentLevelType.Department, branch.Id, now);
        db.Departments.AddRange(branch, deptA, deptB, deptC);
        var job = new JobPosition { Id = Guid.NewGuid(), Code = "SG-JOB", Title = "Job", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        db.JobPositions.Add(job);
        await db.SaveChangesAsync();

        var admin = Employee("SG-ADM", branch.Id, job.Id, now);
        var a1 = Employee("SG-A1", deptA.Id, job.Id, now);
        var a2 = Employee("SG-A2", deptA.Id, job.Id, now);
        var b1 = Employee("SG-B1", deptB.Id, job.Id, now);
        db.Employees.AddRange(admin, a1, a2, b1);
        await db.SaveChangesAsync();

        var account = new Account { Id = Guid.NewGuid(), EmployeeId = admin.Id, Username = "sg-adm", PasswordHash = "h", LastSyncAt = now, UpdatedAt = now };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "SG-G", Name = "G", Status = KpiStatus.Active, UpdatedAt = now };
        db.Accounts.Add(account);
        db.KpiGroups.Add(group);
        await db.SaveChangesAsync();

        var kpi = Kpi("SG-K1", group.Id, now);
        var unstaffedKpi = Kpi("SG-K2", group.Id, now);
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "SG-WT", Name = "Work", Status = RecordStatus.Active, UpdatedAt = now };
        db.KpiDefinitions.AddRange(kpi, unstaffedKpi);
        db.WorkTypes.Add(workType);
        await db.SaveChangesAsync();
        db.JobPositionWorkTypes.Add(new JobPositionWorkType { Id = Guid.NewGuid(), JobPositionId = job.Id, WorkTypeId = workType.Id, UpdatedAt = now });
        db.WorkTypeKpis.Add(new WorkTypeKpi { Id = Guid.NewGuid(), WorkTypeId = workType.Id, KpiDefinitionId = kpi.Id, EffectiveFrom = new DateOnly(Year, 1, 1), UpdatedAt = now });

        var year = await AddYearAsync(db, Year, now);
        var branchYear = Row(branch.Id, kpi.Id, year.Id, 1000m, null, null, now);
        var unstaffedBranchYear = Row(branch.Id, unstaffedKpi.Id, year.Id, 1000m, null, null, now);
        var deptYearA = Row(deptA.Id, kpi.Id, year.Id, 600m, branchYear.Id, TargetSplitDimension.Org, now);
        db.TargetAssignments.AddRange(branchYear, unstaffedBranchYear, deptYearA);
        await db.SaveChangesAsync();

        (_adminAccount, _branchId, _deptA, _deptB, _deptC, _employeeA1, _employeeA2) =
            (account.Id, branch.Id, deptA.Id, deptB.Id, deptC.Id, a1.Id, a2.Id);
        (_kpiId, _unstaffedKpiId, _branchYearId, _unstaffedBranchYearId, _deptYearAId) =
            (kpi.Id, unstaffedKpi.Id, branchYear.Id, unstaffedBranchYear.Id, deptYearA.Id);
    }

    private static async Task<EvaluationPeriod> AddYearAsync(AppDbContext db, int year, DateTimeOffset now)
    {
        EvaluationPeriod Period(string code, PeriodType type, Guid? parent, int m1, int d1, int m2, int d2) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = type,
            ParentPeriodId = parent,
            Status = PeriodStatus.Open,
            StartDate = new DateOnly(year, m1, d1),
            EndDate = new DateOnly(year, m2, d2),
            UpdatedAt = now,
        };

        var y = Period($"{year}", PeriodType.Year, null, 1, 1, 12, 31);
        db.EvaluationPeriods.Add(y);
        await db.SaveChangesAsync();
        var h1 = Period($"{year}-H1", PeriodType.HalfYear, y.Id, 1, 1, 6, 30);
        var h2 = Period($"{year}-H2", PeriodType.HalfYear, y.Id, 7, 1, 12, 31);
        db.EvaluationPeriods.AddRange(h1, h2);
        await db.SaveChangesAsync();
        db.EvaluationPeriods.AddRange(
            Period($"{year}-Q1", PeriodType.Quarter, h1.Id, 1, 1, 3, 31),
            Period($"{year}-Q2", PeriodType.Quarter, h1.Id, 4, 1, 6, 30),
            Period($"{year}-Q3", PeriodType.Quarter, h2.Id, 7, 1, 9, 30),
            Period($"{year}-Q4", PeriodType.Quarter, h2.Id, 10, 1, 12, 31));
        await db.SaveChangesAsync();
        return y;
    }

    private async Task<SuggestTargetsResponseDto> SuggestAsync(
        int tier, Guid anchor, int year = Year, string overwrite = SuggestionOverwrite.EmptyOnly,
        List<Guid>? kpiIds = null, List<SuggestionCurrentValueDto>? current = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new TargetSuggestionService(
            new UnitOfWork(db), db, new AdminPermissions(), Options.Create(new SystemParametersOptions()));
        var request = new SuggestTargetsRequest(
            tier, SuggestionBasis.TargetLastYear, anchor, kpiIds, null, null, null, overwrite, current,
            new SuggestionRoundingDto(null, 1m, null), null);
        return await service.SuggestAsync(year, request, _adminAccount, CancellationToken.None);
    }

    [PostgresFact]
    public async Task Tier1_WithoutHistory_SplitsTheYearOneTwoThreeFour()
    {
        var result = await SuggestAsync(1, _branchYearId, kpiIds: [_kpiId]);

        var kpi = Assert.Single(result.Kpis);
        var cell = Assert.Single(kpi.Cells);
        Assert.Equal((1000m, 100m, 200m, 300m, 400m), (cell.Year, cell.Q1, cell.Q2, cell.Q3, cell.Q4));
        Assert.True(cell.UsedFallbackAverage);
        Assert.True(kpi.SumMatches);
    }

    [PostgresFact]
    public async Task Tier1_WithLastYearQuarters_FollowsTheirShape()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var previous = await AddYearAsync(db, Year - 1, now);
            var quarters = await db.EvaluationPeriods
                .Where(p => p.PeriodType == PeriodType.Quarter && p.StartDate.Year == Year - 1)
                .OrderBy(p => p.StartDate).ToListAsync();
            var previousYear = Row(_branchId, _kpiId, previous.Id, 1000m, null, null, now, TargetAssignmentStatus.Active);
            db.TargetAssignments.Add(previousYear);
            decimal[] shape = [400m, 300m, 200m, 100m];
            for (var i = 0; i < 4; i++)
            {
                db.TargetAssignments.Add(
                    Row(_branchId, _kpiId, quarters[i].Id, shape[i], previousYear.Id, TargetSplitDimension.Period, now, TargetAssignmentStatus.Active));
            }

            await db.SaveChangesAsync();
        }

        var result = await SuggestAsync(1, _branchYearId, kpiIds: [_kpiId]);

        var cell = Assert.Single(Assert.Single(result.Kpis).Cells);
        Assert.Equal((400m, 300m, 200m, 100m), (cell.Q1, cell.Q2, cell.Q3, cell.Q4));
        Assert.False(cell.UsedFallbackAverage);
    }

    [PostgresFact]
    public async Task Tier2_WithoutHistory_SplitsByEligibleHeadcount()
    {
        var result = await SuggestAsync(2, _branchYearId, kpiIds: [_kpiId]);

        var kpi = Assert.Single(result.Kpis);
        var byDepartment = kpi.Cells.ToDictionary(c => c.UnitId);
        Assert.Equal(2, byDepartment.Count);
        Assert.Equal(667m, byDepartment[_deptA].Year);
        Assert.Equal(333m, byDepartment[_deptB].Year);
        Assert.All(kpi.Cells, c =>
        {
            Assert.True(c.UsedFallbackAverage);
            Assert.Equal(c.Year, c.Q1 + c.Q2 + c.Q3 + c.Q4);
        });
        Assert.True(kpi.SumMatches);
        var locked = Assert.Single(kpi.LockedUnits);
        Assert.Equal((_deptC, "no_headcount"), (locked.UnitId, locked.Reason));
    }

    [PostgresFact]
    public async Task Tier2_KpiMeasuredInBillions_ScalesTheVndRoundingStepToItsOwnUnit()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.KpiDefinitions.SingleAsync(k => k.Id == _kpiId)).Unit = "tỷ VND";
            await db.SaveChangesAsync();
        }

        // The default money step is 10,000,000 VND = 0.01 tỷ; unscaled it rounded every share down to 0 and
        // dumped the whole year on one department.
        var result = await SuggestAsync(2, _branchYearId, kpiIds: [_kpiId]);

        var byDepartment = Assert.Single(result.Kpis).Cells.ToDictionary(c => c.UnitId);
        Assert.Equal(666.67m, byDepartment[_deptA].Year);
        Assert.Equal(333.33m, byDepartment[_deptB].Year);
    }

    [PostgresFact]
    public async Task Tier2_KpiNobodyIsResponsibleFor_IsDisabledWithoutCells()
    {
        var result = await SuggestAsync(2, _unstaffedBranchYearId, kpiIds: [_unstaffedKpiId]);

        var kpi = Assert.Single(result.Kpis);
        Assert.Equal("no_history", kpi.DisabledReason);
        Assert.Empty(kpi.Cells);
    }

    [PostgresFact]
    public async Task Tier3_WithoutHistory_SplitsEquallyBetweenEligibleEmployees()
    {
        var result = await SuggestAsync(3, _deptYearAId);

        var kpi = Assert.Single(result.Kpis);
        Assert.Equal(2, kpi.Cells.Count);
        Assert.All(kpi.Cells, c =>
        {
            Assert.Equal(300m, c.Year);
            Assert.True(c.UsedFallbackAverage);
            Assert.Equal(c.Year, c.Q1 + c.Q2 + c.Q3 + c.Q4);
        });
        Assert.True(kpi.SumMatches);
    }

    [PostgresFact]
    public async Task Tier3_EmptyOnly_KeepsAFilledCellAndGivesTheRestToTheOthers()
    {
        var result = await SuggestAsync(
            3, _deptYearAId, current: [new SuggestionCurrentValueDto(_employeeA1, 100m, null, null, null, null, false)]);

        var byEmployee = Assert.Single(result.Kpis).Cells.ToDictionary(c => c.UnitId);
        Assert.Equal(100m, byEmployee[_employeeA1].Year);
        Assert.False(byEmployee[_employeeA1].Overwritten);
        Assert.Equal(500m, byEmployee[_employeeA2].Year);
        Assert.True(byEmployee[_employeeA2].Overwritten);
    }

    [PostgresFact]
    public async Task Tier3_Unlocked_OverwritesAFilledCell()
    {
        var result = await SuggestAsync(
            3, _deptYearAId, overwrite: SuggestionOverwrite.Unlocked,
            current: [new SuggestionCurrentValueDto(_employeeA1, 100m, null, null, null, null, false)]);

        Assert.All(Assert.Single(result.Kpis).Cells, c => Assert.Equal(300m, c.Year));
    }

    [PostgresFact]
    public async Task AnchorFromAnotherYear_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() => SuggestAsync(1, _branchYearId, year: Year + 1));

    [PostgresFact]
    public async Task UnknownAnchor_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => SuggestAsync(1, Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => SuggestAsync(3, Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task Tier3_NeedsADepartmentYearAnchor() =>
        await Assert.ThrowsAsync<NotFoundException>(() => SuggestAsync(3, _branchYearId));

    [PostgresFact]
    public async Task Tier2_WithClosedQuarter_PreservesLockedQuarterAndSuggestsOpenQuarters()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var quarters = await db.EvaluationPeriods
                .Where(p => p.PeriodType == PeriodType.Quarter && p.StartDate.Year == Year)
                .OrderBy(p => p.StartDate)
                .ToListAsync();
            quarters[0].Status = PeriodStatus.Closed;

            // Branch quarters: [200, 200, 300, 300] = 1000.
            decimal[] branchQ = [200m, 200m, 300m, 300m];
            for (var i = 0; i < 4; i++)
            {
                db.TargetAssignments.Add(Row(_branchId, _kpiId, quarters[i].Id, branchQ[i], _branchYearId, TargetSplitDimension.Period, now));
            }

            // Dept A already has stored Q1=150; Dept B has stored Q1=50 (deptYearB created here).
            var deptYearA = await db.TargetAssignments.SingleAsync(t => t.Id == _deptYearAId);
            var yearPeriod = await db.EvaluationPeriods.SingleAsync(p => p.PeriodType == PeriodType.Year && p.StartDate.Year == Year);
            var deptYearB = Row(_deptB, _kpiId, yearPeriod.Id, 400m, _branchYearId, TargetSplitDimension.Org, now);
            db.TargetAssignments.Add(deptYearB);
            await db.SaveChangesAsync();

            db.TargetAssignments.Add(Row(_deptA, _kpiId, quarters[0].Id, 150m, deptYearA.Id, TargetSplitDimension.Period, now));
            db.TargetAssignments.Add(Row(_deptB, _kpiId, quarters[0].Id, 50m, deptYearB.Id, TargetSplitDimension.Period, now));
            await db.SaveChangesAsync();
        }

        var result = await SuggestAsync(2, _branchYearId, kpiIds: [_kpiId]);
        var kpi = Assert.Single(result.Kpis);
        var byDept = kpi.Cells.ToDictionary(c => c.UnitId);

        Assert.Equal(150m, byDept[_deptA].Q1);
        Assert.Equal(50m, byDept[_deptB].Q1);
        Assert.True(kpi.SumMatches);
        Assert.All(kpi.Cells, c => Assert.Equal(c.Year, c.Q1 + c.Q2 + c.Q3 + c.Q4));
        Assert.Equal(200m, byDept[_deptA].Q2 + byDept[_deptB].Q2);
        Assert.Equal(300m, byDept[_deptA].Q3 + byDept[_deptB].Q3);
        Assert.Equal(300m, byDept[_deptA].Q4 + byDept[_deptB].Q4);
    }

    [PostgresFact]
    public async Task Tier3_WithClosedQuarter_PreservesLockedQuarterAndSetsYearFromQuarters()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var quarters = await db.EvaluationPeriods
                .Where(p => p.PeriodType == PeriodType.Quarter && p.StartDate.Year == Year)
                .OrderBy(p => p.StartDate)
                .ToListAsync();
            quarters[0].Status = PeriodStatus.Closed;

            // Dept A target = 600 -> quarters [100, 150, 150, 200].
            decimal[] deptQ = [100m, 150m, 150m, 200m];
            for (var i = 0; i < 4; i++)
            {
                db.TargetAssignments.Add(Row(_deptA, _kpiId, quarters[i].Id, deptQ[i], _deptYearAId, TargetSplitDimension.Period, now));
            }

            var yearPeriod = await db.EvaluationPeriods.SingleAsync(p => p.PeriodType == PeriodType.Year && p.StartDate.Year == Year);
            var emp1Year = new TargetAssignment
            {
                Id = Guid.NewGuid(),
                EmployeeId = _employeeA1,
                KpiDefinitionId = _kpiId,
                EvaluationPeriodId = yearPeriod.Id,
                ParentAssignmentId = _deptYearAId,
                SplitDimension = TargetSplitDimension.Org,
                TargetValue = 300m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                CreatedAt = now,
                AssignedAt = now,
            };
            db.TargetAssignments.Add(emp1Year);
            await db.SaveChangesAsync();
            db.TargetAssignments.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                EmployeeId = _employeeA1,
                KpiDefinitionId = _kpiId,
                EvaluationPeriodId = quarters[0].Id,
                ParentAssignmentId = emp1Year.Id,
                SplitDimension = TargetSplitDimension.Period,
                TargetValue = 100m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                CreatedAt = now,
                AssignedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var result = await SuggestAsync(3, _deptYearAId);
        var kpi = Assert.Single(result.Kpis);
        var byEmp = kpi.Cells.ToDictionary(c => c.UnitId);

        Assert.Equal(100m, byEmp[_employeeA1].Q1);
        Assert.Equal(0m, byEmp[_employeeA2].Q1);
        Assert.True(kpi.SumMatches);
        Assert.All(kpi.Cells, c => Assert.Equal(c.Year, c.Q1 + c.Q2 + c.Q3 + c.Q4));
    }

    [PostgresFact]
    public async Task Tier3_NonSumAggregation_ReturnsUnsupportedDisabledReason()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.KpiDefinitions.SingleAsync(k => k.Id == _kpiId)).AggregationType = KpiAggregationType.Avg;
            await db.SaveChangesAsync();
        }

        var result = await SuggestAsync(3, _deptYearAId);
        var kpi = Assert.Single(result.Kpis);

        Assert.Equal("unsupported_aggregation", kpi.DisabledReason);
        Assert.Empty(kpi.Cells);
    }

    [PostgresFact]
    public async Task Tier2_PartialHistory_UsesPreviousYearPositionPerHeadForNewDepartment()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var prevYear = await AddYearAsync(db, Year - 1, now);

            // Second job position ("SG-JOB-2") with higher historical per-head output than SG-JOB.
            var highJob = new JobPosition { Id = Guid.NewGuid(), Code = "SG-JOB-2", Title = "High", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
            db.JobPositions.Add(highJob);
            await db.SaveChangesAsync();
            var wtId = await db.WorkTypes.Select(w => w.Id).FirstAsync();
            db.JobPositionWorkTypes.Add(new JobPositionWorkType { Id = Guid.NewGuid(), JobPositionId = highJob.Id, WorkTypeId = wtId, UpdatedAt = now });

            // Dept A had a high-job employee with 400 and two SG-JOB employees with 100 each -> total 600 in prev year.
            var empHigh = Employee("SG-A3", _deptA, highJob.Id, now);
            db.Employees.Add(empHigh);
            await db.SaveChangesAsync();

            var prevBranch = Row(_branchId, _kpiId, prevYear.Id, 600m, null, null, now, TargetAssignmentStatus.Active);
            db.TargetAssignments.Add(prevBranch);
            await db.SaveChangesAsync();
            var prevDeptA = Row(_deptA, _kpiId, prevYear.Id, 600m, prevBranch.Id, TargetSplitDimension.Org, now, TargetAssignmentStatus.Active);
            db.TargetAssignments.Add(prevDeptA);
            await db.SaveChangesAsync();

            db.TargetAssignments.AddRange(
                new TargetAssignment { Id = Guid.NewGuid(), EmployeeId = _employeeA1, KpiDefinitionId = _kpiId, EvaluationPeriodId = prevYear.Id, ParentAssignmentId = prevDeptA.Id, SplitDimension = TargetSplitDimension.Org, TargetValue = 100m, Version = 1, Status = TargetAssignmentStatus.Active, CreatedAt = now, AssignedAt = now, ApprovedAt = now },
                new TargetAssignment { Id = Guid.NewGuid(), EmployeeId = _employeeA2, KpiDefinitionId = _kpiId, EvaluationPeriodId = prevYear.Id, ParentAssignmentId = prevDeptA.Id, SplitDimension = TargetSplitDimension.Org, TargetValue = 100m, Version = 1, Status = TargetAssignmentStatus.Active, CreatedAt = now, AssignedAt = now, ApprovedAt = now },
                new TargetAssignment { Id = Guid.NewGuid(), EmployeeId = empHigh.Id, KpiDefinitionId = _kpiId, EvaluationPeriodId = prevYear.Id, ParentAssignmentId = prevDeptA.Id, SplitDimension = TargetSplitDimension.Org, TargetValue = 400m, Version = 1, Status = TargetAssignmentStatus.Active, CreatedAt = now, AssignedAt = now, ApprovedAt = now });
            await db.SaveChangesAsync();
        }

        // Dept A (history=600) vs Dept B (1 SG-JOB employee, no dept history -> uses SG-JOB avg=100, NOT branch avg=200).
        // Weights: 600 vs 100 -> 1000 split 6/7 (~857) vs 1/7 (~143).
        var result = await SuggestAsync(2, _branchYearId, kpiIds: [_kpiId]);
        var byDept = Assert.Single(result.Kpis).Cells.ToDictionary(c => c.UnitId);

        Assert.False(byDept[_deptA].UsedFallbackAverage);
        Assert.True(byDept[_deptB].UsedFallbackAverage);
        Assert.Equal(857m, byDept[_deptA].Year);
        Assert.Equal(143m, byDept[_deptB].Year);
    }

    [PostgresFact]
    public async Task Tier2_MultiKpiCurrentValuesScopedByKpiId_AppliesOnlyToMatchingKpi()
    {
        var secondKpiId = Guid.NewGuid();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var groupId = await db.KpiGroups.Select(g => g.Id).FirstAsync();
            var secondKpi = new KpiDefinition
            {
                Id = secondKpiId,
                KpiGroupId = groupId,
                Code = "SG-K3",
                Name = "SG-K3",
                Unit = "case",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = now,
            };
            db.KpiDefinitions.Add(secondKpi);
            var wtId = await db.WorkTypes.Select(w => w.Id).FirstAsync();
            db.WorkTypeKpis.Add(new WorkTypeKpi { Id = Guid.NewGuid(), WorkTypeId = wtId, KpiDefinitionId = secondKpiId, EffectiveFrom = new DateOnly(Year, 1, 1), UpdatedAt = now });
            var yearPeriod = await db.EvaluationPeriods.SingleAsync(p => p.PeriodType == PeriodType.Year && p.StartDate.Year == Year);
            db.TargetAssignments.Add(Row(_branchId, secondKpiId, yearPeriod.Id, 900m, null, null, now));
            await db.SaveChangesAsync();
        }

        var result = await SuggestAsync(
            2, _branchYearId, kpiIds: [_kpiId, secondKpiId],
            current: [new SuggestionCurrentValueDto(_deptA, 500m, null, null, null, null, false, KpiId: _kpiId)]);

        var kpi1 = result.Kpis.Single(k => k.KpiId == _kpiId).Cells.ToDictionary(c => c.UnitId);
        var kpi2 = result.Kpis.Single(k => k.KpiId == secondKpiId).Cells.ToDictionary(c => c.UnitId);

        Assert.Equal(500m, kpi1[_deptA].Year);
        Assert.False(kpi1[_deptA].Overwritten);
        Assert.Equal(600m, kpi2[_deptA].Year);
        Assert.True(kpi2[_deptA].Overwritten);
    }

    [PostgresFact]
    public async Task D9Warning_UniformHighGrowthDoesNotWarn_AndFixedExceedingTotalWarns()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var prevYear = await AddYearAsync(db, Year - 1, now);
            var prevBranch = Row(_branchId, _kpiId, prevYear.Id, 500m, null, null, now, TargetAssignmentStatus.Active);
            db.TargetAssignments.Add(prevBranch);
            await db.SaveChangesAsync();
            db.TargetAssignments.AddRange(
                Row(_deptA, _kpiId, prevYear.Id, 300m, prevBranch.Id, TargetSplitDimension.Org, now, TargetAssignmentStatus.Active),
                Row(_deptB, _kpiId, prevYear.Id, 200m, prevBranch.Id, TargetSplitDimension.Org, now, TargetAssignmentStatus.Active));
            await db.SaveChangesAsync();
        }

        // Branch doubled from 500 -> 1000 (+100%, well above 30% absolute, but exact same 60/40 share -> 0 warnings).
        var uniform = await SuggestAsync(2, _branchYearId, kpiIds: [_kpiId]);
        Assert.Empty(Assert.Single(uniform.Kpis).Warnings);

        // Fixed cell exceeds total (1200 > 1000) -> warns clearly and sets free cell to 0.
        var exceeded = await SuggestAsync(
            2, _branchYearId, kpiIds: [_kpiId],
            current: [new SuggestionCurrentValueDto(_deptA, 1200m, null, null, null, null, false)]);
        var exceededKpi = Assert.Single(exceeded.Kpis);
        Assert.Contains(exceededKpi.Warnings, w => w.Contains("vượt tổng cần phân bổ"));
        Assert.Equal(0m, exceededKpi.Cells.Single(c => c.UnitId == _deptB).Year);
    }

    private static KpiDefinition Kpi(string code, Guid groupId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        KpiGroupId = groupId,
        Code = code,
        Name = code,
        Unit = "case",
        AggregationType = KpiAggregationType.Sum,
        Direction = KpiDirection.HigherBetter,
        Status = KpiStatus.Active,
        UpdatedAt = now,
    };

    private static TargetAssignment Row(
        Guid departmentId, Guid kpiId, Guid periodId, decimal value, Guid? parentId, TargetSplitDimension? split,
        DateTimeOffset now, TargetAssignmentStatus status = TargetAssignmentStatus.Draft) => new()
        {
            Id = Guid.NewGuid(),
            DepartmentId = departmentId,
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = periodId,
            ParentAssignmentId = parentId,
            SplitDimension = split,
            TargetValue = value,
            Version = 1,
            Status = status,
            CreatedAt = now,
            AssignedAt = now,
            ApprovedAt = status == TargetAssignmentStatus.Draft ? null : now,
        };

    private static Department Department(string code, DepartmentLevelType level, Guid? parent, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        LevelType = level,
        ParentDepartmentId = parent,
        Status = RecordStatus.Active,
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private static Employee Employee(string code, Guid departmentId, Guid jobId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = code,
        Email = $"{code.ToLowerInvariant()}@x.local",
        DepartmentId = departmentId,
        JobPositionId = jobId,
        Status = EmployeeStatus.Active,
        LastSyncAt = now,
        UpdatedAt = now,
    };

    private sealed class AdminPermissions : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPermissionDto>>([]);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
