using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.WorkTransaction;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class WorkTransactionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 2);

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Rig
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public WorkTransactionService Service => new(Db, new Fixed(Now));
        public Employee Owner { get; } = Person("E1");
        public Employee Other { get; } = Person("E2");
        public Work Work { get; }
        public List<(KpiDefinition Kpi, TargetAssignment Target)> Kpis { get; } = [];
        private readonly WorkType _workType;

        public Rig(params (KpiAggregationType Aggregation, string Source)[] mappings)
        {
            var department = new Department { Id = Guid.NewGuid(), Code = "D", Name = "D", LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, UpdatedAt = Now };
            var position = new JobPosition { Id = Guid.NewGuid(), Code = "P", Title = "P", Status = RecordStatus.Active, UpdatedAt = Now };
            Db.Departments.Add(department);
            Db.JobPositions.Add(position);
            foreach (var e in new[] { Owner, Other })
            {
                e.DepartmentId = department.Id;
                e.JobPositionId = position.Id;
            }

            Db.Employees.AddRange(Owner, Other);
            _workType = new WorkType { Id = Guid.NewGuid(), Code = "WT", Name = "WT", ProductCode = "CARD", Status = RecordStatus.Active, UpdatedAt = Now };
            Db.WorkTypes.Add(_workType);
            var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "G", Status = KpiStatus.Active, UpdatedAt = Now };
            Db.KpiGroups.Add(group);
            var period = new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = "P1",
                Name = "P1",
                PeriodType = PeriodType.Month,
                StartDate = new DateOnly(2026, 11, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = PeriodStatus.Open,
                UpdatedAt = Now,
            };
            Db.EvaluationPeriods.Add(period);
            foreach (var (aggregation, source) in mappings)
            {
                var kpi = new KpiDefinition
                {
                    Id = Guid.NewGuid(),
                    KpiGroupId = group.Id,
                    Code = $"K{Kpis.Count}",
                    Name = "K",
                    Unit = "u",
                    AggregationType = aggregation,
                    Direction = KpiDirection.HigherBetter,
                    Status = KpiStatus.Active,
                    UpdatedAt = Now,
                };
                var target = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    KpiDefinitionId = kpi.Id,
                    EmployeeId = Owner.Id,
                    EvaluationPeriodId = period.Id,
                    Version = 1,
                    TargetValue = 100m,
                    Status = TargetAssignmentStatus.Active,
                    CreatedAt = Now,
                    UpdatedAt = Now,
                };
                Db.KpiDefinitions.Add(kpi);
                Db.TargetAssignments.Add(target);
                Db.WorkTypeKpis.Add(new WorkTypeKpi
                {
                    Id = Guid.NewGuid(),
                    WorkTypeId = _workType.Id,
                    KpiDefinitionId = kpi.Id,
                    IsRequired = true,
                    Multiplier = 1.5m,
                    ValueSource = source,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    UpdatedAt = Now,
                });
                Kpis.Add((kpi, target));
            }

            Work = new Work
            {
                Id = Guid.NewGuid(),
                WorkTypeId = _workType.Id,
                AssigneeEmployeeId = Owner.Id,
                CreatedBy = Owner.Id,
                Code = "W1",
                Title = "W1",
                Status = WorkStatus.InProgress,
                Priority = WorkPriority.Medium,
                UpdatedAt = Now,
            };
            Db.Works.Add(Work);
            Db.SaveChanges();
        }

        public void Import(string code, Employee? employee = null, string product = "CARD", decimal amount = 100m, decimal? denominator = null)
        {
            var batch = Db.ImportBatches.FirstOrDefault() ?? new ImportBatch { Id = Guid.NewGuid(), FileName = "f.xlsx", ImportedBy = Owner.Id, ImportedAt = Now };
            Db.ImportedTransactions.Add(new ImportedTransaction
            {
                Id = Guid.NewGuid(),
                ImportBatch = batch,
                TransactionCode = code,
                EmployeeId = (employee ?? Owner).Id,
                ProductCode = product,
                TransactionDate = Day,
                Amount = amount,
                Denominator = denominator,
            });
            Db.SaveChanges();
        }

        public Task<WorkTransactionsDto> AddAsync(params string[] codes) =>
            Service.AddAsync(Work.Id, Owner.Id, new AddWorkTransactionsRequestDto([.. codes]));

        private static Employee Person(string code) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = code,
            Email = $"{code}@x.test",
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
    }

    [Fact]
    public async Task Add_MatchedCodeCreditsEveryMappedKpiAndCompletesTheWork()
    {
        var rig = new Rig((KpiAggregationType.Sum, KpiValueSource.Count), (KpiAggregationType.Sum, KpiValueSource.Amount));
        rig.Import("T1", amount: 250m);

        var result = await rig.AddAsync("T1");

        Assert.Equal("MATCHED", Assert.Single(result.Items).Status);
        Assert.Equal("DONE", result.WorkStatus);
        var progress = await rig.Db.ActualProgresses.ToListAsync();
        Assert.Equal(1m, progress.Single(p => p.TargetAssignmentId == rig.Kpis[0].Target.Id).Value);
        Assert.Equal(250m, progress.Single(p => p.TargetAssignmentId == rig.Kpis[1].Target.Id).Value);
        Assert.Equal(2, await rig.Db.WorkResultItems.CountAsync());
    }

    [Fact]
    public async Task Add_CodeNotImportedYetStaysInReviewAndTheWorkStaysSubmitted()
    {
        var rig = new Rig((KpiAggregationType.Sum, KpiValueSource.Count));

        var result = await rig.AddAsync("T1");

        Assert.Equal("IN_REVIEW", Assert.Single(result.Items).Status);
        Assert.Equal("SUBMITTED", result.WorkStatus);
        Assert.Empty(rig.Db.ActualProgresses);
    }

    [Fact]
    public async Task ReconcileBatch_MatchesAnInReviewCodeOnceItsTransactionIsImported()
    {
        var rig = new Rig((KpiAggregationType.Sum, KpiValueSource.Count));
        await rig.AddAsync("T1");
        rig.Import("T1");

        await rig.Service.ReconcileBatchAsync(rig.Db.ImportBatches.Single().Id);
        await rig.Service.ReconcileBatchAsync(rig.Db.ImportBatches.Single().Id);

        var item = await rig.Db.WorkTransactions.SingleAsync();
        Assert.Equal("MATCHED", item.Status);
        Assert.Equal(WorkStatus.Done, (await rig.Db.Works.SingleAsync()).Status);
        Assert.Equal(1m, (await rig.Db.ActualProgresses.SingleAsync()).Value);
    }

    [Fact]
    public async Task Add_WrongEmployeeOrProductIsNotMatchedAndTheWorkIsNotDone()
    {
        var rig = new Rig((KpiAggregationType.Sum, KpiValueSource.Count));
        rig.Import("T1", employee: rig.Other);
        rig.Import("T2", product: "LOAN");
        rig.Import("T3");

        var result = await rig.AddAsync("T1", "T2", "T3");

        Assert.Equal("EMPLOYEE_MISMATCH", result.Items.Single(i => i.TransactionCode == "T1").Reason);
        Assert.Equal("PRODUCT_MISMATCH", result.Items.Single(i => i.TransactionCode == "T2").Reason);
        Assert.Equal("MATCHED", result.Items.Single(i => i.TransactionCode == "T3").Status);
        Assert.Equal("SUBMITTED", result.WorkStatus);
        Assert.Equal(1m, (await rig.Db.ActualProgresses.SingleAsync()).Value);
    }

    [Fact]
    public async Task Add_AvgAndRatioKeepTheirDenominators_AndNoAggregationStaysInReview()
    {
        var rig = new Rig((KpiAggregationType.Avg, KpiValueSource.Amount), (KpiAggregationType.Ratio, KpiValueSource.Amount));
        rig.Import("T1", amount: 40m, denominator: 80m);

        var result = await rig.AddAsync("T1");

        Assert.Equal("MATCHED", result.Items.Single().Status);
        var progress = await rig.Db.ActualProgresses.ToListAsync();
        Assert.Equal(1m, progress.Single(p => p.TargetAssignmentId == rig.Kpis[0].Target.Id).Denominator);
        Assert.Equal(80m, progress.Single(p => p.TargetAssignmentId == rig.Kpis[1].Target.Id).Denominator);

        var blocked = new Rig((KpiAggregationType.NoAggregation, KpiValueSource.Count));
        blocked.Import("T1");
        var held = (await blocked.AddAsync("T1")).Items.Single();
        Assert.Equal("IN_REVIEW", held.Status);
        Assert.Equal(ErrorCodes.KpiAggregationNotSupported, held.Reason);
        Assert.Empty(blocked.Db.ActualProgresses);
    }

    [Fact]
    public async Task Add_EnforcesTheTenCodeLimitAndGlobalUniqueness()
    {
        var rig = new Rig((KpiAggregationType.Sum, KpiValueSource.Count));
        await rig.AddAsync(Enumerable.Range(1, 10).Select(i => $"T{i}").ToArray());

        var limit = await Assert.ThrowsAsync<BusinessRuleException>(() => rig.AddAsync("T11"));
        Assert.Equal(ErrorCodes.WorkTransactionLimit, limit.ErrorCode);

        var other = new Rig((KpiAggregationType.Sum, KpiValueSource.Count));
        await other.AddAsync("X1");
        await Assert.ThrowsAsync<ValidationException>(() => other.AddAsync());
    }

    [Fact]
    public async Task Add_ByAnotherEmployeeIsForbidden_AndRemoveDeletesOnlyUnmatchedCodes()
    {
        var rig = new Rig((KpiAggregationType.Sum, KpiValueSource.Count));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            rig.Service.AddAsync(rig.Work.Id, rig.Other.Id, new AddWorkTransactionsRequestDto(["T1"])));

        rig.Import("T1");
        var added = await rig.AddAsync("T1", "T2");
        var matched = added.Items.Single(i => i.TransactionCode == "T1");
        var pending = added.Items.Single(i => i.TransactionCode == "T2");

        await Assert.ThrowsAsync<BusinessRuleException>(() => rig.Service.RemoveAsync(rig.Work.Id, matched.Id, rig.Owner.Id));
        var after = await rig.Service.RemoveAsync(rig.Work.Id, pending.Id, rig.Owner.Id);
        Assert.Equal("DONE", after.WorkStatus);
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.RemoveAsync(rig.Work.Id, pending.Id, rig.Owner.Id));
    }
}
