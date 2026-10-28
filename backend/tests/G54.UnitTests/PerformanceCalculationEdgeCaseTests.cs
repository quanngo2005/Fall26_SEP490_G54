using System.Globalization;
using G54.BLL.Common.Constants;
using G54.BLL.Dtos.Performance;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

/// <summary>
/// Calculation rules of <see cref="PerformanceEvaluationService.CalculateAsync"/> that
/// <see cref="PerformanceEvaluationServiceTests"/> does not pin: inactive KPIs, the inclusive period window, which targets
/// count, the task-weight floor, banker's rounding, and how unscored groups affect the total.
/// </summary>
public sealed class PerformanceCalculationEdgeCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly PeriodStart = new(2026, 10, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 10, 31);
    private static readonly DateOnly Day10 = new(2026, 10, 10);
    private static readonly DateOnly Day11 = new(2026, 10, 11);

    // ----- KPI selection --------------------------------------------------------------------

    [Fact]
    public async Task Calculate_LeavesInactiveKpisOutEvenWhenTheyHaveATargetAndProgress()
    {
        using var rig = await Rig.CreateAsync([100m]);
        var active = rig.AddKpi(0, "ACTIVE");
        var retired = rig.AddKpi(0, "RETIRED", status: KpiStatus.Inactive);
        rig.AddTarget(active, 100m);
        rig.AddTarget(retired, 100m);
        rig.AddProgress(active, 40m, Day10);
        rig.AddProgress(retired, 100m, Day10);
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        var group = Assert.Single(dto.Groups);
        Assert.Equal(["ACTIVE"], group.Kpis.Select(k => k.KpiCode));
        Assert.Equal(40m, group.ComponentScore); // the retired KPI's 100 does not lift the mean
        Assert.Equal(40m, dto.TotalScore);
        Assert.Equal(1, await rig.Db.PerformanceContributions.CountAsync());
    }

    // ----- Period window --------------------------------------------------------------------

    [Fact]
    public async Task Calculate_CountsProgressOnTheFirstAndLastDayOfThePeriodButNotTheDaysAround()
    {
        using var rig = await Rig.CreateAsync([100m]);
        var kpi = rig.AddKpi(0, "SUM");
        rig.AddTarget(kpi, 100m);
        rig.AddProgress(kpi, 100m, PeriodStart.AddDays(-1));
        rig.AddProgress(kpi, 10m, PeriodStart);
        rig.AddProgress(kpi, 20m, PeriodEnd);
        rig.AddProgress(kpi, 100m, PeriodEnd.AddDays(1));
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        Assert.Equal(30m, Assert.Single(Assert.Single(dto.Groups).Kpis).ActualValue);
    }

    // ----- Targets --------------------------------------------------------------------------

    [Theory]
    [InlineData("superseded")]
    [InlineData("other-department")]
    [InlineData("other-period")]
    public async Task Calculate_IgnoresATargetThatDoesNotApplyToTheEmployeeAndPeriod(string scenario)
    {
        using var rig = await Rig.CreateAsync([100m]);
        var kpi = rig.AddKpi(0, "SUM");
        switch (scenario)
        {
            case "superseded":
                rig.AddTarget(kpi, 100m, status: TargetAssignmentStatus.Superseded);
                break;
            case "other-department":
                rig.AddTarget(kpi, 100m, departmentId: rig.OtherDepartment.Id);
                break;
            default:
                rig.AddTarget(kpi, 100m, periodId: rig.OtherPeriod.Id);
                break;
        }

        rig.AddProgress(kpi, 40m, Day10);
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        Assert.Empty(Assert.Single(dto.Groups).Kpis);
        Assert.Null(dto.TotalScore);
        Assert.Equal("DRAFT", dto.Status);
        Assert.Equal(0, await rig.Db.PerformanceContributions.CountAsync());
    }

    // ----- Task weight floor ----------------------------------------------------------------

    [Fact]
    public async Task Calculate_NeverLetsAWeightedResultDragTheActualBelowZero()
    {
        // Defensive: the weight adjustment is applied on top of the stored sum, so a result item larger than its progress
        // row (inconsistent data) must not produce a negative actual.
        using var rig = await Rig.CreateAsync([100m]);
        var kpi = rig.AddKpi(0, "SUM");
        rig.AddTarget(kpi, 100m);
        var progress = rig.AddProgress(kpi, 5m, Day10);
        rig.AddWeightedResult(progress, value: 10m, taskWeight: 0.1m); // 5 + 10 * (0.1 - 1) = -4
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        var scored = Assert.Single(Assert.Single(dto.Groups).Kpis);
        Assert.Equal(0m, scored.ActualValue);
        Assert.Equal(0m, scored.ComponentScore);
        Assert.Equal(0m, dto.TotalScore);
    }

    // ----- Rounding -------------------------------------------------------------------------

    public static TheoryData<string, string> MidpointScores => new()
    {
        { "12.34565", "12.3456" }, // 6 is even: stays
        { "12.34575", "12.3458" }, // 7 is odd: goes up to the even neighbour
        { "12.34585", "12.3458" }, // 8 is even: stays
    };

    [Theory]
    [MemberData(nameof(MidpointScores))]
    public async Task Calculate_RoundsAScoreMidpointToTheEvenFourthDecimal(string actual, string expected)
    {
        using var rig = await Rig.CreateAsync([100m]);
        var kpi = rig.AddKpi(0, "SUM");
        rig.AddTarget(kpi, 100m);
        rig.AddProgress(kpi, Parse(actual), Day10);
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        var scored = Assert.Single(Assert.Single(dto.Groups).Kpis);
        Assert.Equal(Parse(expected), scored.AchievementPercentRaw);
        Assert.Equal(Parse(expected), scored.ComponentScore);
        Assert.Equal(Parse(expected), dto.TotalScore);
    }

    public static TheoryData<string, string, string> MidpointAverages => new()
    {
        { "0.0002", "0.0003", "0.0002" }, // mean 0.00025: 2 is even
        { "0.0001", "0.0002", "0.0002" }, // mean 0.00015: 1 is odd
        { "0.0004", "0.0005", "0.0004" }, // mean 0.00045: 4 is even, so away-from-zero would give 0.0005
    };

    [Theory]
    [MemberData(nameof(MidpointAverages))]
    public async Task Calculate_RoundsAnAverageMidpointToTheEvenFourthDecimal(string first, string second, string expected)
    {
        using var rig = await Rig.CreateAsync([100m]);
        var kpi = rig.AddKpi(0, "AVG", aggregation: KpiAggregationType.Avg);
        rig.AddTarget(kpi, 1m);
        rig.AddProgress(kpi, Parse(first), Day10);
        rig.AddProgress(kpi, Parse(second), Day11);
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        Assert.Equal(Parse(expected), Assert.Single(Assert.Single(dto.Groups).Kpis).ActualValue);
    }

    // ----- Groups and total -----------------------------------------------------------------

    [Fact]
    public async Task Calculate_DoesNotShareTheWeightOfAGroupWithoutDataAmongTheOthers()
    {
        using var rig = await Rig.CreateAsync([60m, 40m]);
        var scored = rig.AddKpi(0, "SCORED");
        var silent = rig.AddKpi(1, "SILENT");
        rig.AddTarget(scored, 100m);
        rig.AddTarget(silent, 100m); // a target but no progress: nothing to score
        rig.AddProgress(scored, 50m, Day10);
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        Assert.Equal(50m, dto.Groups[0].ComponentScore);
        Assert.Null(dto.Groups[1].ComponentScore);
        Assert.Equal(30m, dto.TotalScore); // 50 * 60 %, not rescaled to 50
        Assert.Equal("CALCULATED", dto.Status);
    }

    [Fact]
    public async Task Calculate_WeighsEachGroupByItsProfileWeight_AndAveragesTheKpisInsideAGroup()
    {
        using var rig = await Rig.CreateAsync([70m, 30m]);
        var first = rig.AddKpi(0, "A1");
        var second = rig.AddKpi(0, "A2");
        var other = rig.AddKpi(1, "B1");
        rig.AddTarget(first, 100m);
        rig.AddTarget(second, 100m);
        rig.AddTarget(other, 100m);
        rig.AddProgress(first, 100m, Day10);
        rig.AddProgress(second, 50m, Day10);
        rig.AddProgress(other, 80m, Day10);
        await rig.SaveAsync();

        var dto = await rig.Service().CalculateAsync(rig.ManagerAccount.Id, rig.Evaluation.Id);

        Assert.Equal(75m, dto.Groups[0].ComponentScore); // mean of 100 and 50
        Assert.Equal(80m, dto.Groups[1].ComponentScore);
        Assert.Equal(76.5m, dto.TotalScore); // 75 * 0.7 + 80 * 0.3
        Assert.Equal(3, await rig.Db.PerformanceContributions.CountAsync());
        var snapshots = await rig.Db.PerformanceContributions.AsNoTracking().Select(c => c.WeightSnapshot).ToListAsync();
        Assert.Equal([30m, 70m, 70m], snapshots.OrderBy(w => w));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    // ----- Fixture --------------------------------------------------------------------------

    private sealed class Rig : IDisposable
    {
        private readonly List<KpiGroup> _groups = [];
        private readonly Dictionary<Guid, TargetAssignment> _carriers = [];

        public required AppDbContext Db { get; init; }
        public required Department Department { get; init; }
        public required Department OtherDepartment { get; init; }
        public required Employee Employee { get; init; }
        public required Employee Manager { get; init; }
        public required Account ManagerAccount { get; init; }
        public required EvaluationPeriod Period { get; init; }
        public required EvaluationPeriod OtherPeriod { get; init; }
        public required PerformanceEvaluation Evaluation { get; init; }

        public static async Task<Rig> CreateAsync(decimal[] groupWeights)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var department = Dept("DA");
            var otherDepartment = Dept("DB");
            db.Departments.AddRange(department, otherDepartment);

            var position = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "POS",
                Title = "Position",
                Status = RecordStatus.Active,
                UpdatedAt = Now,
            };
            db.JobPositions.Add(position);

            var employee = Person("E001", department, position);
            var manager = Person("M001", department, position);
            db.Employees.AddRange(employee, manager);
            var account = new Account
            {
                Id = Guid.NewGuid(),
                EmployeeId = manager.Id,
                Username = manager.Email,
                PasswordHash = "hash",
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            db.Accounts.Add(account);

            var profile = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                JobPositionId = position.Id,
                Code = "EP",
                Name = "Profile",
                Version = 1,
                EffectiveFrom = PeriodStart,
                Status = EvaluationProfileStatus.Active,
                UpdatedAt = Now,
            };
            db.EvaluationProfiles.Add(profile);

            var groups = new List<KpiGroup>();
            for (var index = 0; index < groupWeights.Length; index++)
            {
                var group = new KpiGroup
                {
                    Id = Guid.NewGuid(),
                    Code = $"G{index + 1}",
                    Name = $"Group {index + 1}",
                    Status = KpiStatus.Active,
                    UpdatedAt = Now,
                };
                groups.Add(group);
                db.KpiGroups.Add(group);
                db.EvaluationProfileItems.Add(new EvaluationProfileItem
                {
                    Id = Guid.NewGuid(),
                    EvaluationProfileId = profile.Id,
                    KpiGroupId = group.Id,
                    WeightPercent = groupWeights[index],
                    DisplayOrder = index + 1,
                    UpdatedAt = Now,
                });
            }

            var period = NewPeriod("2026-10", PeriodStart, PeriodEnd);
            var otherPeriod = NewPeriod("2026-11", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
            db.EvaluationPeriods.AddRange(period, otherPeriod);

            var evaluation = new PerformanceEvaluation
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EvaluationPeriodId = period.Id,
                EvaluationProfileId = profile.Id,
                Status = PerformanceEvaluationStatus.Draft,
                UpdatedAt = Now,
            };
            db.PerformanceEvaluations.Add(evaluation);
            await db.SaveChangesAsync();

            var rig = new Rig
            {
                Db = db,
                Department = department,
                OtherDepartment = otherDepartment,
                Employee = employee,
                Manager = manager,
                ManagerAccount = account,
                Period = period,
                OtherPeriod = otherPeriod,
                Evaluation = evaluation,
            };
            rig._groups.AddRange(groups);
            return rig;
        }

        public PerformanceEvaluationService Service() => new(
            Db,
            new FakePermissionService(null, new UserPermissionDto("evaluation", "calculate", PermissionScopes.All, "EVALUATION_CALCULATE")),
            new PerformanceScoreFormula(),
            new FixedTime(Now),
            NullLogger<PerformanceEvaluationService>.Instance);

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public KpiDefinition AddKpi(
            int groupIndex,
            string code,
            KpiStatus status = KpiStatus.Active,
            KpiAggregationType aggregation = KpiAggregationType.Sum)
        {
            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = _groups[groupIndex].Id,
                Code = code,
                Name = code,
                Unit = "unit",
                AggregationType = aggregation,
                Direction = KpiDirection.HigherBetter,
                Status = status,
                UpdatedAt = Now,
            };
            Db.KpiDefinitions.Add(kpi);
            return kpi;
        }

        /// <summary>A target that applies to the employee's own department unless told otherwise.</summary>
        public void AddTarget(
            KpiDefinition kpi,
            decimal value,
            TargetAssignmentStatus status = TargetAssignmentStatus.Active,
            Guid? departmentId = null,
            Guid? periodId = null) =>
            Db.TargetAssignments.Add(new TargetAssignment
            {
                Id = Guid.NewGuid(),
                KpiDefinitionId = kpi.Id,
                DepartmentId = departmentId ?? Department.Id,
                EvaluationPeriodId = periodId ?? Period.Id,
                Version = 1,
                TargetValue = value,
                Status = status,
                CreatedAt = Now,
                UpdatedAt = Now,
            });

        /// <summary>Progress recorded on the employee's own (superseded) carrier target, as verification does.</summary>
        public ActualProgress AddProgress(KpiDefinition kpi, decimal value, DateOnly date)
        {
            if (!_carriers.TryGetValue(kpi.Id, out var carrier))
            {
                carrier = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    KpiDefinitionId = kpi.Id,
                    EmployeeId = Employee.Id,
                    EvaluationPeriodId = Period.Id,
                    Version = 1,
                    TargetValue = 0m,
                    Status = TargetAssignmentStatus.Superseded,
                    CreatedAt = Now,
                    UpdatedAt = Now,
                };
                Db.TargetAssignments.Add(carrier);
                _carriers[kpi.Id] = carrier;
            }

            var progress = new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = carrier.Id,
                ProgressDate = date,
                Value = value,
                Status = ActualProgressStatus.Valid,
                UpdatedAt = Now,
            };
            Db.ActualProgresses.Add(progress);
            return progress;
        }

        /// <summary>A verified result item feeding the progress row; the row keeps the unweighted sum.</summary>
        public void AddWeightedResult(ActualProgress progress, decimal value, decimal taskWeight)
        {
            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = $"W-{Guid.NewGuid():N}"[..12],
                Title = "Work",
                AssigneeEmployeeId = Employee.Id,
                CreatedBy = Employee.Id,
                Status = WorkStatus.Verified,
                Priority = WorkPriority.Medium,
                UpdatedAt = Now,
            };
            var submission = new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                SubmitterEmployeeId = Employee.Id,
                SubmissionNo = 1,
                Status = SubmissionStatus.Approved,
                SubmittedAt = Now,
                UpdatedAt = Now,
            };
            var verification = new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = Manager.Id,
                RoundNo = 1,
                Decision = VerificationDecision.Approved,
                VerifiedAt = Now,
                UpdatedAt = Now,
            };
            var result = new VerifiedWorkResult
            {
                Id = Guid.NewGuid(),
                VerificationId = verification.Id,
                VerifiedAt = Now,
                Status = VerifiedResultStatus.Valid,
                UpdatedAt = Now,
            };
            Db.Works.Add(work);
            Db.ResultSubmissions.Add(submission);
            Db.Verifications.Add(verification);
            Db.VerifiedWorkResults.Add(result);
            Db.WorkResultItems.Add(new WorkResultItem
            {
                Id = Guid.NewGuid(),
                VerifiedWorkResultId = result.Id,
                ActualProgressId = progress.Id,
                EmployeeId = Employee.Id,
                Value = value,
                TaskWeight = taskWeight,
                UpdatedAt = Now,
            });
        }

        public void Dispose() => Db.Dispose();

        private static Department Dept(string code) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            UpdatedAt = Now,
        };

        private static Employee Person(string code, Department department, JobPosition position) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code.ToLowerInvariant()}@example.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };

        private static EvaluationPeriod NewPeriod(string code, DateOnly start, DateOnly end) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = PeriodType.Month,
            StartDate = start,
            EndDate = end,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
