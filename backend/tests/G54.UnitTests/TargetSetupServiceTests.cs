using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Department;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace G54.UnitTests;

public sealed class TargetSetupServiceTests
{
    private sealed record Fixture(
        AppDbContext Db,
        TargetSetupService Service,
        Guid BranchId,
        Guid UserId,
        int Year,
        Guid YearPeriodId,
        Guid[] QuarterPeriodIds,
        Guid RatioKpiId,
        Guid NonRatioKpiId,
        Guid BranchYearRatioId,
        Guid BranchYearNonRatioId
    );

    private static async Task<Fixture> CreateAsync(PeriodStatus status = PeriodStatus.Open)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        var year = 2027; // Use 2027 so quarters are in the future
        var branch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BRANCH-001",
            Name = "Branch 001",
            LevelType = DepartmentLevelType.Branch
        };

        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group" };

        // Ratio KPI (NO_AGGREGATION)
        var ratioKpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI_RATIO",
            Name = "Threshold Ratio",
            Unit = "%",
            AggregationType = KpiAggregationType.NoAggregation,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        };

        // Non-ratio KPI (SUM aggregation)
        var nonRatioKpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI_REVENUE",
            Name = "Revenue",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        };

        var yearPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = $"Year {year}",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(year, 1, 1),
            EndDate = new DateOnly(year, 12, 31),
            Status = status
        };

        // Create half-years (parents of quarters, like in real seed)
        var h1PeriodId = Guid.NewGuid();
        var h2PeriodId = Guid.NewGuid();
        var h1Period = new EvaluationPeriod
        {
            Id = h1PeriodId,
            Code = $"{year}-H1",
            Name = "H1",
            ParentPeriodId = yearPeriod.Id,
            PeriodType = PeriodType.HalfYear,
            StartDate = new DateOnly(year, 1, 1),
            EndDate = new DateOnly(year, 6, 30),
            Status = status
        };
        var h2Period = new EvaluationPeriod
        {
            Id = h2PeriodId,
            Code = $"{year}-H2",
            Name = "H2",
            ParentPeriodId = yearPeriod.Id,
            PeriodType = PeriodType.HalfYear,
            StartDate = new DateOnly(year, 7, 1),
            EndDate = new DateOnly(year, 12, 31),
            Status = status
        };

        // Create 4 quarter periods (children of half-years)
        var quarterIds = new Guid[4];
        var quarters = new List<EvaluationPeriod>();
        var quarterDates = new[]
        {
            (new DateOnly(year, 1, 1), new DateOnly(year, 3, 31), h1PeriodId),
            (new DateOnly(year, 4, 1), new DateOnly(year, 6, 30), h1PeriodId),
            (new DateOnly(year, 7, 1), new DateOnly(year, 9, 30), h2PeriodId),
            (new DateOnly(year, 10, 1), new DateOnly(year, 12, 31), h2PeriodId)
        };

        for (int q = 1; q <= 4; q++)
        {
            var (start, end, parentId) = quarterDates[q - 1];
            var qId = Guid.NewGuid();
            quarterIds[q - 1] = qId;

            quarters.Add(new EvaluationPeriod
            {
                Id = qId,
                Code = $"{year}-Q{q}",
                Name = $"Q{q}",
                ParentPeriodId = parentId,
                PeriodType = PeriodType.Quarter,
                StartDate = start,
                EndDate = end,
                Status = status
            });
        }

        // Create branch-year targets
        var branchYearRatioId = Guid.NewGuid();
        var branchYearRatio = new TargetAssignment
        {
            Id = branchYearRatioId,
            DepartmentId = branch.Id,
            KpiDefinitionId = ratioKpi.Id,
            EvaluationPeriodId = yearPeriod.Id,
            TargetValue = 80m, // Ratio threshold
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var branchYearNonRatioId = Guid.NewGuid();
        var branchYearNonRatio = new TargetAssignment
        {
            Id = branchYearNonRatioId,
            DepartmentId = branch.Id,
            KpiDefinitionId = nonRatioKpi.Id,
            EvaluationPeriodId = yearPeriod.Id,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Create quarter splits for non-ratio KPI (sum = 1000)
        var quarterSplits = new List<TargetAssignment>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = branchYearNonRatioId,
                KpiDefinitionId = nonRatioKpi.Id,
                DepartmentId = branch.Id,
                EvaluationPeriodId = quarterIds[0],
                TargetValue = 250m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = branchYearNonRatioId,
                KpiDefinitionId = nonRatioKpi.Id,
                DepartmentId = branch.Id,
                EvaluationPeriodId = quarterIds[1],
                TargetValue = 250m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = branchYearNonRatioId,
                KpiDefinitionId = nonRatioKpi.Id,
                DepartmentId = branch.Id,
                EvaluationPeriodId = quarterIds[2],
                TargetValue = 250m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = branchYearNonRatioId,
                KpiDefinitionId = nonRatioKpi.Id,
                DepartmentId = branch.Id,
                EvaluationPeriodId = quarterIds[3],
                TargetValue = 250m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            }
        };

        db.Departments.Add(branch);
        db.KpiGroups.Add(group);
        db.KpiDefinitions.AddRange(ratioKpi, nonRatioKpi);
        db.EvaluationPeriods.Add(yearPeriod);
        db.EvaluationPeriods.AddRange(h1Period, h2Period);
        db.EvaluationPeriods.AddRange(quarters);
        db.TargetAssignments.Add(branchYearRatio);
        db.TargetAssignments.Add(branchYearNonRatio);
        db.TargetAssignments.AddRange(quarterSplits);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var uow = new UnitOfWork(db);
        var permissionService = new AllocationPermissions(admin: true);
        var service = new TargetSetupService(uow, db, permissionService, new NoOpTargetChangeLogService(), new RecordingNotifier(), new NoOpDepartmentService());

        return new Fixture(
            db,
            service,
            branch.Id,
            userId,
            year,
            yearPeriod.Id,
            quarterIds,
            ratioKpi.Id,
            nonRatioKpi.Id,
            branchYearRatioId,
            branchYearNonRatioId
        );
    }

    /// <summary>One KPI of the year published, the other still DRAFT: the year stays editable for the DRAFT KPI only.</summary>
    private static async Task PublishNonRatioKpiOnlyAsync(Fixture f)
    {
        var row = await f.Db.TargetAssignments.SingleAsync(t => t.Id == f.BranchYearNonRatioId);
        row.Status = TargetAssignmentStatus.Active;
        row.ApprovedAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetQuartersAsync_PartPublishedYear_StaysDraftWhateverRowIsTheAnchor()
    {
        var f = await CreateAsync();
        await PublishNonRatioKpiOnlyAsync(f);

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        Assert.Equal("DRAFT", result.Status);
    }

    [Fact]
    public async Task SaveQuartersAsync_KpiAlreadyPublishedInAPartPublishedYear_Throws409()
    {
        var f = await CreateAsync();
        await PublishNonRatioKpiOnlyAsync(f);
        var request = new SaveQuarterSplitRequest([new SaveQuarterSplitRowRequest(f.NonRatioKpiId, 300m, 300m, 200m, 200m)]);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task GetQuartersAsync_NormalCase_Returns2KpisWithQuartersMatched()
    {
        var f = await CreateAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(f.Year, result.Year);
        Assert.Equal(f.BranchYearNonRatioId, result.BranchYearId);
        Assert.Equal("DRAFT", result.Status);
        Assert.Equal(4, result.Quarters.Count);

        // Find the non-ratio KPI row (should have matched = true because sum = 1000)
        var nonRatioRow = result.Rows.FirstOrDefault(r => r.KpiId == f.NonRatioKpiId);
        Assert.NotNull(nonRatioRow);
        Assert.False(nonRatioRow.IsRatio);
        Assert.Equal(250m, nonRatioRow.Q1);
        Assert.Equal(250m, nonRatioRow.Q2);
        Assert.Equal(250m, nonRatioRow.Q3);
        Assert.Equal(250m, nonRatioRow.Q4);
        Assert.Equal(1000m, nonRatioRow.Sum);
        Assert.Equal(0m, nonRatioRow.Diff);
        Assert.True(nonRatioRow.Matched);
    }

    [Fact]
    public async Task GetQuartersAsync_RatioKpiRow_ReturnsSameValueInAllQuarters()
    {
        var f = await CreateAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearRatioId, f.UserId, CancellationToken.None);

        // Find the ratio KPI row
        var ratioRow = result.Rows.FirstOrDefault(r => r.KpiId == f.RatioKpiId);
        Assert.NotNull(ratioRow);
        Assert.True(ratioRow.IsRatio);
        Assert.Equal(80m, ratioRow.Q1);
        Assert.Equal(80m, ratioRow.Q2);
        Assert.Equal(80m, ratioRow.Q3);
        Assert.Equal(80m, ratioRow.Q4);
    }

    [Fact]
    public async Task GetQuartersAsync_LockedQuarter_ReturnsFlagAsTrue()
    {
        var f = await CreateAsync(status: PeriodStatus.Locked);

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        // All quarters should be locked
        Assert.True(result.Quarters.All(q => q.Locked));
    }

    [Fact]
    public async Task GetQuartersAsync_BranchYearNotFound_Throws404()
    {
        var f = await CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetQuartersAsync(Guid.NewGuid(), f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetQuartersAsync_InvalidBranchYearType_Throws404()
    {
        var f = await CreateAsync();

        // Create a quarter row (not a valid branch-year)
        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetQuartersAsync(f.QuarterPeriodIds[0], f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetQuartersAsync_OtherBranch_ThrowsForbidden()
    {
        var f = await CreateAsync();

        // Create a different branch and a branch-year for it
        var otherBranch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BRANCH-002",
            Name = "Branch 002",
            LevelType = DepartmentLevelType.Branch
        };
        f.Db.Departments.Add(otherBranch);

        var otherBranchYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = otherBranch.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft
        };
        f.Db.TargetAssignments.Add(otherBranchYear);
        await f.Db.SaveChangesAsync();

        // Should throw forbidden because user doesn't have access to other branch
        var restrictedPermission = new AllocationPermissions(admin: false);
        var restrictedService = new TargetSetupService(new UnitOfWork(f.Db), f.Db, restrictedPermission, new NoOpTargetChangeLogService(), new RecordingNotifier(), new NoOpDepartmentService());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            restrictedService.GetQuartersAsync(otherBranchYear.Id, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetQuartersAsync_ReturnsPreviousYearQuarters()
    {
        var f = await CreateAsync();

        // Create previous year (2026) with quarter periods (current year in fixture is 2027)
        var prevYear = 2026;
        var prevYearPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = prevYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = $"Year {prevYear}",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(prevYear, 1, 1),
            EndDate = new DateOnly(prevYear, 12, 31),
            Status = PeriodStatus.Locked
        };

        // Create half-years for previous year
        var prevH1Period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"{prevYear}-H1",
            Name = "H1",
            ParentPeriodId = prevYearPeriod.Id,
            PeriodType = PeriodType.HalfYear,
            StartDate = new DateOnly(prevYear, 1, 1),
            EndDate = new DateOnly(prevYear, 6, 30),
            Status = PeriodStatus.Locked
        };
        var prevH2Period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"{prevYear}-H2",
            Name = "H2",
            ParentPeriodId = prevYearPeriod.Id,
            PeriodType = PeriodType.HalfYear,
            StartDate = new DateOnly(prevYear, 7, 1),
            EndDate = new DateOnly(prevYear, 12, 31),
            Status = PeriodStatus.Locked
        };

        // Create 4 quarter periods for previous year
        var prevQuarterIds = new Guid[4];
        var prevQuarters = new List<EvaluationPeriod>();
        var prevQuarterDates = new[]
        {
            (new DateOnly(prevYear, 1, 1), new DateOnly(prevYear, 3, 31), prevH1Period.Id),
            (new DateOnly(prevYear, 4, 1), new DateOnly(prevYear, 6, 30), prevH1Period.Id),
            (new DateOnly(prevYear, 7, 1), new DateOnly(prevYear, 9, 30), prevH2Period.Id),
            (new DateOnly(prevYear, 10, 1), new DateOnly(prevYear, 12, 31), prevH2Period.Id)
        };

        for (int q = 1; q <= 4; q++)
        {
            var (start, end, parentId) = prevQuarterDates[q - 1];
            var qId = Guid.NewGuid();
            prevQuarterIds[q - 1] = qId;

            prevQuarters.Add(new EvaluationPeriod
            {
                Id = qId,
                Code = $"{prevYear}-Q{q}",
                Name = $"Q{q}",
                ParentPeriodId = parentId,
                PeriodType = PeriodType.Quarter,
                StartDate = start,
                EndDate = end,
                Status = PeriodStatus.Locked
            });
        }

        // Create previous year branch-year row and ACTIVE quarter children
        var prevBranchYearId = Guid.NewGuid();
        var prevBranchYear = new TargetAssignment
        {
            Id = prevBranchYearId,
            DepartmentId = f.BranchId,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = prevYearPeriod.Id,
            TargetValue = 800m,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var prevQuarterChildren = new List<TargetAssignment>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = prevBranchYearId,
                KpiDefinitionId = f.NonRatioKpiId,
                DepartmentId = f.BranchId,
                EvaluationPeriodId = prevQuarterIds[0],
                TargetValue = 200m,
                Version = 1,
                Status = TargetAssignmentStatus.Active,
                SplitDimension = TargetSplitDimension.Period
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = prevBranchYearId,
                KpiDefinitionId = f.NonRatioKpiId,
                DepartmentId = f.BranchId,
                EvaluationPeriodId = prevQuarterIds[1],
                TargetValue = 200m,
                Version = 1,
                Status = TargetAssignmentStatus.Active,
                SplitDimension = TargetSplitDimension.Period
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = prevBranchYearId,
                KpiDefinitionId = f.NonRatioKpiId,
                DepartmentId = f.BranchId,
                EvaluationPeriodId = prevQuarterIds[2],
                TargetValue = 200m,
                Version = 1,
                Status = TargetAssignmentStatus.Active,
                SplitDimension = TargetSplitDimension.Period
            },
            new()
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = prevBranchYearId,
                KpiDefinitionId = f.NonRatioKpiId,
                DepartmentId = f.BranchId,
                EvaluationPeriodId = prevQuarterIds[3],
                TargetValue = 200m,
                Version = 1,
                Status = TargetAssignmentStatus.Active,
                SplitDimension = TargetSplitDimension.Period
            }
        };

        f.Db.EvaluationPeriods.Add(prevYearPeriod);
        f.Db.EvaluationPeriods.AddRange(prevH1Period, prevH2Period);
        f.Db.EvaluationPeriods.AddRange(prevQuarters);
        f.Db.TargetAssignments.Add(prevBranchYear);
        f.Db.TargetAssignments.AddRange(prevQuarterChildren);
        await f.Db.SaveChangesAsync();

        // Get quarters for current year
        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        // Find the non-ratio KPI row and check previousYear is populated
        var nonRatioRow = result.Rows.FirstOrDefault(r => r.KpiId == f.NonRatioKpiId);
        Assert.NotNull(nonRatioRow);
        Assert.NotNull(nonRatioRow.PreviousYear);
        Assert.Equal(200m, nonRatioRow.PreviousYear.Q1);
        Assert.Equal(200m, nonRatioRow.PreviousYear.Q2);
        Assert.Equal(200m, nonRatioRow.PreviousYear.Q3);
        Assert.Equal(200m, nonRatioRow.PreviousYear.Q4);
        Assert.Equal(800m, nonRatioRow.PreviousYearValue);
    }

    /// <summary>
    /// Creates a previous-year period (with half-years and 4 quarters). EvaluationPeriods are global (not
    /// department-scoped), so this must be called at most once per `prevYear` in a test — `GetYearAsync(prevYear)`
    /// looks the row up by `Code` alone, with no department filter, and would be ambiguous with two rows sharing it.
    /// Returns the year period id and the 4 quarter period ids in order.
    /// </summary>
    private static async Task<(Guid YearPeriodId, Guid[] QuarterPeriodIds)> SeedPreviousYearPeriodsAsync(Fixture f, int prevYear)
    {
        var prevYearPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = prevYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = $"Year {prevYear}",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(prevYear, 1, 1),
            EndDate = new DateOnly(prevYear, 12, 31),
            Status = PeriodStatus.Locked
        };
        var prevH1Period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"{prevYear}-H1",
            Name = "H1",
            ParentPeriodId = prevYearPeriod.Id,
            PeriodType = PeriodType.HalfYear,
            StartDate = new DateOnly(prevYear, 1, 1),
            EndDate = new DateOnly(prevYear, 6, 30),
            Status = PeriodStatus.Locked
        };
        var prevH2Period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"{prevYear}-H2",
            Name = "H2",
            ParentPeriodId = prevYearPeriod.Id,
            PeriodType = PeriodType.HalfYear,
            StartDate = new DateOnly(prevYear, 7, 1),
            EndDate = new DateOnly(prevYear, 12, 31),
            Status = PeriodStatus.Locked
        };

        var prevQuarterDates = new[]
        {
            (new DateOnly(prevYear, 1, 1), new DateOnly(prevYear, 3, 31), prevH1Period.Id),
            (new DateOnly(prevYear, 4, 1), new DateOnly(prevYear, 6, 30), prevH1Period.Id),
            (new DateOnly(prevYear, 7, 1), new DateOnly(prevYear, 9, 30), prevH2Period.Id),
            (new DateOnly(prevYear, 10, 1), new DateOnly(prevYear, 12, 31), prevH2Period.Id)
        };

        var prevQuarters = new List<EvaluationPeriod>();
        for (int q = 1; q <= 4; q++)
        {
            var (start, end, parentId) = prevQuarterDates[q - 1];
            prevQuarters.Add(new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = $"{prevYear}-Q{q}",
                Name = $"Q{q}",
                ParentPeriodId = parentId,
                PeriodType = PeriodType.Quarter,
                StartDate = start,
                EndDate = end,
                Status = PeriodStatus.Locked
            });
        }

        f.Db.EvaluationPeriods.Add(prevYearPeriod);
        f.Db.EvaluationPeriods.AddRange(prevH1Period, prevH2Period);
        f.Db.EvaluationPeriods.AddRange(prevQuarters);
        await f.Db.SaveChangesAsync();

        return (prevYearPeriod.Id, prevQuarters.Select(q => q.Id).ToArray());
    }

    /// <summary>
    /// Creates a previous-year branch-year row and ACTIVE quarter children for `departmentId` + `kpiId` under the
    /// given (already-seeded) year/quarter periods, so VALID ActualProgress rows can be attached under this tree.
    /// </summary>
    private static async Task<TargetAssignment[]> SeedPreviousYearTargetTreeAsync(
        Fixture f, Guid departmentId, Guid kpiId, Guid yearPeriodId, Guid[] quarterPeriodIds, decimal yearValue, decimal[] quarterValues)
    {
        var prevBranchYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = departmentId,
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = yearPeriodId,
            TargetValue = yearValue,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var prevQuarterChildren = quarterPeriodIds.Select((periodId, idx) => new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = prevBranchYear.Id,
            KpiDefinitionId = kpiId,
            DepartmentId = departmentId,
            EvaluationPeriodId = periodId,
            TargetValue = quarterValues[idx],
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            SplitDimension = TargetSplitDimension.Period
        }).ToArray();

        f.Db.TargetAssignments.Add(prevBranchYear);
        f.Db.TargetAssignments.AddRange(prevQuarterChildren);
        await f.Db.SaveChangesAsync();

        return prevQuarterChildren;
    }

    /// <summary>Seeds previous-year periods plus a previous-year branch-year row and ACTIVE quarter children for the
    /// branch in `f`. Convenience wrapper over <see cref="SeedPreviousYearPeriodsAsync"/> +
    /// <see cref="SeedPreviousYearTargetTreeAsync"/> for tests that only need one branch's tree.</summary>
    private static async Task<TargetAssignment[]> SeedPreviousYearQuartersAsync(
        Fixture f, Guid kpiId, int prevYear, decimal yearValue, decimal[] quarterValues)
    {
        var (yearPeriodId, quarterPeriodIds) = await SeedPreviousYearPeriodsAsync(f, prevYear);
        return await SeedPreviousYearTargetTreeAsync(f, f.BranchId, kpiId, yearPeriodId, quarterPeriodIds, yearValue, quarterValues);
    }

    /// <summary>Adds a child TargetAssignment under `parentId` (e.g. a team or employee-level split); saves immediately.</summary>
    private static async Task<TargetAssignment> AddDescendantAsync(
        Fixture f, Guid parentId, Guid kpiId, Guid departmentId, Guid periodId, decimal value)
    {
        var node = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = parentId,
            KpiDefinitionId = kpiId,
            DepartmentId = departmentId,
            EvaluationPeriodId = periodId,
            TargetValue = value,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            SplitDimension = TargetSplitDimension.Period
        };
        f.Db.TargetAssignments.Add(node);
        await f.Db.SaveChangesAsync();
        return node;
    }

    [Fact]
    public async Task GetQuartersAsync_PreviousYearActualsInAllQuarters_SummedPerQuarter()
    {
        var f = await CreateAsync();
        var prevQuarterChildren = await SeedPreviousYearQuartersAsync(f, f.NonRatioKpiId, f.Year - 1, 800m, [200m, 200m, 200m, 200m]);

        // Actuals live deep under the branch tree (team -> employee), not on the quarter rows themselves,
        // mirroring real data where VALID actual_progress sits on employee-level assignments several levels
        // below the branch root.
        var q1Team = await AddDescendantAsync(f, prevQuarterChildren[0].Id, f.NonRatioKpiId, f.BranchId, prevQuarterChildren[0].EvaluationPeriodId, 100m);
        var q1Employee = await AddDescendantAsync(f, q1Team.Id, f.NonRatioKpiId, f.BranchId, prevQuarterChildren[0].EvaluationPeriodId, 50m);
        var q2Employee = await AddDescendantAsync(f, prevQuarterChildren[1].Id, f.NonRatioKpiId, f.BranchId, prevQuarterChildren[1].EvaluationPeriodId, 60m);
        var q3Employee = await AddDescendantAsync(f, prevQuarterChildren[2].Id, f.NonRatioKpiId, f.BranchId, prevQuarterChildren[2].EvaluationPeriodId, 70m);
        var q4Employee = await AddDescendantAsync(f, prevQuarterChildren[3].Id, f.NonRatioKpiId, f.BranchId, prevQuarterChildren[3].EvaluationPeriodId, 80m);

        // Two VALID actual progress entries in Q1 (summed, on the deepest employee node), one per remaining quarter
        f.Db.ActualProgresses.AddRange(
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = q1Employee.Id, ProgressDate = new DateOnly(f.Year - 1, 1, 10), Value = 30m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow },
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = q1Employee.Id, ProgressDate = new DateOnly(f.Year - 1, 2, 10), Value = 20m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow },
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = q2Employee.Id, ProgressDate = new DateOnly(f.Year - 1, 5, 10), Value = 60m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow },
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = q3Employee.Id, ProgressDate = new DateOnly(f.Year - 1, 8, 10), Value = 70m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow },
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = q4Employee.Id, ProgressDate = new DateOnly(f.Year - 1, 11, 10), Value = 80m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == f.NonRatioKpiId);
        Assert.NotNull(row.PreviousYearActual);
        Assert.Equal(50m, row.PreviousYearActual.Q1);
        Assert.Equal(60m, row.PreviousYearActual.Q2);
        Assert.Equal(70m, row.PreviousYearActual.Q3);
        Assert.Equal(80m, row.PreviousYearActual.Q4);
    }

    [Fact]
    public async Task GetQuartersAsync_PreviousYearActualsOnSiblingBranchTree_Excluded()
    {
        var f = await CreateAsync();

        // EvaluationPeriods are global: seed them once and build both branches' TargetAssignment trees under
        // the same previous-year/quarter periods.
        var (yearPeriodId, quarterPeriodIds) = await SeedPreviousYearPeriodsAsync(f, f.Year - 1);
        var prevQuarterChildren = await SeedPreviousYearTargetTreeAsync(
            f, f.BranchId, f.NonRatioKpiId, yearPeriodId, quarterPeriodIds, 800m, [200m, 200m, 200m, 200m]);
        var q1Employee = await AddDescendantAsync(f, prevQuarterChildren[0].Id, f.NonRatioKpiId, f.BranchId, prevQuarterChildren[0].EvaluationPeriodId, 50m);
        f.Db.ActualProgresses.Add(new ActualProgress
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = q1Employee.Id,
            ProgressDate = new DateOnly(f.Year - 1, 1, 10),
            Value = 30m,
            Status = ActualProgressStatus.Valid,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        // A sibling branch has its own previous-year tree (same KPI, same periods) with a VALID actual; it must
        // never leak into this branch's suggestion even though it is the same previous year and the same KPI.
        var otherBranch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BRANCH-002",
            Name = "Branch 002",
            LevelType = DepartmentLevelType.Branch
        };
        f.Db.Departments.Add(otherBranch);
        await f.Db.SaveChangesAsync();
        var otherPrevQuarterChildren = await SeedPreviousYearTargetTreeAsync(
            f, otherBranch.Id, f.NonRatioKpiId, yearPeriodId, quarterPeriodIds, 800m, [200m, 200m, 200m, 200m]);
        var otherEmployee = await AddDescendantAsync(f, otherPrevQuarterChildren[0].Id, f.NonRatioKpiId, otherBranch.Id, otherPrevQuarterChildren[0].EvaluationPeriodId, 999m);
        f.Db.ActualProgresses.Add(new ActualProgress
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = otherEmployee.Id,
            ProgressDate = new DateOnly(f.Year - 1, 1, 15),
            Value = 999m,
            Status = ActualProgressStatus.Valid,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == f.NonRatioKpiId);
        Assert.NotNull(row.PreviousYearActual);
        Assert.Equal(30m, row.PreviousYearActual.Q1);
        Assert.Equal(0m, row.PreviousYearActual.Q2);
    }

    [Fact]
    public async Task GetQuartersAsync_PreviousYearActualsNotValid_ExcludedAndReturnsNull()
    {
        var f = await CreateAsync();
        var prevQuarterChildren = await SeedPreviousYearQuartersAsync(f, f.NonRatioKpiId, f.Year - 1, 800m, [200m, 200m, 200m, 200m]);

        f.Db.ActualProgresses.Add(new ActualProgress
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = prevQuarterChildren[0].Id,
            ProgressDate = new DateOnly(f.Year - 1, 1, 10),
            Value = 999m,
            Status = ActualProgressStatus.Invalid,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == f.NonRatioKpiId);
        Assert.Null(row.PreviousYearActual);
    }

    [Fact]
    public async Task GetQuartersAsync_NoPreviousYearOrActuals_ReturnsNull()
    {
        var f = await CreateAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == f.NonRatioKpiId);
        Assert.Null(row.PreviousYearActual);
    }

    [Fact]
    public async Task GetQuartersAsync_RatioKpi_PreviousYearActualAlwaysNull()
    {
        var f = await CreateAsync();
        var prevQuarterChildren = await SeedPreviousYearQuartersAsync(f, f.RatioKpiId, f.Year - 1, 80m, [80m, 80m, 80m, 80m]);

        f.Db.ActualProgresses.Add(new ActualProgress
        {
            Id = Guid.NewGuid(),
            TargetAssignmentId = prevQuarterChildren[0].Id,
            ProgressDate = new DateOnly(f.Year - 1, 1, 10),
            Value = 75m,
            Status = ActualProgressStatus.Valid,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == f.RatioKpiId);
        Assert.True(row.IsRatio);
        Assert.Null(row.PreviousYearActual);
    }

    [Fact]
    public async Task GetQuartersAsync_UnsplitSumKpi_ReturnsNullQuartersAndGroup()
    {
        var f = await CreateAsync();
        f.Db.TargetAssignments.RemoveRange(f.Db.TargetAssignments.Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId));
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == f.NonRatioKpiId);
        Assert.Null(row.Q1);
        Assert.Null(row.Q4);
        Assert.Equal(0m, row.Sum);
        Assert.Equal(1000m, row.Diff);
        Assert.False(row.Matched);
        Assert.Null(row.PreviousYearValue);
        Assert.Equal("G", row.GroupCode);
        Assert.Equal("Group", row.GroupName);
        Assert.Equal("SUM", row.AggregationType);
        Assert.Equal(["KPI_RATIO", "KPI_REVENUE"], result.Rows.Select(r => r.KpiCode).ToList());
    }

    [Fact]
    public async Task GetQuartersAsync_AvgKpi_KeepsYearValueInEachQuarter()
    {
        var f = await CreateAsync();
        var (avgKpiId, _) = await AddAvgKpiAsync(f, 500m);

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == avgKpiId);
        Assert.True(row.IsRatio);
        Assert.Equal("AVG", row.AggregationType);
        Assert.Equal(500m, row.Q1);
        Assert.Equal(500m, row.Q4);
        Assert.Equal(500m, row.Sum);
        Assert.True(row.Matched);
    }

    [Fact]
    public async Task SaveQuarters_AvgKpi_IgnoresRequestAndAutoFillsYearValue()
    {
        var f = await CreateAsync();
        var (avgKpiId, avgYearId) = await AddAvgKpiAsync(f, 500m);

        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 250m, 250m, 250m, 250m),
            new(avgKpiId, 125m, 125m, 125m, 125m)
        });

        await f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None);

        var avgQuarters = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == avgYearId && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, avgQuarters.Count);
        Assert.All(avgQuarters, q => Assert.Equal(500m, q.TargetValue));

        var setup = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);
        Assert.Equal(2, setup.Quarters.Total);
        Assert.Equal(2, setup.Quarters.Done);
    }

    [Fact]
    public async Task SaveAllocation_AvgKpi_IsRejectedAsBranchWide()
    {
        var f = await CreateAsync();
        var (avgKpiId, _) = await AddAvgKpiAsync(f, 500m);

        await Assert.ThrowsAsync<ValidationException>(() => f.Service.SaveAllocationAsync(
            f.BranchYearNonRatioId, avgKpiId, new SaveKpiAllocationRequest([]), f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task SaveQuarters_AvgKpiSplitUnderOldRule_ResetsQuartersToYearValue()
    {
        var f = await CreateAsync();
        var (avgKpiId, avgYearId) = await AddAvgKpiAsync(f, 4.5m);
        decimal[] oldSplit = [0.99m, 1.08m, 1.125m, 1.305m];
        foreach (var (quarterId, value) in f.QuarterPeriodIds.Zip(oldSplit))
        {
            f.Db.TargetAssignments.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = avgYearId,
                DepartmentId = f.BranchId,
                KpiDefinitionId = avgKpiId,
                EvaluationPeriodId = quarterId,
                TargetValue = value,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            });
        }
        await f.Db.SaveChangesAsync();

        var allocation = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, avgKpiId, f.UserId, CancellationToken.None);
        Assert.Equal(4.5m, allocation.Branch.Q1);
        Assert.Equal(4.5m, allocation.Branch.Q4);

        await f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, new SaveQuarterSplitRequest([]), f.UserId, CancellationToken.None);

        var stored = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == avgYearId && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, stored.Count);
        Assert.All(stored, q => Assert.Equal(4.5m, q.TargetValue));
    }

    [Fact]
    public async Task GetSetup_AvgKpiWithoutQuarterRows_CountsQuartersAsDone()
    {
        var f = await CreateAsync();
        var (avgKpiId, _) = await AddAvgKpiAsync(f, 500m);

        var setup = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);

        Assert.Equal(2, setup.Quarters.Total);
        Assert.Equal(2, setup.Quarters.Done);
        Assert.Equal("ALLOCATE", setup.Step);
        // AVG KPIs keep one branch value: not counted in step 3 and never a publish blocker.
        Assert.Equal(1, setup.Allocation.Total);
        Assert.DoesNotContain(setup.Blockers, b => b.KpiId == avgKpiId);
    }

    [Fact]
    public async Task SaveAllocation_RatioAggregationKpi_IsRejected()
    {
        var f = await CreateAsync();
        var (ratioKpiId, _) = await AddAvgKpiAsync(f, 40m, KpiAggregationType.Ratio);

        await Assert.ThrowsAsync<ValidationException>(() => f.Service.SaveAllocationAsync(
            f.BranchYearNonRatioId, ratioKpiId, new SaveKpiAllocationRequest([]), f.UserId, CancellationToken.None));
    }

    /// <summary>Adds a non-SUM KPI (AVG by default) with a DRAFT branch-year target and no quarter rows.</summary>
    private static async Task<(Guid KpiId, Guid BranchYearId)> AddAvgKpiAsync(
        Fixture f, decimal yearValue, KpiAggregationType aggregation = KpiAggregationType.Avg)
    {
        var kpiId = Guid.NewGuid();
        var yearId = Guid.NewGuid();
        f.Db.KpiDefinitions.Add(new KpiDefinition
        {
            Id = kpiId,
            KpiGroupId = f.Db.KpiGroups.First().Id,
            Code = "KPI_CASA_AVG",
            Name = "CASA bình quân",
            Unit = "VND",
            AggregationType = aggregation,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        });
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = yearId,
            DepartmentId = f.BranchId,
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = yearValue,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();
        return (kpiId, yearId);
    }

    [Fact]
    public async Task GetQuartersAsync_ReturnsFileRatiosFromTheImportAuditLog()
    {
        var f = await CreateAsync();
        f.Db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityName = "BranchYearTargetImport",
            EntityId = $"{f.BranchId}:{f.Year}",
            Action = "Import",
            NewValues = "{\"fileName\":\"KPI.xlsx\",\"rowCount\":28,\"quarterRatios\":{\"Q1\":0.22,\"Q2\":0.24,\"Q3\":0.25,\"Q4\":0.29}}",
            ActorId = f.UserId,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetQuartersAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        Assert.NotNull(result.FileRatios);
        Assert.Equal(0.22m, result.FileRatios.Q1);
        Assert.Equal(0.24m, result.FileRatios.Q2);
        Assert.Equal(0.25m, result.FileRatios.Q3);
        Assert.Equal(0.29m, result.FileRatios.Q4);
    }

    [Fact]
    public async Task Save_CreatesFourPeriodRowsPerKpi_AndReturnsMatched()
    {
        var f = await CreateAsync();

        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 300m, 300m, 200m, 200m)
        });

        var result = await f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(f.Year, result.Year);

        // Find the non-ratio KPI row
        var nonRatioRow = result.Rows.FirstOrDefault(r => r.KpiId == f.NonRatioKpiId);
        Assert.NotNull(nonRatioRow);
        Assert.Equal(300m, nonRatioRow.Q1);
        Assert.Equal(300m, nonRatioRow.Q2);
        Assert.Equal(200m, nonRatioRow.Q3);
        Assert.Equal(200m, nonRatioRow.Q4);
        Assert.Equal(1000m, nonRatioRow.Sum);
        Assert.Equal(0m, nonRatioRow.Diff);
        Assert.True(nonRatioRow.Matched);

        // Verify 4 quarter rows were created/updated in DB
        var quarterRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId
                && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, quarterRows.Count);

        // The ratio KPI's quarter rows are freshly auto-filled (not pre-seeded like the non-ratio ones above), so
        // this exercises the create path: each quarter row's EffectiveFrom must be that quarter period's own
        // start date, not the branch-year's.
        var ratioQuarterRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearRatioId && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, ratioQuarterRows.Count);
        foreach (var quarterRow in ratioQuarterRows)
        {
            var quarterPeriod = await f.Db.EvaluationPeriods.SingleAsync(p => p.Id == quarterRow.EvaluationPeriodId);
            Assert.Equal(quarterPeriod.StartDate, quarterRow.EffectiveFrom);
        }
    }

    [Fact]
    public async Task Save_UpdatesExistingRows()
    {
        var f = await CreateAsync();

        // First save: different values
        var request1 = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 400m, 300m, 200m, 100m)
        });

        var result1 = await f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request1, f.UserId, CancellationToken.None);
        var nonRatioRow1 = result1.Rows.FirstOrDefault(r => r.KpiId == f.NonRatioKpiId);
        Assert.NotNull(nonRatioRow1);
        Assert.Equal(1000m, nonRatioRow1.Sum);

        // Second save: change values, sum should still match
        var request2 = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 500m, 300m, 150m, 50m)
        });

        var result2 = await f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request2, f.UserId, CancellationToken.None);
        var nonRatioRow2 = result2.Rows.FirstOrDefault(r => r.KpiId == f.NonRatioKpiId);
        Assert.NotNull(nonRatioRow2);
        Assert.Equal(500m, nonRatioRow2.Q1);
        Assert.Equal(300m, nonRatioRow2.Q2);
        Assert.Equal(150m, nonRatioRow2.Q3);
        Assert.Equal(50m, nonRatioRow2.Q4);
        Assert.Equal(1000m, nonRatioRow2.Sum);

        // Verify still 4 quarter rows (not duplicated)
        var quarterRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId
                && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, quarterRows.Count);
    }

    [Fact]
    public async Task Save_RatioKpi_AutoFillsQuartersWithYearValue()
    {
        var f = await CreateAsync();

        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            // Only request the non-ratio KPI; ratio should be auto-filled
            new(f.NonRatioKpiId, 250m, 250m, 250m, 250m)
        });

        var result = await f.Service.SaveQuartersAsync(f.BranchYearRatioId, request, f.UserId, CancellationToken.None);

        // Find the ratio KPI row
        var ratioRow = result.Rows.FirstOrDefault(r => r.KpiId == f.RatioKpiId);
        Assert.NotNull(ratioRow);
        Assert.True(ratioRow.IsRatio);
        Assert.Equal(80m, ratioRow.Q1);
        Assert.Equal(80m, ratioRow.Q2);
        Assert.Equal(80m, ratioRow.Q3);
        Assert.Equal(80m, ratioRow.Q4);
        Assert.Equal(80m, ratioRow.Sum);
        Assert.True(ratioRow.Matched);

        // Verify 4 quarter rows were created for ratio KPI
        var ratioQuarterRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearRatioId
                && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, ratioQuarterRows.Count);
        Assert.All(ratioQuarterRows, r => Assert.Equal(80m, r.TargetValue));
    }

    [Fact]
    public async Task Save_LockedQuarterChanged_Throws409()
    {
        // Create with locked status
        var f = await CreateAsync(status: PeriodStatus.Locked);

        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 300m, 300m, 200m, 200m) // Changed values, still sum to the year target
        });

        // Should throw ConflictException because quarters are locked
        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Save_LockedQuarterUnchanged_Succeeds()
    {
        // Create with locked status
        var f = await CreateAsync(status: PeriodStatus.Locked);

        // Request the same values as the existing rows (250 each)
        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 250m, 250m, 250m, 250m)
        });

        // Should succeed because values are unchanged
        var result = await f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Save_NotDraft_Throws409()
    {
        var f = await CreateAsync();

        // Change branch-year to Active
        var branchYear = f.Db.TargetAssignments.First(t => t.Id == f.BranchYearNonRatioId);
        branchYear.Status = TargetAssignmentStatus.Active;
        await f.Db.SaveChangesAsync();

        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 250m, 250m, 250m, 250m)
        });

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Save_UnknownKpi_ThrowsValidation()
    {
        var f = await CreateAsync();

        var unknownKpiId = Guid.NewGuid();
        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(unknownKpiId, 250m, 250m, 250m, 250m)
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Save_DuplicateKpis_ThrowsValidation()
    {
        var f = await CreateAsync();

        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 250m, 250m, 250m, 250m),
            new(f.NonRatioKpiId, 300m, 300m, 300m, 300m)  // Duplicate
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Save_NegativeValue_ThrowsValidation()
    {
        var f = await CreateAsync();

        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, -100m, 250m, 250m, 250m)  // Negative Q1
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Save_QuarterSumMismatch_Throws422()
    {
        var f = await CreateAsync();

        // 300 * 4 = 1200, branch year is 1000 -> exact sums are required (G12/R1).
        var request = new SaveQuarterSplitRequest(new List<SaveQuarterSplitRowRequest>
        {
            new(f.NonRatioKpiId, 300m, 300m, 300m, 300m)
        });

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.SaveQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));

        Assert.Equal(ErrorCodes.TargetQuarterSumMismatch, exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
        Assert.NotNull(exception.Errors);
        Assert.Single(exception.Errors);
    }

    [Fact]
    public async Task GetAllocationAsync_ReturnsAllocationWithHeadcountViaProfile()
    {
        var f = await CreateAsync();

        // Create departments under the branch
        var dept1 = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };
        var dept2 = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-002",
            Name = "Department 2",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        // Create evaluation profile for the non-ratio KPI
        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Code = "PROFILE-001",
            Name = "Profile 1",
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(f.Year, 1, 1),
            EffectiveTo = null
        };

        // Create employees in dept1 with the job position that has the profile
        var jobPos1 = new JobPosition
        {
            Id = profile.JobPositionId,
            Code = "TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow
        };

        var profileItem = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            WeightPercent = 100m,
            DisplayOrder = 1
        };

        profile.EvaluationProfileItems.Add(profileItem);

        var emp1 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "Employee 1",
            Email = "emp1@test.com",
            DepartmentId = dept1.Id,
            JobPositionId = jobPos1.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var emp2 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E002",
            FullName = "Employee 2",
            Email = "emp2@test.com",
            DepartmentId = dept1.Id,
            JobPositionId = jobPos1.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        f.Db.Departments.AddRange(dept1, dept2);
        f.Db.JobPositions.Add(jobPos1);
        f.Db.EvaluationProfiles.Add(profile);
        f.Db.Employees.AddRange(emp1, emp2);
        await f.Db.SaveChangesAsync();

        // Call GetAllocationAsync
        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(f.NonRatioKpiId, result.KpiId);
        Assert.Equal(1000m, result.Branch.Year);

        // Should have dept1 with headcount 2, dept2 should be hidden (no employees)
        Assert.Single(result.Departments);
        Assert.Equal(dept1.Id, result.Departments[0].DepartmentId);
        Assert.Equal(2, result.Departments[0].Headcount);

        // Department 2 should be hidden
        Assert.Single(result.HiddenDepartments);
        Assert.Equal(dept2.Id, result.HiddenDepartments[0].DepartmentId);
    }

    [Fact]
    public async Task GetAllocationAsync_SecondKpi_ReadsQuartersOfItsOwnBranchYearRow()
    {
        var f = await CreateAsync();
        var kpiId = Guid.NewGuid();
        var yearId = Guid.NewGuid();
        f.Db.KpiDefinitions.Add(new KpiDefinition
        {
            Id = kpiId,
            KpiGroupId = f.Db.KpiGroups.First().Id,
            Code = "KPI_SECOND",
            Name = "Second",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        });
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = yearId,
            DepartmentId = f.BranchId,
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 400m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        });
        foreach (var quarterId in f.QuarterPeriodIds)
        {
            f.Db.TargetAssignments.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = yearId,
                DepartmentId = f.BranchId,
                KpiDefinitionId = kpiId,
                EvaluationPeriodId = quarterId,
                TargetValue = 100m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            });
        }
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, kpiId, f.UserId, CancellationToken.None);

        Assert.Equal(400m, result.Branch.Year);
        Assert.Equal(100m, result.Branch.Q4);
    }

    [Fact]
    public async Task GetAllocationAsync_HeadcountFallbackViaWorkType()
    {
        var f = await CreateAsync();

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        // Create job position without active profile
        var jobPos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "SALES",
            Title = "Sales Officer",
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow
        };

        // Create work type and WorkTypeKpi for fallback
        var workType = new WorkType
        {
            Id = Guid.NewGuid(),
            Code = "WT-001",
            Name = "Sales Work Type",
            Status = RecordStatus.Active
        };

        var workTypeKpi = new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = workType.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EffectiveFrom = new DateOnly(f.Year, 1, 1),
            EffectiveTo = null
        };

        var jobPosWorkType = new JobPositionWorkType
        {
            Id = Guid.NewGuid(),
            JobPositionId = jobPos.Id,
            WorkTypeId = workType.Id
        };

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "Employee 1",
            Email = "emp1@test.com",
            DepartmentId = dept.Id,
            JobPositionId = jobPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        f.Db.Departments.Add(dept);
        f.Db.JobPositions.Add(jobPos);
        f.Db.WorkTypes.Add(workType);
        f.Db.WorkTypeKpis.Add(workTypeKpi);
        f.Db.JobPositionWorkTypes.Add(jobPosWorkType);
        f.Db.Employees.Add(emp);
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Departments);
        Assert.Equal(1, result.Departments[0].Headcount);
    }

    [Fact]
    public async Task GetAllocationAsync_NoHiddenDepartmentsWhenAllEligible()
    {
        var f = await CreateAsync();

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        var jobPos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow
        };

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = jobPos.Id,
            Code = "PROFILE-001",
            Name = "Profile 1",
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(f.Year, 1, 1),
            EffectiveTo = null
        };

        var profileItem = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            WeightPercent = 100m,
            DisplayOrder = 1
        };

        profile.EvaluationProfileItems.Add(profileItem);

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "Employee 1",
            Email = "emp1@test.com",
            DepartmentId = dept.Id,
            JobPositionId = jobPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        f.Db.Departments.Add(dept);
        f.Db.JobPositions.Add(jobPos);
        f.Db.EvaluationProfiles.Add(profile);
        f.Db.Employees.Add(emp);
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.HiddenDepartments);
    }

    [Fact]
    public async Task GetAllocationAsync_KpiNotFound_Throws404()
    {
        var f = await CreateAsync();

        var unknownKpiId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetAllocationAsync(f.BranchYearNonRatioId, unknownKpiId, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAllocationAsync_InvalidBranchYear_Throws404()
    {
        var f = await CreateAsync();

        var invalidBranchYearId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetAllocationAsync(invalidBranchYearId, f.NonRatioKpiId, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAllAllocationsAsync_ReturnsAllocatableKpis_ExcludesRatio()
    {
        var f = await CreateAsync();

        var result = await f.Service.GetAllAllocationsAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(f.NonRatioKpiId, result[0].KpiId);
        Assert.DoesNotContain(result, r => r.KpiId == f.RatioKpiId);
    }

    [Fact]
    public async Task GetAllAllocationsAsync_RowMatchesPerKpiGet()
    {
        var f = await CreateAsync();

        var single = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);
        var all = await f.Service.GetAllAllocationsAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        var fromAll = Assert.Single(all);
        Assert.Equal(single.KpiId, fromAll.KpiId);
        Assert.Equal(single.KpiName, fromAll.KpiName);
        Assert.Equal(single.Branch.Year, fromAll.Branch.Year);
        Assert.Equal(single.Branch.Q1, fromAll.Branch.Q1);
        Assert.Equal(single.Branch.Q2, fromAll.Branch.Q2);
        Assert.Equal(single.Branch.Q3, fromAll.Branch.Q3);
        Assert.Equal(single.Branch.Q4, fromAll.Branch.Q4);
        Assert.Equal(single.Totals.Year, fromAll.Totals.Year);
        Assert.Equal(single.Remaining.Year, fromAll.Remaining.Year);
    }

    [Fact]
    public async Task GetAllAllocationsAsync_OmitsKpiWithQuarterSplitNotReady()
    {
        var f = await CreateAsync();

        // A second SUM KPI whose branch-year row exists but has no quarter split yet (not ready).
        var notReadyKpiId = Guid.NewGuid();
        f.Db.KpiDefinitions.Add(new KpiDefinition
        {
            Id = notReadyKpiId,
            KpiGroupId = f.Db.KpiGroups.First().Id,
            Code = "KPI_NOTREADY",
            Name = "Not Ready",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        });
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = f.BranchId,
            KpiDefinitionId = notReadyKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 500m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetAllAllocationsAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        Assert.Single(result);
        Assert.DoesNotContain(result, r => r.KpiId == notReadyKpiId);
    }

    [Fact]
    public async Task GetAllAllocationsAsync_UnknownBranchYear_Throws404()
    {
        var f = await CreateAsync();

        var invalidBranchYearId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetAllAllocationsAsync(invalidBranchYearId, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAllAllocationsAsync_OtherBranch_ThrowsForbidden()
    {
        var f = await CreateAsync();

        var otherBranch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BRANCH-002",
            Name = "Branch 002",
            LevelType = DepartmentLevelType.Branch
        };

        var otherBranchYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = otherBranch.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft
        };

        f.Db.Departments.Add(otherBranch);
        f.Db.TargetAssignments.Add(otherBranchYear);
        await f.Db.SaveChangesAsync();

        var restrictedPermission = new AllocationPermissions(admin: false);
        var restrictedService = new TargetSetupService(new UnitOfWork(f.Db), f.Db, restrictedPermission, new NoOpTargetChangeLogService(), new RecordingNotifier(), new NoOpDepartmentService());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            restrictedService.GetAllAllocationsAsync(otherBranchYear.Id, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAllocationAsync_OtherBranch_ThrowsForbidden()
    {
        var f = await CreateAsync();

        // Create a different branch
        var otherBranch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BRANCH-002",
            Name = "Branch 002",
            LevelType = DepartmentLevelType.Branch
        };

        var otherBranchYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = otherBranch.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft
        };

        f.Db.Departments.Add(otherBranch);
        f.Db.TargetAssignments.Add(otherBranchYear);
        await f.Db.SaveChangesAsync();

        var restrictedPermission = new AllocationPermissions(admin: false);
        var restrictedService = new TargetSetupService(new UnitOfWork(f.Db), f.Db, restrictedPermission, new NoOpTargetChangeLogService(), new RecordingNotifier(), new NoOpDepartmentService());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            restrictedService.GetAllocationAsync(otherBranchYear.Id, f.NonRatioKpiId, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetAllocationAsync_CalculatesTotalsAndRemaining()
    {
        var f = await CreateAsync();

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        var jobPos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow
        };

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = jobPos.Id,
            Code = "PROFILE-001",
            Name = "Profile 1",
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(f.Year, 1, 1),
            EffectiveTo = null
        };

        var profileItem = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            WeightPercent = 100m,
            DisplayOrder = 1
        };

        profile.EvaluationProfileItems.Add(profileItem);

        // Create a department-year allocation row with partial allocation
        var deptYearId = Guid.NewGuid();
        var deptYear = new TargetAssignment
        {
            Id = deptYearId,
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = dept.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 600m,  // Less than 1000
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            SplitDimension = TargetSplitDimension.Org
        };

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "Employee 1",
            Email = "emp1@test.com",
            DepartmentId = dept.Id,
            JobPositionId = jobPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        f.Db.Departments.Add(dept);
        f.Db.JobPositions.Add(jobPos);
        f.Db.EvaluationProfiles.Add(profile);
        f.Db.TargetAssignments.Add(deptYear);
        f.Db.Employees.Add(emp);
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(600m, result.Totals.Year);
        Assert.Equal(400m, result.Remaining.Year);  // 1000 - 600
    }

    [Fact]
    public async Task GetAllocationAsync_PreviousYearActual_SummedFromDescendantAssignment()
    {
        var f = await CreateAsync();
        var dept = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId);

        var (prevYearPeriodId, _) = await SeedPreviousYearPeriodsAsync(f, f.Year - 1);
        var (deptPrevYearId, employeeAssignmentId) = await SeedDeptPreviousYearActualTreeAsync(
            f, dept.Id, f.NonRatioKpiId, prevYearPeriodId, 800m);

        f.Db.ActualProgresses.AddRange(
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = employeeAssignmentId, ProgressDate = new DateOnly(f.Year - 1, 3, 15), Value = 60m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow },
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = employeeAssignmentId, ProgressDate = new DateOnly(f.Year - 1, 9, 20), Value = 40m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        var row = Assert.Single(result.Departments, d => d.DepartmentId == dept.Id);
        Assert.Equal(100m, row.PreviousYearActual);
        _ = deptPrevYearId;
    }

    [Fact]
    public async Task GetAllocationAsync_PreviousYearActual_NonValidIgnored()
    {
        var f = await CreateAsync();
        var dept = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId);

        var (prevYearPeriodId, _) = await SeedPreviousYearPeriodsAsync(f, f.Year - 1);
        var (_, employeeAssignmentId) = await SeedDeptPreviousYearActualTreeAsync(
            f, dept.Id, f.NonRatioKpiId, prevYearPeriodId, 800m);

        f.Db.ActualProgresses.AddRange(
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = employeeAssignmentId, ProgressDate = new DateOnly(f.Year - 1, 3, 15), Value = 60m, Status = ActualProgressStatus.Valid, UpdatedAt = DateTimeOffset.UtcNow },
            new ActualProgress { Id = Guid.NewGuid(), TargetAssignmentId = employeeAssignmentId, ProgressDate = new DateOnly(f.Year - 1, 9, 20), Value = 999m, Status = ActualProgressStatus.Invalid, UpdatedAt = DateTimeOffset.UtcNow });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        var row = Assert.Single(result.Departments, d => d.DepartmentId == dept.Id);
        Assert.Equal(60m, row.PreviousYearActual);
    }

    [Fact]
    public async Task GetAllocationAsync_PreviousYearActual_NoActuals_ReturnsNull()
    {
        var f = await CreateAsync();
        var dept = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId);

        var (prevYearPeriodId, _) = await SeedPreviousYearPeriodsAsync(f, f.Year - 1);
        await SeedDeptPreviousYearActualTreeAsync(f, dept.Id, f.NonRatioKpiId, prevYearPeriodId, 800m);

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        var row = Assert.Single(result.Departments, d => d.DepartmentId == dept.Id);
        Assert.Null(row.PreviousYearActual);
    }

    [Fact]
    public async Task GetAllocationAsync_PreviousYearActual_NoPreviousYearRow_ReturnsNull()
    {
        var f = await CreateAsync();
        var dept = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId);

        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, f.UserId, CancellationToken.None);

        var row = Assert.Single(result.Departments, d => d.DepartmentId == dept.Id);
        Assert.Null(row.PreviousYearActual);
    }

    [Fact]
    public async Task SaveAllocation_CreatesDeptYearAndQuarterRows_Matched()
    {
        var f = await CreateAsync();

        // Create department with employee
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        var jobPos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow
        };

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = jobPos.Id,
            Code = "PROFILE-001",
            Name = "Profile 1",
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(f.Year, 1, 1),
            EffectiveTo = null
        };

        var profileItem = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            WeightPercent = 100m,
            DisplayOrder = 1
        };

        profile.EvaluationProfileItems.Add(profileItem);

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "Employee 1",
            Email = "emp1@test.com",
            DepartmentId = dept.Id,
            JobPositionId = jobPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        f.Db.Departments.Add(dept);
        f.Db.JobPositions.Add(jobPos);
        f.Db.EvaluationProfiles.Add(profile);
        f.Db.Employees.Add(emp);
        await f.Db.SaveChangesAsync();

        // Save allocation
        var request = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 1000m, 250m, 250m, 250m, 250m)
        });

        var result = await f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Departments);
        Assert.Equal(1000m, result.Departments[0].Year);
        Assert.Equal(250m, result.Departments[0].Q1);
        Assert.Equal(250m, result.Departments[0].Q2);
        Assert.Equal(250m, result.Departments[0].Q3);
        Assert.Equal(250m, result.Departments[0].Q4);
        Assert.True(result.Departments[0].Matched);
        Assert.Equal(0m, result.Remaining.Year);

        // Dept-year row's EffectiveFrom must be the branch-year period's own start date.
        var deptYearRow = await f.Db.TargetAssignments.SingleAsync(
            t => t.DepartmentId == dept.Id && t.EvaluationPeriodId == f.YearPeriodId);
        Assert.Equal(new DateOnly(f.Year, 1, 1), deptYearRow.EffectiveFrom);

        // Each dept-quarter row's EffectiveFrom must be that quarter period's own start date.
        var deptQuarterRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == deptYearRow.Id && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, deptQuarterRows.Count);
        foreach (var deptQuarterRow in deptQuarterRows)
        {
            var quarterPeriod = await f.Db.EvaluationPeriods.SingleAsync(p => p.Id == deptQuarterRow.EvaluationPeriodId);
            Assert.Equal(quarterPeriod.StartDate, deptQuarterRow.EffectiveFrom);
        }
    }

    [Fact]
    public async Task SaveAllocation_UpdatesExisting()
    {
        var f = await CreateAsync();

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        var jobPos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow
        };

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = jobPos.Id,
            Code = "PROFILE-001",
            Name = "Profile 1",
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(f.Year, 1, 1),
            EffectiveTo = null
        };

        var profileItem = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            WeightPercent = 100m,
            DisplayOrder = 1
        };

        profile.EvaluationProfileItems.Add(profileItem);

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "Employee 1",
            Email = "emp1@test.com",
            DepartmentId = dept.Id,
            JobPositionId = jobPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        f.Db.Departments.Add(dept);
        f.Db.JobPositions.Add(jobPos);
        f.Db.EvaluationProfiles.Add(profile);
        f.Db.Employees.Add(emp);
        await f.Db.SaveChangesAsync();

        // First save (the department year must equal the branch year)
        var request1 = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 1000m, 400m, 300m, 200m, 100m)
        });

        await f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request1, f.UserId, CancellationToken.None);
        var rows1 = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId && t.SplitDimension == TargetSplitDimension.Org)
            .ToListAsync();
        Assert.Single(rows1);

        // Second save with a different quarter distribution (year is unchanged)
        var request2 = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 1000m, 100m, 200m, 300m, 400m)
        });

        await f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request2, f.UserId, CancellationToken.None);
        var rows2 = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId && t.SplitDimension == TargetSplitDimension.Org)
            .ToListAsync();

        // Should still have only 1 dept-year row (updated, not duplicated)
        Assert.Single(rows2);
        Assert.Equal(1000m, rows2[0].TargetValue);

        var quarterRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == rows2[0].Id && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, quarterRows.Count);
        Assert.Equal(400m, quarterRows.Single(r => r.EvaluationPeriodId == f.QuarterPeriodIds[3]).TargetValue);
    }

    [Fact]
    public async Task SaveAllocation_DepartmentYearSumMismatch_Throws422()
    {
        var f = await CreateAsync();

        var dept = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId);

        // The department year (600) does not fill the branch year (1000).
        var request = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 600m, 150m, 150m, 150m, 150m)
        });

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None));

        Assert.Equal(ErrorCodes.TargetAllocationSumMismatch, exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
        Assert.NotNull(exception.Errors);
    }

    [Fact]
    public async Task SaveAllocation_DepartmentQuarterSumMismatch_Throws422()
    {
        var f = await CreateAsync();

        var dept = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId);

        // The year sums to the branch year, but the department quarters do not add up to its own year.
        var request = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 1000m, 150m, 150m, 150m, 100m)
        });

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None));

        Assert.Equal(ErrorCodes.TargetDeptQuarterSumMismatch, exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task SaveAllocation_RemovesDraftRowsForDepartmentsNoLongerAllocated()
    {
        var f = await CreateAsync();

        var dept1 = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId, "DEPT-A");
        var dept2 = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId, "DEPT-B");

        // A previous save allocated the whole branch year to dept2.
        var staleDeptYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = dept2.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            SplitDimension = TargetSplitDimension.Org
        };
        f.Db.TargetAssignments.Add(staleDeptYear);
        await f.Db.SaveChangesAsync();

        // The new save gives the whole year to dept1, so the stale dept2 row must be dropped.
        var request = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept1.Id, 1000m, 250m, 250m, 250m, 250m)
        });

        await f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None);

        var deptYearRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId && t.SplitDimension == TargetSplitDimension.Org)
            .ToListAsync();
        Assert.Single(deptYearRows);
        Assert.Equal(dept1.Id, deptYearRows[0].DepartmentId);
        Assert.False(await f.Db.TargetAssignments.AnyAsync(t => t.Id == staleDeptYear.Id));
    }

    [Fact]
    public async Task SaveAllocation_QuartersNotReady_Throws409()
    {
        var f = await CreateAsync();

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        var jobPos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "TELLER",
            Title = "Teller",
            Status = RecordStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow
        };

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = jobPos.Id,
            Code = "PROFILE-001",
            Name = "Profile 1",
            Status = EvaluationProfileStatus.Active,
            EffectiveFrom = new DateOnly(f.Year, 1, 1),
            EffectiveTo = null
        };

        var profileItem = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profile.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            WeightPercent = 100m,
            DisplayOrder = 1
        };

        profile.EvaluationProfileItems.Add(profileItem);

        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E001",
            FullName = "Employee 1",
            Email = "emp1@test.com",
            DepartmentId = dept.Id,
            JobPositionId = jobPos.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        f.Db.Departments.Add(dept);
        f.Db.JobPositions.Add(jobPos);
        f.Db.EvaluationProfiles.Add(profile);
        f.Db.Employees.Add(emp);
        await f.Db.SaveChangesAsync();

        // Delete one quarter to simulate quarters not ready
        var quarterToDelete = f.Db.TargetAssignments
            .FirstOrDefault(t => t.ParentAssignmentId == f.BranchYearNonRatioId
                && t.SplitDimension == TargetSplitDimension.Period);

        if (quarterToDelete != null)
        {
            f.Db.TargetAssignments.Remove(quarterToDelete);
            await f.Db.SaveChangesAsync();
        }

        var request = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 1000m, 250m, 250m, 250m, 250m)
        });

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAllocation_IneligibleDepartment_Throws422()
    {
        var f = await CreateAsync();

        // Create department with no employees
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        f.Db.Departments.Add(dept);
        await f.Db.SaveChangesAsync();

        var request = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 1000m, 250m, 250m, 250m, 250m)
        });

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None));

        Assert.Equal(ErrorCodes.TargetDepartmentNotEligible, exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task SaveAllocation_NotDraft_Throws409()
    {
        var f = await CreateAsync();

        // Change branch-year to Active
        var branchYear = f.Db.TargetAssignments.First(t => t.Id == f.BranchYearNonRatioId);
        branchYear.Status = TargetAssignmentStatus.Active;
        await f.Db.SaveChangesAsync();

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department 1",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };

        f.Db.Departments.Add(dept);
        await f.Db.SaveChangesAsync();

        var request = new SaveKpiAllocationRequest(new List<SaveKpiAllocationDepartmentRequest>
        {
            new(dept.Id, 1000m, 250m, 250m, 250m, 250m)
        });

        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.SaveAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetSetupAsync_DeptQuartersDoNotMatchBranchQuarters_BlocksPublish()
    {
        var f = await CreateAsync();
        var dept = await AddEligibleDepartmentAsync(f, f.NonRatioKpiId);

        var deptYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = dept.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            SplitDimension = TargetSplitDimension.Org
        };
        f.Db.TargetAssignments.Add(deptYear);

        // Sums to the year (1000) but not to each branch quarter (250 each).
        var values = new[] { 400m, 300m, 200m, 100m };
        for (var q = 0; q < 4; q++)
        {
            f.Db.TargetAssignments.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = deptYear.Id,
                DepartmentId = dept.Id,
                KpiDefinitionId = f.NonRatioKpiId,
                EvaluationPeriodId = f.QuarterPeriodIds[q],
                TargetValue = values[q],
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            });
        }

        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);

        Assert.Equal("ALLOCATE", result.Step);
        Assert.False(result.CanPublish);
        Assert.Equal(0, result.Allocation.Done);
        Assert.Single(result.Blockers);
    }

    [Fact]
    public async Task GetAllocation_WithAnchorOfAnotherKpi_Works()
    {
        var f = await CreateAsync();

        // Create a second KPI
        var group = f.Db.KpiGroups.First();
        var kpi2 = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI_OTHER",
            Name = "Other KPI",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active
        };

        // Create branch-year for second KPI
        var branchYear2 = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            DepartmentId = f.BranchId,
            KpiDefinitionId = kpi2.Id,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 500m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        f.Db.KpiDefinitions.Add(kpi2);
        f.Db.TargetAssignments.Add(branchYear2);
        await f.Db.SaveChangesAsync();

        // Call GetAllocationAsync with branchYearId of KPI1 but kpiId of KPI2
        // This should find KPI2's branch-year row for the branch and year
        var result = await f.Service.GetAllocationAsync(f.BranchYearNonRatioId, kpi2.Id, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(kpi2.Id, result.KpiId);
        Assert.Equal(500m, result.Branch.Year);
    }

    // ====================== H7: GetSetupAsync tests ======================

    [Fact]
    public async Task GetSetupAsync_NoYear_ReturnsImportStep()
    {
        var f = await CreateAsync();

        // Non-existent year
        var result = await f.Service.GetSetupAsync(f.BranchId, 9999, f.UserId, CancellationToken.None);

        Assert.False(result.HasBranchYear);
        Assert.Equal("IMPORT", result.Step);
        Assert.Equal("NONE", result.Status);
        Assert.Equal(0, result.RowCount);
        Assert.Equal(0, result.Quarters.Total);
        Assert.Equal(0, result.Allocation.Total);
    }

    [Fact]
    public async Task GetSetupAsync_NoBranchYear_ReturnsImportStep()
    {
        var f = await CreateAsync();

        // Remove the branch-year rows to simulate no import yet
        var branchYearRows = await f.Db.TargetAssignments
            .Where(t => t.DepartmentId == f.BranchId
                && t.EvaluationPeriodId == f.YearPeriodId
                && t.ParentAssignmentId == null)
            .ToListAsync();

        foreach (var row in branchYearRows)
        {
            f.Db.TargetAssignments.Remove(row);
        }
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);

        Assert.False(result.HasBranchYear);
        Assert.Equal("IMPORT", result.Step);
        Assert.Equal("NONE", result.Status);
        Assert.Equal(0, result.RowCount);
    }

    [Fact]
    public async Task GetSetupAsync_BranchNotFound_Throws404()
    {
        var f = await CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetSetupAsync(Guid.NewGuid(), f.Year, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetSetupAsync_WrongBranchLevel_Throws404()
    {
        var f = await CreateAsync();

        // Create a department (not a branch)
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-001",
            Name = "Department",
            LevelType = DepartmentLevelType.Department,
            ParentDepartmentId = f.BranchId
        };
        f.Db.Departments.Add(dept);
        await f.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            f.Service.GetSetupAsync(dept.Id, f.Year, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetSetupAsync_OtherBranch_ThrowsForbidden()
    {
        var f = await CreateAsync();

        // Create another branch
        var otherBranch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BRANCH-002",
            Name = "Branch 002",
            LevelType = DepartmentLevelType.Branch
        };
        f.Db.Departments.Add(otherBranch);
        await f.Db.SaveChangesAsync();

        // Try to access with restricted permission
        var restrictedPermission = new AllocationPermissions(admin: false);
        var restrictedService = new TargetSetupService(new UnitOfWork(f.Db), f.Db, restrictedPermission, new NoOpTargetChangeLogService(), new RecordingNotifier(), new NoOpDepartmentService());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            restrictedService.GetSetupAsync(otherBranch.Id, f.Year, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetSetupAsync_WithBranchYear_ReturnsCorrectRowCount()
    {
        var f = await CreateAsync();

        var result = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);

        Assert.True(result.HasBranchYear);
        Assert.Equal(2, result.RowCount); // Ratio + Non-ratio KPI
    }

    [Fact]
    public async Task GetSetupAsync_QuartersSplit_ReturnsQuarterStep()
    {
        var f = await CreateAsync();

        // Quarters are already split in fixture (250 each = 1000 total)
        var result = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);

        Assert.True(result.HasBranchYear);
        Assert.Equal("ALLOCATE", result.Step); // Quarters done, but allocation not done
        Assert.Equal(1, result.Quarters.Done); // Non-ratio KPI has quarters done
        Assert.Equal(1, result.Quarters.Total); // 1 non-ratio KPI
    }

    [Fact]
    public async Task GetSetupAsync_AllDone_ReturnsPublishStep()
    {
        var f = await CreateAsync();

        // Quarters already matched in fixture
        // Now add complete allocation
        var departments = await f.Db.Departments
            .Where(d => d.ParentDepartmentId == f.BranchId && d.LevelType == DepartmentLevelType.Department)
            .ToListAsync();

        if (departments.Count == 0)
        {
            // Create a test department
            var dept = new Department
            {
                Id = Guid.NewGuid(),
                Code = "DEPT-TEST",
                Name = "Test Department",
                ParentDepartmentId = f.BranchId,
                LevelType = DepartmentLevelType.Department
            };
            f.Db.Departments.Add(dept);
            await f.Db.SaveChangesAsync();
            departments.Add(dept);
        }

        await AddEligibleEmployeeAsync(f.Db, departments[0].Id, f.NonRatioKpiId);

        // Create dept-year and dept-quarter rows for the non-ratio KPI
        var deptYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = departments[0].Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            SplitDimension = TargetSplitDimension.Org
        };
        f.Db.TargetAssignments.Add(deptYear);
        await f.Db.SaveChangesAsync();

        // Create dept-quarter rows (250 each for each quarter)
        var deptQuarters = new List<TargetAssignment>();
        for (int q = 0; q < 4; q++)
        {
            deptQuarters.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = deptYear.Id,
                DepartmentId = departments[0].Id,
                KpiDefinitionId = f.NonRatioKpiId,
                EvaluationPeriodId = f.QuarterPeriodIds[q],
                TargetValue = 250m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            });
        }
        f.Db.TargetAssignments.AddRange(deptQuarters);
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);

        Assert.True(result.HasBranchYear);
        Assert.Equal("PUBLISH", result.Step);
        Assert.True(result.CanPublish);
        Assert.Equal(1, result.Quarters.Done);
        Assert.Equal(1, result.Allocation.Done);
    }

    [Fact]
    public async Task GetSetupAsync_AllActiveAndPublished_ReturnsPublishedStep()
    {
        var f = await CreateAsync();

        // Set all rows to Active
        var allRows = await f.Db.TargetAssignments
            .Where(t => t.DepartmentId == f.BranchId && t.EvaluationPeriodId == f.YearPeriodId)
            .ToListAsync();

        foreach (var row in allRows)
        {
            row.Status = TargetAssignmentStatus.Active;
            row.ApprovedAt = DateTimeOffset.UtcNow;
        }
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetSetupAsync(f.BranchId, f.Year, f.UserId, CancellationToken.None);

        Assert.True(result.HasBranchYear);
        Assert.Equal("PUBLISHED", result.Step);
        Assert.Equal("ACTIVE", result.Status);
        Assert.False(result.CanPublish); // Already published
    }

    // ====================== H8: PublishAsync tests ======================

    [Fact]
    public async Task PublishAsync_Complete_SetsAllToActiveAndCreatesAuditLog()
    {
        var f = await CreateAsync();

        // First set up complete allocations
        var departments = await f.Db.Departments
            .Where(d => d.ParentDepartmentId == f.BranchId && d.LevelType == DepartmentLevelType.Department)
            .ToListAsync();

        if (departments.Count == 0)
        {
            var dept = new Department
            {
                Id = Guid.NewGuid(),
                Code = "DEPT-PUB",
                Name = "Publish Test",
                ParentDepartmentId = f.BranchId,
                LevelType = DepartmentLevelType.Department
            };
            f.Db.Departments.Add(dept);
            await f.Db.SaveChangesAsync();
            departments.Add(dept);
        }

        await AddEligibleEmployeeAsync(f.Db, departments[0].Id, f.NonRatioKpiId);

        // Create complete allocation for non-ratio KPI
        var deptYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = departments[0].Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 1000m,
            Version = 1,
            Status = TargetAssignmentStatus.Draft,
            SplitDimension = TargetSplitDimension.Org
        };
        f.Db.TargetAssignments.Add(deptYear);
        await f.Db.SaveChangesAsync();

        for (int q = 0; q < 4; q++)
        {
            f.Db.TargetAssignments.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = deptYear.Id,
                DepartmentId = departments[0].Id,
                KpiDefinitionId = f.NonRatioKpiId,
                EvaluationPeriodId = f.QuarterPeriodIds[q],
                TargetValue = 250m,
                Version = 1,
                Status = TargetAssignmentStatus.Draft,
                SplitDimension = TargetSplitDimension.Period
            });
        }
        await f.Db.SaveChangesAsync();

        var result = await f.Service.PublishAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.PublishedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(result.BranchYearCount > 0);
        Assert.True(result.QuarterCount > 0);
        Assert.True(result.DepartmentYearCount > 0);
        Assert.True(result.DepartmentQuarterCount > 0);

        // Verify all rows are now Active
        var allRows = await f.Db.TargetAssignments
            .Where(t => t.DepartmentId == f.BranchId && t.EvaluationPeriodId == f.YearPeriodId)
            .ToListAsync();

        Assert.All(allRows, r => Assert.Equal(TargetAssignmentStatus.Active, r.Status));
        Assert.All(allRows, r => Assert.NotNull(r.ApprovedAt));
    }

    [Fact]
    public async Task PublishAsync_AlreadyPublished_Throws409()
    {
        var f = await CreateAsync();

        // Set all rows to Active (already published)
        var allRows = await f.Db.TargetAssignments
            .Where(t => t.DepartmentId == f.BranchId && t.EvaluationPeriodId == f.YearPeriodId)
            .ToListAsync();

        foreach (var row in allRows)
        {
            row.Status = TargetAssignmentStatus.Active;
            row.ApprovedAt = DateTimeOffset.UtcNow;
        }
        await f.Db.SaveChangesAsync();

        // Try to publish again
        await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.PublishAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_Incomplete_Throws422()
    {
        var f = await CreateAsync();

        // Don't set up allocations - quarters are done but allocation is not
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.PublishAsync(f.BranchYearNonRatioId, f.UserId, CancellationToken.None));
    }

    /// <summary>A department directly under the fixture branch with one eligible employee for <paramref name="kpiId"/>.</summary>
    /// <summary>
    /// Creates a previous-year ACTIVE department (ORG split) row for <paramref name="departmentId"/> + <paramref name="kpiId"/>,
    /// plus one descendant (e.g. employee-level) child under it so VALID ActualProgress rows can be attached there.
    /// Returns (departmentPreviousYearRowId, descendantAssignmentId).
    /// </summary>
    private static async Task<(Guid DeptPreviousYearRowId, Guid DescendantAssignmentId)> SeedDeptPreviousYearActualTreeAsync(
        Fixture f, Guid departmentId, Guid kpiId, Guid prevYearPeriodId, decimal yearValue)
    {
        var deptPrevYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = Guid.NewGuid(), // previous-year branch-year row; not otherwise referenced by these tests
            DepartmentId = departmentId,
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = prevYearPeriodId,
            TargetValue = yearValue,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            SplitDimension = TargetSplitDimension.Org
        };

        var descendant = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = deptPrevYear.Id,
            DepartmentId = departmentId,
            KpiDefinitionId = kpiId,
            EvaluationPeriodId = prevYearPeriodId,
            TargetValue = yearValue,
            Version = 1,
            Status = TargetAssignmentStatus.Active,
            SplitDimension = TargetSplitDimension.Period
        };

        f.Db.TargetAssignments.AddRange(deptPrevYear, descendant);
        await f.Db.SaveChangesAsync();

        return (deptPrevYear.Id, descendant.Id);
    }

    private static async Task<Department> AddEligibleDepartmentAsync(Fixture f, Guid kpiId, string code = "DEPT-001")
    {
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Department {code}",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };
        f.Db.Departments.Add(department);
        await f.Db.SaveChangesAsync();
        await AddEligibleEmployeeAsync(f.Db, department.Id, kpiId);
        return department;
    }

    /// <summary>An ACTIVE employee in <paramref name="departmentId"/> whose job position (no evaluation profile)
    /// is eligible for <paramref name="kpiId"/> through its work type, so the department counts as headcount 1.</summary>
    private static async Task AddEligibleEmployeeAsync(AppDbContext db, Guid departmentId, Guid kpiId)
    {
        var now = DateTimeOffset.UtcNow;
        var position = new JobPosition { Id = Guid.NewGuid(), Code = $"JP-{Guid.NewGuid():N}", Title = "Teller", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var workType = new WorkType { Id = Guid.NewGuid(), Code = $"WT-{Guid.NewGuid():N}", Name = "Teller work", Status = RecordStatus.Active, UpdatedAt = now };
        db.AddRange(position, workType);
        db.Add(new JobPositionWorkType { Id = Guid.NewGuid(), JobPositionId = position.Id, WorkTypeId = workType.Id, UpdatedAt = now });
        db.Add(new WorkTypeKpi { Id = Guid.NewGuid(), WorkTypeId = workType.Id, KpiDefinitionId = kpiId, EffectiveFrom = new DateOnly(2000, 1, 1), UpdatedAt = now });
        db.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = $"E-{Guid.NewGuid():N}",
            FullName = "Teller",
            Email = $"{Guid.NewGuid():N}@test.local",
            DepartmentId = departmentId,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    // =========================================================================
    // Tier 3: employee allocation (docs/api/TARGET_ALLOCATION_CONTRACT.md, Phase 3)
    // =========================================================================

    /// <summary>An ACTIVE department-year KPI row (as if tier 2 had already been published) plus its 4 ACTIVE
    /// quarter children, under the fixture's non-ratio branch-year KPI row.</summary>
    private static async Task<TargetAssignment> CreateActiveDeptYearAsync(
        Fixture f, Guid departmentId, decimal year, decimal q1, decimal q2, decimal q3, decimal q4)
    {
        var now = DateTimeOffset.UtcNow;
        var deptYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = departmentId,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = year,
            SplitDimension = TargetSplitDimension.Org,
            Status = TargetAssignmentStatus.Active,
            Version = 1,
            CreatedAt = now,
            ApprovedAt = now,
            EffectiveFrom = new DateOnly(f.Year, 1, 1)
        };
        f.Db.TargetAssignments.Add(deptYear);

        var quarterValues = new[] { q1, q2, q3, q4 };
        for (var i = 0; i < 4; i++)
        {
            f.Db.TargetAssignments.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                ParentAssignmentId = deptYear.Id,
                DepartmentId = departmentId,
                KpiDefinitionId = f.NonRatioKpiId,
                EvaluationPeriodId = f.QuarterPeriodIds[i],
                TargetValue = quarterValues[i],
                SplitDimension = TargetSplitDimension.Period,
                Status = TargetAssignmentStatus.Active,
                Version = 1,
                CreatedAt = now,
                ApprovedAt = now,
                EffectiveFrom = new DateOnly(f.Year, 1, 1)
            });
        }

        await f.Db.SaveChangesAsync();
        return deptYear;
    }

    private static async Task<Department> CreateDepartmentAsync(Fixture f, string code)
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Department {code}",
            ParentDepartmentId = f.BranchId,
            LevelType = DepartmentLevelType.Department
        };
        f.Db.Departments.Add(dept);
        await f.Db.SaveChangesAsync();
        return dept;
    }

    [Fact]
    public async Task GetEmployeeAllocationAsync_ReturnsEligibleAndLockedEmployees()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E1");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);
        await AddEligibleEmployeeAsync(f.Db, dept.Id, f.NonRatioKpiId);

        // An employee with no profile/work-type link for this KPI: locked, not in the editable grid.
        var now = DateTimeOffset.UtcNow;
        var otherPosition = new JobPosition { Id = Guid.NewGuid(), Code = "JP-OTHER", Title = "Other", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        f.Db.JobPositions.Add(otherPosition);
        f.Db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E-OTHER",
            FullName = "Not Responsible",
            Email = "not-responsible@test.local",
            DepartmentId = dept.Id,
            JobPositionId = otherPosition.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now
        });
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetEmployeeAllocationAsync(deptYear.Id, f.UserId, CancellationToken.None);

        Assert.Equal(dept.Id, result.DepartmentId);
        Assert.Equal(400m, result.Department.Year);
        Assert.Single(result.Employees);
        Assert.Single(result.LockedEmployees);
        Assert.Equal("Not Responsible", result.LockedEmployees[0].EmployeeName);
    }

    [Fact]
    public async Task GetEmployeeAllocationAsync_TierNotPublished_Throws409()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E2");

        // Dept-year row is DRAFT: tier 2 has not been published yet.
        var deptYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = dept.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 400m,
            SplitDimension = TargetSplitDimension.Org,
            Status = TargetAssignmentStatus.Draft,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = new DateOnly(f.Year, 1, 1)
        };
        f.Db.TargetAssignments.Add(deptYear);
        await f.Db.SaveChangesAsync();

        // GetEmployeeAllocationAsync itself does not require ACTIVE (viewing is fine); only Save/Publish do.
        var result = await f.Service.GetEmployeeAllocationAsync(deptYear.Id, f.UserId, CancellationToken.None);
        Assert.Equal("DRAFT", result.Status);

        var request = new SaveEmployeeAllocationRequest(new List<SaveEmployeeAllocationEmployeeRequest>());
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => f.Service.SaveEmployeeAllocationAsync(deptYear.Id, request, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetTierNotPublished, ex.ErrorCode);
    }

    [Fact]
    public async Task SaveEmployeeAllocationAsync_CreatesRows_Matched()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E3");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);
        await AddEligibleEmployeeAsync(f.Db, dept.Id, f.NonRatioKpiId);
        var employee = await f.Db.Employees.SingleAsync(e => e.DepartmentId == dept.Id);

        var request = new SaveEmployeeAllocationRequest(new List<SaveEmployeeAllocationEmployeeRequest>
        {
            new(employee.Id, 400m, 100m, 100m, 100m, 100m)
        });

        var result = await f.Service.SaveEmployeeAllocationAsync(deptYear.Id, request, f.UserId, CancellationToken.None);

        Assert.Single(result.Employees);
        Assert.Equal(400m, result.Employees[0].Year);
        Assert.True(result.Employees[0].Matched);
        Assert.Equal(0m, result.Remaining.Year);

        var employeeYearRow = await f.Db.TargetAssignments.SingleAsync(
            t => t.EmployeeId == employee.Id && t.SplitDimension == TargetSplitDimension.Org);
        Assert.Equal(TargetAssignmentStatus.Draft, employeeYearRow.Status);
        var employeeQuarterRows = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == employeeYearRow.Id && t.SplitDimension == TargetSplitDimension.Period)
            .ToListAsync();
        Assert.Equal(4, employeeQuarterRows.Count);
    }

    [Fact]
    public async Task SaveEmployeeAllocationAsync_SumMismatch_Throws422()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E4");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);
        await AddEligibleEmployeeAsync(f.Db, dept.Id, f.NonRatioKpiId);
        var employee = await f.Db.Employees.SingleAsync(e => e.DepartmentId == dept.Id);

        var request = new SaveEmployeeAllocationRequest(new List<SaveEmployeeAllocationEmployeeRequest>
        {
            new(employee.Id, 300m, 75m, 75m, 75m, 75m)
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.SaveEmployeeAllocationAsync(deptYear.Id, request, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetEmployeeAllocationSumMismatch, ex.ErrorCode);
    }

    [Fact]
    public async Task SaveEmployeeAllocationAsync_IneligibleEmployee_Throws422()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E5");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);

        var now = DateTimeOffset.UtcNow;
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "JP-E5", Title = "Other", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        f.Db.JobPositions.Add(position);
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E-E5",
            FullName = "Not Responsible",
            Email = "e5@test.local",
            DepartmentId = dept.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now
        };
        f.Db.Employees.Add(employee);
        await f.Db.SaveChangesAsync();

        var request = new SaveEmployeeAllocationRequest(new List<SaveEmployeeAllocationEmployeeRequest>
        {
            new(employee.Id, 400m, 100m, 100m, 100m, 100m)
        });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.SaveEmployeeAllocationAsync(deptYear.Id, request, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetEmployeeNotEligible, ex.ErrorCode);
    }

    [Fact]
    public async Task PublishEmployeesAsync_Success_SetsActive()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E6");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);
        await AddEligibleEmployeeAsync(f.Db, dept.Id, f.NonRatioKpiId);
        var employee = await f.Db.Employees.SingleAsync(e => e.DepartmentId == dept.Id);

        var request = new SaveEmployeeAllocationRequest(new List<SaveEmployeeAllocationEmployeeRequest>
        {
            new(employee.Id, 400m, 100m, 100m, 100m, 100m)
        });
        await f.Service.SaveEmployeeAllocationAsync(deptYear.Id, request, f.UserId, CancellationToken.None);

        var result = await f.Service.PublishEmployeesAsync(deptYear.Id, f.UserId, CancellationToken.None);

        Assert.Equal(1, result.EmployeeYearCount);
        Assert.Equal(4, result.EmployeeQuarterCount);

        var employeeYearRow = await f.Db.TargetAssignments.SingleAsync(
            t => t.EmployeeId == employee.Id && t.SplitDimension == TargetSplitDimension.Org);
        Assert.Equal(TargetAssignmentStatus.Active, employeeYearRow.Status);
    }

    [Fact]
    public async Task PublishEmployeesAsync_Blocked_WhenIncomplete_Throws422()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E7");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);
        // Eligible employee exists, but no employee allocation was ever saved.
        await AddEligibleEmployeeAsync(f.Db, dept.Id, f.NonRatioKpiId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.PublishEmployeesAsync(deptYear.Id, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetEmployeePublishBlocked, ex.ErrorCode);
    }

    [Fact]
    public async Task PublishEmployeesAsync_TierNotPublished_Throws409()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-E8");
        var deptYear = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = f.BranchYearNonRatioId,
            DepartmentId = dept.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 400m,
            SplitDimension = TargetSplitDimension.Org,
            Status = TargetAssignmentStatus.Draft,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = new DateOnly(f.Year, 1, 1)
        };
        f.Db.TargetAssignments.Add(deptYear);
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => f.Service.PublishEmployeesAsync(deptYear.Id, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetTierNotPublished, ex.ErrorCode);
    }

    // =========================================================================
    // Mid-term adjustment (Target Allocation v3, D12/D13)
    // =========================================================================

    private static async Task MakeBranchYearActiveAsync(Fixture f)
    {
        var rows = await f.Db.TargetAssignments
            .Where(t => (t.Id == f.BranchYearNonRatioId)
                || (t.ParentAssignmentId == f.BranchYearNonRatioId && t.SplitDimension == TargetSplitDimension.Period))
            .ToListAsync();
        foreach (var row in rows)
        {
            row.Status = TargetAssignmentStatus.Active;
            row.ApprovedAt = DateTimeOffset.UtcNow;
        }

        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdjustQuartersAsync_BranchYearNotActive_Throws409()
    {
        var f = await CreateAsync();
        var request = new AdjustQuarterSplitRequest(
            [new SaveQuarterSplitRowRequest(f.NonRatioKpiId, 300m, 300m, 200m, 200m)], "Điều chỉnh test");

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => f.Service.AdjustQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task AdjustQuartersAsync_MissingChangeReason_ThrowsValidation()
    {
        var f = await CreateAsync();
        await MakeBranchYearActiveAsync(f);
        var request = new AdjustQuarterSplitRequest([new SaveQuarterSplitRowRequest(f.NonRatioKpiId, 300m, 300m, 200m, 200m)], "   ");

        await Assert.ThrowsAsync<ValidationException>(
            () => f.Service.AdjustQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task AdjustQuartersAsync_AfterPublish_VersionsOnlyChangedQuarterAndWritesChangeLog()
    {
        var f = await CreateAsync();
        await MakeBranchYearActiveAsync(f);

        // Original split is 250/250/250/250 = 1000 (fixture default). Move 50 from Q1 to Q2.
        var request = new AdjustQuarterSplitRequest(
            [new SaveQuarterSplitRowRequest(f.NonRatioKpiId, 200m, 300m, 250m, 250m)], "Điều chỉnh giữa kỳ Q1->Q2");

        var result = await f.Service.AdjustQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None);

        var row = result.Rows.Single(r => r.KpiId == f.NonRatioKpiId);
        Assert.Equal(200m, row.Q1);
        Assert.Equal(300m, row.Q2);
        Assert.Equal(250m, row.Q3);
        Assert.Equal(250m, row.Q4);

        // Only Q1/Q2 (the ones that changed) get a new ACTIVE version; Q3/Q4 keep their original row untouched.
        var activeQuarters = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId && t.SplitDimension == TargetSplitDimension.Period
                && t.Status == TargetAssignmentStatus.Active)
            .ToListAsync();
        Assert.Equal(4, activeQuarters.Count);
        Assert.Contains(activeQuarters, q => q.TargetValue == 200m && q.Version == 2 && q.PreviousVersionId != null);
        Assert.Contains(activeQuarters, q => q.TargetValue == 300m && q.Version == 2 && q.PreviousVersionId != null);
        Assert.Equal(2, activeQuarters.Count(q => q.TargetValue == 250m && q.Version == 1));

        var supersededQuarters = await f.Db.TargetAssignments
            .Where(t => t.ParentAssignmentId == f.BranchYearNonRatioId && t.SplitDimension == TargetSplitDimension.Period
                && t.Status == TargetAssignmentStatus.Superseded)
            .ToListAsync();
        Assert.Equal(2, supersededQuarters.Count);
    }

    [Fact]
    public async Task AdjustQuartersAsync_SumMismatch_Throws422()
    {
        var f = await CreateAsync();
        await MakeBranchYearActiveAsync(f);
        var request = new AdjustQuarterSplitRequest(
            [new SaveQuarterSplitRowRequest(f.NonRatioKpiId, 300m, 300m, 300m, 300m)], "Sai tổng");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.AdjustQuartersAsync(f.BranchYearNonRatioId, request, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetQuarterSumMismatch, ex.ErrorCode);
    }

    [Fact]
    public async Task AdjustAllocationAsync_PartialDepartmentSet_ThrowsValidation()
    {
        var f = await CreateAsync();
        await MakeBranchYearActiveAsync(f);
        var deptA = await CreateDepartmentAsync(f, "DEPT-ADJ-A");
        var deptB = await CreateDepartmentAsync(f, "DEPT-ADJ-B");
        await CreateActiveDeptYearAsync(f, deptA.Id, 600m, 150m, 150m, 150m, 150m);
        await CreateActiveDeptYearAsync(f, deptB.Id, 400m, 100m, 100m, 100m, 100m);

        // Omits deptB: must be rejected rather than silently dropping a department.
        var request = new AdjustKpiAllocationRequest(
            [new SaveKpiAllocationDepartmentRequest(deptA.Id, 600m, 150m, 150m, 150m, 150m)], "Thiếu phòng");

        await Assert.ThrowsAsync<ValidationException>(
            () => f.Service.AdjustAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task AdjustAllocationAsync_AfterPublish_VersionsChangedDepartmentAndReopensTier3Mismatch()
    {
        var f = await CreateAsync();
        // Wire a real (not no-op) change-log so this test can verify the ADJUST entry it writes.
        var service = new TargetSetupService(
            new UnitOfWork(f.Db), f.Db, new AllocationPermissions(admin: true), new TargetChangeLogService(f.Db, TimeProvider.System),
            new RecordingNotifier(), new NoOpDepartmentService());
        f = f with { Service = service };
        await MakeBranchYearActiveAsync(f);
        var deptA = await CreateDepartmentAsync(f, "DEPT-ADJ2-A");
        var deptB = await CreateDepartmentAsync(f, "DEPT-ADJ2-B");
        await AddEligibleEmployeeAsync(f.Db, deptA.Id, f.NonRatioKpiId);
        await AddEligibleEmployeeAsync(f.Db, deptB.Id, f.NonRatioKpiId);
        var deptYearA = await CreateActiveDeptYearAsync(f, deptA.Id, 600m, 150m, 150m, 150m, 150m);
        await CreateActiveDeptYearAsync(f, deptB.Id, 400m, 100m, 100m, 100m, 100m);

        // Tier 3 already published for deptA at the old (600) year value.
        var empA = await f.Db.Employees.FirstAsync(e => e.DepartmentId == deptA.Id);
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = deptYearA.Id,
            EmployeeId = empA.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 600m,
            SplitDimension = TargetSplitDimension.Org,
            Status = TargetAssignmentStatus.Active,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            ApprovedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = new DateOnly(f.Year, 1, 1)
        });
        await f.Db.SaveChangesAsync();

        // BM moves 100 from deptA to deptB: 600->500, 400->500 (branch total stays 1000).
        var request = new AdjustKpiAllocationRequest(
            [
                new SaveKpiAllocationDepartmentRequest(deptA.Id, 500m, 125m, 125m, 125m, 125m),
                new SaveKpiAllocationDepartmentRequest(deptB.Id, 500m, 125m, 125m, 125m, 125m),
            ],
            "BM điều chỉnh lại giữa các phòng");

        var result = await f.Service.AdjustAllocationAsync(f.BranchYearNonRatioId, f.NonRatioKpiId, request, f.UserId, CancellationToken.None);

        Assert.Equal(500m, result.Departments.Single(d => d.DepartmentId == deptA.Id).Year);
        Assert.Equal(500m, result.Departments.Single(d => d.DepartmentId == deptB.Id).Year);

        // deptA's dept-year row is versioned; its old ACTIVE row is now SUPERSEDED.
        var deptAYearRows = await f.Db.TargetAssignments
            .Where(t => t.DepartmentId == deptA.Id && t.KpiDefinitionId == f.NonRatioKpiId && t.SplitDimension == TargetSplitDimension.Org)
            .ToListAsync();
        Assert.Contains(deptAYearRows, r => r.Status == TargetAssignmentStatus.Superseded && r.TargetValue == 600m);
        Assert.Contains(deptAYearRows, r => r.Status == TargetAssignmentStatus.Active && r.TargetValue == 500m && r.Version == 2);

        // An ADJUST change-log entry was written for deptA's year change.
        var logs = await f.Db.TargetChangeLogs
            .Where(l => l.DepartmentId == deptA.Id && l.KpiDefinitionId == f.NonRatioKpiId && l.Action == TargetChangeLogAction.Adjust)
            .ToListAsync();
        Assert.Contains(logs, l => l.Period == "YEAR" && l.OldValue == 600m && l.NewValue == 500m);

        // Tier 3 for deptA is now mismatched: the employee still totals 600 against the new 500 department year.
        var employeeAllocation = await f.Service.GetEmployeeAllocationAsync(
            (await f.Db.TargetAssignments.Where(t => t.DepartmentId == deptA.Id && t.Status == TargetAssignmentStatus.Active
                && t.SplitDimension == TargetSplitDimension.Org).Select(t => t.Id).FirstAsync()),
            f.UserId, CancellationToken.None);
        Assert.False(employeeAllocation.Employees.Single().Matched);
        Assert.Equal(-100m, employeeAllocation.Remaining.Year);
    }

    [Fact]
    public async Task AdjustEmployeeAllocationAsync_NotYetPublished_Throws409()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-ADJ3");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);
        await AddEligibleEmployeeAsync(f.Db, dept.Id, f.NonRatioKpiId);
        var emp = await f.Db.Employees.FirstAsync(e => e.DepartmentId == dept.Id);

        var request = new AdjustEmployeeAllocationRequest(
            [new SaveEmployeeAllocationEmployeeRequest(emp.Id, 400m, 100m, 100m, 100m, 100m)], "Chưa phát hành");

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => f.Service.AdjustEmployeeAllocationAsync(deptYear.Id, request, f.UserId, CancellationToken.None));
        Assert.Equal(ErrorCodes.TargetInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task AdjustEmployeeAllocationAsync_AfterPublish_VersionsAndFlipsAckToReconfirm()
    {
        var f = await CreateAsync();
        var dept = await CreateDepartmentAsync(f, "DEPT-ADJ4");
        var deptYear = await CreateActiveDeptYearAsync(f, dept.Id, 400m, 100m, 100m, 100m, 100m);
        await AddEligibleEmployeeAsync(f.Db, dept.Id, f.NonRatioKpiId);
        var emp = await f.Db.Employees.FirstAsync(e => e.DepartmentId == dept.Id);

        var now = DateTimeOffset.UtcNow;
        f.Db.TargetAssignments.Add(new TargetAssignment
        {
            Id = Guid.NewGuid(),
            ParentAssignmentId = deptYear.Id,
            EmployeeId = emp.Id,
            KpiDefinitionId = f.NonRatioKpiId,
            EvaluationPeriodId = f.YearPeriodId,
            TargetValue = 400m,
            SplitDimension = TargetSplitDimension.Org,
            Status = TargetAssignmentStatus.Active,
            Version = 1,
            CreatedAt = now,
            ApprovedAt = now,
            EffectiveFrom = new DateOnly(f.Year, 1, 1)
        });
        f.Db.EmployeeTargetAcks.Add(new EmployeeTargetAck
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp.Id,
            Year = f.Year,
            Status = EmployeeTargetAckStatus.Confirmed,
            ConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await f.Db.SaveChangesAsync();

        var request = new AdjustEmployeeAllocationRequest(
            [new SaveEmployeeAllocationEmployeeRequest(emp.Id, 450m, 112.5m, 112.5m, 112.5m, 112.5m)], "DM điều chỉnh tăng chỉ tiêu");

        // DM adjust is scoped by department year; department year itself is unaffected by this call, only
        // the employee row - but the dept year must already be Active (it is, from CreateActiveDeptYearAsync).
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            f.Service.AdjustEmployeeAllocationAsync(deptYear.Id, request, f.UserId, CancellationToken.None));

        // The above intentionally mismatches department total (400) vs the new employee total (450); fix the
        // request to equal the department year exactly and retry.
        var validRequest = new AdjustEmployeeAllocationRequest(
            [new SaveEmployeeAllocationEmployeeRequest(emp.Id, 500m, 125m, 125m, 125m, 125m)], "DM điều chỉnh tăng chỉ tiêu");
        deptYear.TargetValue = 500m;
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AdjustEmployeeAllocationAsync(deptYear.Id, validRequest, f.UserId, CancellationToken.None);

        Assert.Equal(500m, result.Employees.Single().Year);

        var empYearRows = await f.Db.TargetAssignments
            .Where(t => t.EmployeeId == emp.Id && t.KpiDefinitionId == f.NonRatioKpiId && t.SplitDimension == TargetSplitDimension.Org)
            .ToListAsync();
        Assert.Contains(empYearRows, r => r.Status == TargetAssignmentStatus.Superseded && r.TargetValue == 400m);
        Assert.Contains(empYearRows, r => r.Status == TargetAssignmentStatus.Active && r.TargetValue == 500m && r.Version == 2);

        var ack = await f.Db.EmployeeTargetAcks.SingleAsync(a => a.EmployeeId == emp.Id && a.Year == f.Year);
        Assert.Equal(EmployeeTargetAckStatus.Reconfirm, ack.Status);
        Assert.Null(ack.ConfirmedAt);
    }
}

/// <summary>
/// Mock permission service for testing.
/// </summary>
public sealed class AllocationPermissions(bool admin) : IPermissionService
{
    public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken = default)
    {
        // For admin = true, return true for target:manage:All
        if (admin && resource == "target" && action == "manage" && requiredScope == "All")
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult<UserPermissionsResponseDto?>(null);
    }

    public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<UserPermissionDto>>(new List<UserPermissionDto>());
    }

    public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>No-op test double for the tier-3 publish change-log hook (owned by MyTargetsService's test suite).</summary>
public sealed class NoOpTargetChangeLogService : ITargetChangeLogService
{
    public Task LogAsync(
        Guid actorId, string action, int tier, Guid kpiId, string unit, string period,
        decimal? oldValue, decimal? newValue, string? basis = null, Guid? employeeId = null,
        Guid? departmentId = null, int? year = null, decimal? roundingStep = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<TargetChangeLog>> GetByEmployeeYearAsync(
        Guid employeeId, int year, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TargetChangeLog>>(new List<TargetChangeLog>());
}

/// <summary>
/// No-op test double for TargetSetupService's manager-resolution dependency (mid-term adjustment/remind
/// notifications, D12/D13/W8). Returns a manager-less department so the caller's best-effort notification is
/// skipped; every other member is unused by TargetSetupService's existing test suite.
/// </summary>
public sealed class NoOpDepartmentService : IDepartmentService
{
    public Task<PagedResult<DepartmentSummaryDto>> GetDepartmentsAsync(
        Guid accountId, DepartmentFilterDto filter, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DepartmentDetailDto> GetDepartmentAsync(
        Guid accountId, Guid departmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DepartmentDetailDto(
            departmentId, "DEPT", "Department", null, "DEPARTMENT", "ACTIVE", null,
            [], null, 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    public Task<DepartmentOverviewDto> GetOverviewAsync(
        Guid accountId, Guid departmentId, Guid? periodId, string? compare = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DepartmentOverviewExportDto> ExportOverviewAsync(
        Guid accountId, Guid departmentId, Guid? periodId, string? compare = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<PagedResult<DepartmentMemberDto>> GetMembersAsync(
        Guid accountId, Guid departmentId, DepartmentMemberFilterDto filter, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<DepartmentTreeNodeDto>> GetTreeAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DepartmentDetailDto> CreateDepartmentAsync(CreateDepartmentDto dto, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DepartmentDetailDto> UpdateDepartmentAsync(Guid departmentId, UpdateDepartmentDto dto, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DepartmentDetailDto> ChangeStatusAsync(Guid departmentId, DepartmentStatusDto dto, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeleteDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
