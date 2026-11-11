using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Dashboard;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

public sealed class MyDashboardServiceTests
{
    private static readonly DateTimeOffset TestDate = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Teller_ReturnsWeightedScore_Capped_AndPacingOnPace_Month()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        world.AddTarget(world.Teller, world.KpiHdv, world.NovMonth, targetValue: 100m);
        world.AddTarget(world.Teller, world.KpiCsat, world.NovMonth, targetValue: 5m);

        // Actual progress in Nov 2026
        world.AddActualProgress(world.Teller, world.KpiHdv, world.NovMonth, new DateOnly(2026, 11, 5), value: 80m);
        world.AddActualProgress(world.Teller, world.KpiCsat, world.NovMonth, new DateOnly(2026, 11, 6), value: 4.5m);

        // Previous Oct 2026 targets and actuals (achievement 76%)
        world.AddTarget(world.Teller, world.KpiHdv, world.OctMonth, targetValue: 100m);
        world.AddTarget(world.Teller, world.KpiCsat, world.OctMonth, targetValue: 5m);
        world.AddActualProgress(world.Teller, world.KpiHdv, world.OctMonth, new DateOnly(2026, 10, 15), value: 76m);
        world.AddActualProgress(world.Teller, world.KpiCsat, world.OctMonth, new DateOnly(2026, 10, 20), value: 3.8m);

        await world.SaveAsync();

        var service = world.CreateService();
        var result = await service.GetMyProgressCardAsync(world.TellerAccount.Id, new MyProgressCardQueryDto("MONTH"));

