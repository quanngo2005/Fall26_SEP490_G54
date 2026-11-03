using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
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
        var service = new TargetSetupService(uow, db, permissionService);

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
        var restrictedService = new TargetSetupService(new UnitOfWork(f.Db), f.Db, restrictedPermission);

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
        Assert.Equal(320m, ratioRow.Sum);

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
        var restrictedService = new TargetSetupService(new UnitOfWork(f.Db), f.Db, restrictedPermission);

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
        var restrictedService = new TargetSetupService(new UnitOfWork(f.Db), f.Db, restrictedPermission);

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