        Assert.Equal("TELLER", result.Role);
        Assert.Equal("Hiệu suất tháng 11", result.CardTitle);
        Assert.Equal("2026-11", result.Period.Code);
        Assert.Equal("MONTH", result.Period.PeriodType);
        Assert.True(result.CurrentRate >= 80m);
        Assert.Equal("ON_PACE", result.PacingStatus);
        Assert.Equal("Đạt nhịp", result.PacingStatusLabel);
        Assert.Equal(29m, result.ExpectedRateToday); // 6 elapsed / 21 working days = 28.57% -> 29%
        Assert.Equal("Kỳ vọng hôm nay 29%", result.ExpectedRateTodayText);
        Assert.Null(result.PolicyNote);
        Assert.NotNull(result.PreviousPeriodComparison);
        Assert.Equal("MoM", result.PreviousPeriodComparison.ComparisonType);
        Assert.Equal("UP", result.PreviousPeriodComparison.Direction);
        Assert.Contains("so với T10", result.PreviousPeriodComparison.Text);
    }

    [Fact]
    public async Task SalesOfficer_ReturnsUncappedDoanhSo_AndPacingAhead_Month()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        world.AddTarget(world.Sales, world.KpiHdv, world.NovMonth, targetValue: 100m);
        // Exceed target: 104m actual on 100m target
        world.AddActualProgress(world.Sales, world.KpiHdv, world.NovMonth, new DateOnly(2026, 11, 7), value: 104m);

        // Previous Oct 2026 (achievement 92%)
        world.AddTarget(world.Sales, world.KpiHdv, world.OctMonth, targetValue: 100m);
        world.AddActualProgress(world.Sales, world.KpiHdv, world.OctMonth, new DateOnly(2026, 10, 25), value: 92m);

        await world.SaveAsync();

        var service = world.CreateService();
        var result = await service.GetMyProgressCardAsync(world.SalesAccount.Id, new MyProgressCardQueryDto("MONTH"));

        Assert.Equal("SALES", result.Role);
        Assert.Equal("Doanh số tháng 11", result.CardTitle);
        Assert.Equal(104m, result.CurrentRate);
        Assert.Equal("AHEAD", result.PacingStatus);
        Assert.Equal("Vượt", result.PacingStatusLabel);
        Assert.Equal("KPI Sales không trần", result.PolicyNote);
        Assert.Null(result.ExpectedRateTodayText); // Hidden when exceeded 100% per design
        Assert.NotNull(result.PreviousPeriodComparison);
        Assert.Equal("MoM", result.PreviousPeriodComparison.ComparisonType);
        Assert.Equal("UP", result.PreviousPeriodComparison.Direction);
        Assert.Equal(12m, result.PreviousPeriodComparison.Delta);
        Assert.Equal("▲ 12% MoM", result.PreviousPeriodComparison.Text);
    }

    [Fact]
    public async Task QuarterMode_ReturnsQuarterTitle_AndQoQComparison()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        world.AddTarget(world.Sales, world.KpiHdv, world.Q4Quarter, targetValue: 300m);
        world.AddActualProgress(world.Sales, world.KpiHdv, world.Q4Quarter, new DateOnly(2026, 10, 15), value: 150m);
        world.AddActualProgress(world.Sales, world.KpiHdv, world.Q4Quarter, new DateOnly(2026, 11, 5), value: 186m); // Total 336m on 300m = 112%

        // Q3 Quarter target & actual (previous quarter: 100m actual on 100m target = 100% or 97%)
        world.AddTarget(world.Sales, world.KpiHdv, world.Q3Quarter, targetValue: 300m);
        world.AddActualProgress(world.Sales, world.KpiHdv, world.Q3Quarter, new DateOnly(2026, 8, 15), value: 291m); // 97%

        await world.SaveAsync();

        var service = world.CreateService();
        var result = await service.GetMyProgressCardAsync(world.SalesAccount.Id, new MyProgressCardQueryDto("QUARTER"));

        Assert.Equal("SALES", result.Role);
        Assert.Equal("Doanh số Quý 4/2026", result.CardTitle);
        Assert.Equal("2026-Q4", result.Period.Code);
        Assert.Equal("QUARTER", result.Period.PeriodType);
        Assert.Equal(112m, result.CurrentRate);
        Assert.Equal("AHEAD", result.PacingStatus);
        Assert.Equal("Vượt", result.PacingStatusLabel);
        Assert.Equal(42m, result.ExpectedRateToday); // 28 elapsed / 66 total working days ≈ 42.4% -> 42%
        Assert.NotNull(result.PreviousPeriodComparison);
        Assert.Equal("QoQ", result.PreviousPeriodComparison.ComparisonType);
        Assert.Equal("UP", result.PreviousPeriodComparison.Direction);
        Assert.Equal(15m, result.PreviousPeriodComparison.Delta);
        Assert.Equal("▲ 15% QoQ", result.PreviousPeriodComparison.Text);
    }

    [Fact]
    public async Task MonthMode_ProratesQuarterTarget_WhenDirectMonthTargetMissing()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        // Only assign target at Q4 Quarter level (66 working days total, Nov has 21 working days -> ~31.8% of target)
        world.AddTarget(world.Sales, world.KpiHdv, world.Q4Quarter, targetValue: 210m);
        // Actual in Nov: 35m (vs prorated Nov target of ~66.8m)
        world.AddActualProgress(world.Sales, world.KpiHdv, world.NovMonth, new DateOnly(2026, 11, 5), value: 35m);

        await world.SaveAsync();

        var service = world.CreateService();
        var result = await service.GetMyProgressCardAsync(world.SalesAccount.Id, new MyProgressCardQueryDto("MONTH", world.NovMonth.Id));

        Assert.Equal("SALES", result.Role);
        Assert.Equal("Doanh số tháng 11", result.CardTitle);
        Assert.True(result.CurrentRate > 0m);
    }

    [Fact]
    public async Task DayPeriod_ThrowsValidationException()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        var dayPeriod = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "D-2026-11-09",
            Name = "Ngày 09/11/2026",
            PeriodType = PeriodType.Day,
            StartDate = new DateOnly(2026, 11, 9),
            EndDate = new DateOnly(2026, 11, 9),
            Status = PeriodStatus.Open
        };
        world.Db.EvaluationPeriods.Add(dayPeriod);
        await world.SaveAsync();

        var service = world.CreateService();
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetMyProgressCardAsync(world.TellerAccount.Id, new MyProgressCardQueryDto(PeriodId: dayPeriod.Id)));
    }

    [Fact]
    public async Task UnknownPeriod_ThrowsNotFoundException()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        var service = world.CreateService();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetMyProgressCardAsync(world.TellerAccount.Id, new MyProgressCardQueryDto(PeriodId: Guid.NewGuid())));
    }

    [Fact]
    public async Task LockedOrUnknownAccount_ThrowsForbiddenException()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        world.TellerAccount.IsLocked = true;
        await world.SaveAsync();

        var service = world.CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetMyProgressCardAsync(world.TellerAccount.Id, new MyProgressCardQueryDto()));
    }

    [Fact]
    public async Task Teller_BehindPacing_WhenActualProgressLow()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        world.AddTarget(world.Teller, world.KpiHdv, world.NovMonth, targetValue: 100m);
        // Only 10m actual progress by day 6 on 60% weighted criteria -> current rate is 6% (expected is 29%)
        world.AddActualProgress(world.Teller, world.KpiHdv, world.NovMonth, new DateOnly(2026, 11, 5), value: 10m);
        await world.SaveAsync();

        var service = world.CreateService();
        var result = await service.GetMyProgressCardAsync(world.TellerAccount.Id, new MyProgressCardQueryDto("MONTH"));

        Assert.Equal("TELLER", result.Role);
        Assert.Equal(6m, result.CurrentRate);
        Assert.Equal("BEHIND", result.PacingStatus);
        Assert.Equal("Chậm nhịp", result.PacingStatusLabel);
        Assert.Equal(29m, result.ExpectedRateToday);
    }

    [Fact]
    public async Task Employee_NoTargetsAssigned_ReturnsZeroRateAndBehindPacing()
    {
        using var world = await TestWorld.CreateAsync(TestDate);
        // No targets or actuals added
        var service = world.CreateService();
        var result = await service.GetMyProgressCardAsync(world.TellerAccount.Id, new MyProgressCardQueryDto("MONTH"));

        Assert.Equal(0m, result.CurrentRate);
        Assert.Equal("BEHIND", result.PacingStatus);
        Assert.Equal("Chậm nhịp", result.PacingStatusLabel);
        Assert.NotNull(result.PreviousPeriodComparison);
        Assert.Equal("FLAT", result.PreviousPeriodComparison.Direction);
        Assert.Equal(0m, result.PreviousPeriodComparison.Delta);
    }

    private sealed class TestWorld : IDisposable
    {
        public AppDbContext Db { get; }
        public DateTimeOffset Clock { get; }
        public Employee Teller { get; }
        public Employee Sales { get; }
        public Account TellerAccount { get; }
        public Account SalesAccount { get; }
        public KpiDefinition KpiHdv { get; }
        public KpiDefinition KpiCsat { get; }
        public EvaluationPeriod OctMonth { get; }
        public EvaluationPeriod NovMonth { get; }
        public EvaluationPeriod Q3Quarter { get; }
        public EvaluationPeriod Q4Quarter { get; }

        private TestWorld(
            AppDbContext db,
            DateTimeOffset clock,
            Employee teller,
            Employee sales,
            Account tellerAccount,
            Account salesAccount,
            KpiDefinition kpiHdv,
            KpiDefinition kpiCsat,
            EvaluationPeriod octMonth,
            EvaluationPeriod novMonth,
            EvaluationPeriod q3Quarter,
            EvaluationPeriod q4Quarter)
        {
            Db = db;
            Clock = clock;
            Teller = teller;
            Sales = sales;
            TellerAccount = tellerAccount;
            SalesAccount = salesAccount;
            KpiHdv = kpiHdv;
            KpiCsat = kpiCsat;
            OctMonth = octMonth;
            NovMonth = novMonth;
            Q3Quarter = q3Quarter;
            Q4Quarter = q4Quarter;
        }

        public static async Task<TestWorld> CreateAsync(DateTimeOffset clock)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new AppDbContext(options);

            var dept = new Department
            {
                Id = Guid.NewGuid(),
                Code = "CN01",
                Name = "Chi nhánh 1",
                LevelType = DepartmentLevelType.Branch,
                Status = RecordStatus.Active,
                UpdatedAt = clock
            };
            db.Departments.Add(dept);

            var posTeller = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = JobPositionCodes.Teller,
                Title = "Giao dịch viên",
                Status = RecordStatus.Active,
                UpdatedAt = clock
            };
            var posSales = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = JobPositionCodes.Sales,
                Title = "Chuyên viên Kinh doanh",
                Status = RecordStatus.Active,
                UpdatedAt = clock
            };
            db.JobPositions.AddRange(posTeller, posSales);

            var empTeller = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV01",
                FullName = "Nguyễn Minh Anh",
                Email = "teller@bpms.com",
                DepartmentId = dept.Id,
                JobPositionId = posTeller.Id,
                Status = EmployeeStatus.Active,
                UpdatedAt = clock
            };
            var empSales = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV02",
                FullName = "Trần Quốc Bảo",
                Email = "sales@bpms.com",
                DepartmentId = dept.Id,
                JobPositionId = posSales.Id,
                Status = EmployeeStatus.Active,
                UpdatedAt = clock
            };
            db.Employees.AddRange(empTeller, empSales);

            var accTeller = new Account
            {
                Id = Guid.NewGuid(),
                Username = "teller@bpms.com",
                PasswordHash = "hash",
                EmployeeId = empTeller.Id,
                IsLocked = false,
                UpdatedAt = clock
            };
            var accSales = new Account
            {
                Id = Guid.NewGuid(),
                Username = "sales@bpms.com",
                PasswordHash = "hash",
                EmployeeId = empSales.Id,
                IsLocked = false,
                UpdatedAt = clock
            };
            db.Accounts.AddRange(accTeller, accSales);

            var kpiGroup = new KpiGroup
            {
                Id = Guid.NewGuid(),
                Code = "KD",
                Name = "Kinh doanh",
                Status = KpiStatus.Active,
                UpdatedAt = clock
            };
            db.KpiGroups.Add(kpiGroup);

            var kpiHdv = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = kpiGroup.Id,
                Code = "HDV",
                Name = "Huy động vốn",
                Unit = "triệu VND",
                Direction = KpiDirection.HigherBetter,
                AggregationType = KpiAggregationType.Sum,
                Status = KpiStatus.Active,
                UpdatedAt = clock
            };
            var kpiCsat = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = kpiGroup.Id,
                Code = "CSAT",
                Name = "Chất lượng dịch vụ",
                Unit = "điểm",
                Direction = KpiDirection.HigherBetter,
                AggregationType = KpiAggregationType.Avg,
                Status = KpiStatus.Active,
                MaxAchievementPercent = 100m,
                UpdatedAt = clock
            };
            db.KpiDefinitions.AddRange(kpiHdv, kpiCsat);

            // Teller evaluation profile
            var profileTeller = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                JobPositionId = posTeller.Id,
                Code = "EP-TELLER",
                Name = "Profile Teller",
                Version = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Status = EvaluationProfileStatus.Active,
                UpdatedAt = clock
            };
            db.EvaluationProfiles.Add(profileTeller);

            var itemHdv = new EvaluationProfileItem
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = profileTeller.Id,
                KpiDefinitionId = kpiHdv.Id,
                Name = "Huy động vốn",
                WeightPercent = 60m,
                DisplayOrder = 1,
                UpdatedAt = clock
            };
            var itemCsat = new EvaluationProfileItem
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = profileTeller.Id,
                KpiDefinitionId = kpiCsat.Id,
                Name = "Chất lượng dịch vụ",
                WeightPercent = 40m,
                DisplayOrder = 2,
                UpdatedAt = clock
            };
            db.EvaluationProfileItems.AddRange(itemHdv, itemCsat);

            // Periods
            var q3 = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = "2026-Q3",
                Name = "Quý 3/2026",
                PeriodType = PeriodType.Quarter,
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 9, 30),
                Status = PeriodStatus.Open
            };
            var q4 = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = "2026-Q4",
                Name = "Quý 4/2026",
                PeriodType = PeriodType.Quarter,
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 12, 31),
                Status = PeriodStatus.Open
            };
            var oct = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = "2026-10",
                Name = "Tháng 10/2026",
                PeriodType = PeriodType.Month,
                ParentPeriodId = q4.Id,
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31),
                Status = PeriodStatus.Open
            };
            var nov = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = "2026-11",
                Name = "Tháng 11/2026",
                PeriodType = PeriodType.Month,
                ParentPeriodId = q4.Id,
                StartDate = new DateOnly(2026, 11, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = PeriodStatus.Open
            };
            db.EvaluationPeriods.AddRange(q3, q4, oct, nov);

            await db.SaveChangesAsync();

            return new TestWorld(db, clock, empTeller, empSales, accTeller, accSales, kpiHdv, kpiCsat, oct, nov, q3, q4);
        }

        public TargetAssignment AddTarget(Employee emp, KpiDefinition kpi, EvaluationPeriod period, decimal targetValue)
        {
            var target = new TargetAssignment
            {
                Id = Guid.NewGuid(),
                EmployeeId = emp.Id,
                KpiDefinitionId = kpi.Id,
                EvaluationPeriodId = period.Id,
                TargetValue = targetValue,
                Status = TargetAssignmentStatus.Active,
                UpdatedAt = Clock
            };
            Db.TargetAssignments.Add(target);
            return target;
        }

        public ActualProgress AddActualProgress(Employee emp, KpiDefinition kpi, EvaluationPeriod period, DateOnly date, decimal value)
        {
            var target = Db.TargetAssignments.Local
                .FirstOrDefault(t => t.EmployeeId == emp.Id && t.KpiDefinitionId == kpi.Id && t.EvaluationPeriodId == period.Id);

            target ??= AddTarget(emp, kpi, period, value);

            var ap = new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = target.Id,
                ProgressDate = date,
                Value = value,
                Status = ActualProgressStatus.Valid,
                UpdatedAt = Clock
            };
            Db.ActualProgresses.Add(ap);
            return ap;
        }

        public MyDashboardService CreateService()
        {
            return new MyDashboardService(
                Db,
                new WeekdayWorkingDayCalendar(),
                new PerformanceScoreFormula(),
                new FixedTimeProvider(Clock));
        }

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public void Dispose() => Db.Dispose();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
