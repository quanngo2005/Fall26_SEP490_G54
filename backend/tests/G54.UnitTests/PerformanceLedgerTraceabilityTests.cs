using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

/// <summary>Score -> ledger line -> work / submission -> transaction (docs/api/TRACEABILITY_CONTRACT.md).</summary>
public sealed class PerformanceLedgerTraceabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ledger_ListsResultAndRiskPenaltyWithASummary()
    {
        using var world = await World.CreateAsync();
        var result = world.AddMatchedResult(world.Employee, rawValue: 10m, multiplier: 1.5m);
        var risk = world.AddRisk(world.Employee, RiskEventSeverity.Medium);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetEmployeeLedgerAsync(
            world.EmployeeAccount.Id, world.Employee.Id, world.October.Id, null, 1, 20);

        Assert.Equal(2, dto.Entries.TotalCount);
        Assert.Equal(15m, dto.Summary.ResultPoints);
        Assert.Equal(-5m, dto.Summary.RiskPenaltyPoints);
        var line = Assert.Single(dto.Entries.Items, e => e.Id == result.Item.Id);
        Assert.Equal("RESULT", line.Type);
        Assert.Equal(10m, line.RawValue);
        Assert.Equal(1.5m, line.Multiplier);
        Assert.Equal(15m, line.Points);
        Assert.Equal("HDV", line.KpiCode);
        Assert.Equal(result.Work.Id, line.WorkId);
        Assert.Equal("RISK_PENALTY", Assert.Single(dto.Entries.Items, e => e.Id == risk.Id).Type);
    }

    [Fact]
    public async Task Ledger_FilteredByKpi_KeepsOnlyThatKpisResults()
    {
        using var world = await World.CreateAsync();
        var hdv = world.AddMatchedResult(world.Employee, 10m, 1m);
        world.AddMatchedResult(world.Employee, 4m, 1m, world.KpiKh);
        world.AddRisk(world.Employee, RiskEventSeverity.High);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetEmployeeLedgerAsync(
            world.EmployeeAccount.Id, world.Employee.Id, world.October.Id, world.KpiHdv.Id, 1, 20);

        Assert.Equal(hdv.Item.Id, Assert.Single(dto.Entries.Items).Id);
        Assert.Equal(0m, dto.Summary.RiskPenaltyPoints);
    }

    [Fact]
    public async Task Ledger_LeavesOutOtherPeriodsInvalidResultsAndDismissedRisks()
    {
        using var world = await World.CreateAsync();
        var outside = world.AddMatchedResult(world.Employee, 1m, 1m, date: new DateOnly(2026, 9, 10));
        var invalid = world.AddMatchedResult(world.Employee, 2m, 1m);
        invalid.Result.Status = VerifiedResultStatus.Invalid;
        var dismissed = world.AddRisk(world.Employee, RiskEventSeverity.Low);
        dismissed.Status = RiskEventStatus.Dismissed;
        var other = world.AddMatchedResult(world.OtherEmployee, 3m, 1m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetEmployeeLedgerAsync(
            world.EmployeeAccount.Id, world.Employee.Id, world.October.Id, null, 1, 20);

        Assert.Empty(dto.Entries.Items);
        Assert.NotNull(outside);
        Assert.NotNull(other);
    }

    [Fact]
    public async Task Ledger_IsPagedNewestFirst()
    {
        using var world = await World.CreateAsync();
        var first = world.AddMatchedResult(world.Employee, 1m, 1m);
        first.Result.VerifiedAt = Now.AddDays(-2);
        var second = world.AddMatchedResult(world.Employee, 2m, 1m);
        second.Result.VerifiedAt = Now.AddDays(-1);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetEmployeeLedgerAsync(
            world.EmployeeAccount.Id, world.Employee.Id, world.October.Id, null, 2, 1);

        Assert.Equal(2, dto.Entries.TotalCount);
        Assert.Equal(first.Item.Id, Assert.Single(dto.Entries.Items).Id);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Ledger_RejectsAnInvalidPage(int page, int pageSize)
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ValidationException>(() => world.Service(PermissionScopes.Own).GetEmployeeLedgerAsync(
            world.EmployeeAccount.Id, world.Employee.Id, world.October.Id, null, page, pageSize));
    }

    [Fact]
    public async Task Ledger_RejectsADayPeriodAndAnUnknownPeriodOrEmployee()
    {
        using var world = await World.CreateAsync();
        var service = world.Service(PermissionScopes.Dept);

        await Assert.ThrowsAsync<ValidationException>(() => service.GetEmployeeLedgerAsync(
            world.ManagerAccount.Id, world.Employee.Id, world.DayPeriod.Id, null, 1, 20));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetEmployeeLedgerAsync(
            world.ManagerAccount.Id, world.Employee.Id, Guid.NewGuid(), null, 1, 20));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetEmployeeLedgerAsync(
            world.ManagerAccount.Id, Guid.NewGuid(), world.October.Id, null, 1, 20));
    }

    [Fact]
    public async Task Ledger_FollowsTheDashboardScope()
    {
        using var world = await World.CreateAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => world.Service(PermissionScopes.Own).GetEmployeeLedgerAsync(
            world.EmployeeAccount.Id, world.OtherEmployee.Id, world.October.Id, null, 1, 20));
        await Assert.ThrowsAsync<ForbiddenException>(() => world.Service(PermissionScopes.Dept).GetEmployeeLedgerAsync(
            world.ManagerAccount.Id, world.OtherDeptEmployee.Id, world.October.Id, null, 1, 20));
        await Assert.ThrowsAsync<ForbiddenException>(() => world.Service(null).GetEmployeeLedgerAsync(
            world.EmployeeAccount.Id, world.Employee.Id, world.October.Id, null, 1, 20));

        var dto = await world.Service(PermissionScopes.Dept).GetEmployeeLedgerAsync(
            world.ManagerAccount.Id, world.Employee.Id, world.October.Id, null, 1, 20);
        Assert.Empty(dto.Entries.Items);
        await world.Service(PermissionScopes.Branch).GetEmployeeLedgerAsync(
            world.ManagerAccount.Id, world.OtherDeptEmployee.Id, world.October.Id, null, 1, 20);
    }

    [Fact]
    public async Task Entry_ReturnsTheWorkAndTheSubmissionBehindAResult()
    {
        using var world = await World.CreateAsync();
        var result = world.AddMatchedResult(world.Employee, 10m, 1m);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetLedgerEntryAsync(world.EmployeeAccount.Id, result.Item.Id);

        Assert.Equal("RESULT", dto.Entry.Type);
        Assert.Equal(world.Employee.Id, dto.EmployeeId);
        Assert.Equal(result.Work.Code, dto.Work!.Code);
        Assert.Equal("WT", dto.Work.WorkTypeCode);
        Assert.Equal("APPROVED", dto.Submission!.Status);
        Assert.Equal("APPROVED", dto.Submission.Decision);
        Assert.Equal(world.Manager.FullName, dto.Submission.VerifierName);
        Assert.Null(dto.RiskEvent);
    }

    [Fact]
    public async Task Entry_ReturnsTheRiskEventBehindAPenalty()
    {
        using var world = await World.CreateAsync();
        var risk = world.AddRisk(world.Employee, RiskEventSeverity.High);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetLedgerEntryAsync(world.EmployeeAccount.Id, risk.Id);

        Assert.Equal("RISK_PENALTY", dto.Entry.Type);
        Assert.Equal(-10m, dto.Entry.Points);
        Assert.Equal(risk.Code, dto.RiskEvent!.Code);
        Assert.Equal("HIGH", dto.RiskEvent.Severity);
        Assert.Equal(world.Manager.FullName, dto.RecordedByName);
        Assert.Null(dto.Work);
    }

    [Fact]
    public async Task Entry_UnknownIsNotFoundAndOtherDepartmentIsForbidden()
    {
        using var world = await World.CreateAsync();
        var result = world.AddMatchedResult(world.OtherDeptEmployee, 1m, 1m);
        await world.SaveAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            world.Service(PermissionScopes.Own).GetLedgerEntryAsync(world.EmployeeAccount.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            world.Service(PermissionScopes.Dept).GetLedgerEntryAsync(world.ManagerAccount.Id, result.Item.Id));
    }

    [Fact]
    public async Task Transactions_ListTheWorksCodesWithTheirImportedData()
    {
        using var world = await World.CreateAsync();
        var result = world.AddMatchedResult(world.Employee, 10m, 1m);
        world.AddTransaction(result.Work, "TX-2", WorkTransactionStatus.NotMatched, imported: false);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetLedgerEntryTransactionsAsync(world.EmployeeAccount.Id, result.Item.Id);

        Assert.Equal(result.Work.Id, dto.WorkId);
        Assert.Equal(2, dto.Items.Count);
        var matched = Assert.Single(dto.Items, t => t.TransactionCode == "TX-1");
        Assert.True(matched.IsSource);
        Assert.Equal("MATCHED", matched.Status);
        Assert.Equal("PROD", matched.ProductCode);
        Assert.Equal(1000m, matched.Amount);
        Assert.Equal("import.xlsx", matched.ImportFileName);
        var unmatched = Assert.Single(dto.Items, t => t.TransactionCode == "TX-2");
        Assert.False(unmatched.IsSource);
        Assert.Null(unmatched.ProductCode);
    }

    [Fact]
    public async Task Transactions_OfALineWithoutAWork_AreEmpty()
    {
        using var world = await World.CreateAsync();
        var risk = world.AddRisk(world.Employee, RiskEventSeverity.Low);
        await world.SaveAsync();

        var dto = await world.Service(PermissionScopes.Own).GetLedgerEntryTransactionsAsync(world.EmployeeAccount.Id, risk.Id);

        Assert.Null(dto.WorkId);
        Assert.Empty(dto.Items);
    }

    [Fact]
    public async Task Transactions_FollowTheScopeOfTheEntrysEmployee()
    {
        using var world = await World.CreateAsync();
        var result = world.AddMatchedResult(world.OtherDeptEmployee, 1m, 1m);
        await world.SaveAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            world.Service(PermissionScopes.Dept).GetLedgerEntryTransactionsAsync(world.ManagerAccount.Id, result.Item.Id));
        var dto = await world.Service(PermissionScopes.Branch).GetLedgerEntryTransactionsAsync(world.ManagerAccount.Id, result.Item.Id);
        Assert.Single(dto.Items);
    }

    private sealed record ResultGraph(WorkResultItem Item, VerifiedWorkResult Result, Work Work);

    private sealed class World : IDisposable
    {
        public required AppDbContext Db { get; init; }
        public required Employee Employee { get; init; }
        public required Employee OtherEmployee { get; init; }
        public required Employee Manager { get; init; }
        public required Employee OtherDeptEmployee { get; init; }
        public required Account EmployeeAccount { get; init; }
        public required Account ManagerAccount { get; init; }
        public required EvaluationPeriod October { get; init; }
        public required EvaluationPeriod DayPeriod { get; init; }
        public required KpiDefinition KpiHdv { get; init; }
        public required KpiDefinition KpiKh { get; init; }
        public required WorkType WorkType { get; init; }
        public required EvaluationProfile Profile { get; init; }
        private readonly Dictionary<(Guid, Guid), TargetAssignment> _targets = [];

        public static async Task<World> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

            var branch = Dept("BR", DepartmentLevelType.Branch, null);
            var deptA = Dept("DA", DepartmentLevelType.Department, branch.Id);
            var deptB = Dept("DB", DepartmentLevelType.Department, branch.Id);
            db.Departments.AddRange(branch, deptA, deptB);

            var position = new JobPosition { Id = Guid.NewGuid(), Code = "POS", Title = "Chuyên viên", Status = RecordStatus.Active, UpdatedAt = Now };
            db.JobPositions.Add(position);

            var employee = Person("E001", deptA, position);
            var other = Person("E002", deptA, position);
            var manager = Person("M001", deptA, position);
            var otherDept = Person("E900", deptB, position);
            db.Employees.AddRange(employee, other, manager, otherDept);
            var employeeAccount = Login(employee);
            var managerAccount = Login(manager);
            db.Accounts.AddRange(employeeAccount, managerAccount);

            var group = new KpiGroup { Id = Guid.NewGuid(), Code = "KD", Name = "Kinh doanh", Status = KpiStatus.Active, UpdatedAt = Now };
            db.KpiGroups.Add(group);
            var hdv = Kpi("HDV", group);
            var kh = Kpi("KH", group);
            db.KpiDefinitions.AddRange(hdv, kh);

            var profile = new EvaluationProfile
            {
                Id = Guid.NewGuid(),
                JobPositionId = position.Id,
                Code = "EP",
                Name = "Profile",
                Version = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Status = EvaluationProfileStatus.Active,
                UpdatedAt = Now,
            };
            db.EvaluationProfiles.Add(profile);

            var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT", Name = "Work type", UpdatedAt = Now };
            db.WorkTypes.Add(workType);

            var october = Period("2026-10", PeriodType.Month, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
            var day = Period("D2026-10-10", PeriodType.Day, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10));
            db.EvaluationPeriods.AddRange(october, day);
            await db.SaveChangesAsync();

            return new World
            {
                Db = db,
                Employee = employee,
                OtherEmployee = other,
                Manager = manager,
                OtherDeptEmployee = otherDept,
                EmployeeAccount = employeeAccount,
                ManagerAccount = managerAccount,
                October = october,
                DayPeriod = day,
                KpiHdv = hdv,
                KpiKh = kh,
                WorkType = workType,
                Profile = profile,
            };
        }

        public PerformanceDashboardService Service(string? scope) =>
            new(
                Db,
                new FakePermissions(scope is null ? [] : [new UserPermissionDto("evaluation", "read", scope, "EVALUATION_READ")]),
                new FixedTime(Now));

        public Task<int> SaveAsync() => Db.SaveChangesAsync();

        public void Dispose() => Db.Dispose();

        public ResultGraph AddMatchedResult(Employee employee, decimal rawValue, decimal multiplier, KpiDefinition? kpi = null, DateOnly? date = null)
        {
            kpi ??= KpiHdv;
            if (!_targets.TryGetValue((kpi.Id, employee.Id), out var target))
            {
                target = new TargetAssignment
                {
                    Id = Guid.NewGuid(),
                    KpiDefinitionId = kpi.Id,
                    KpiDefinition = kpi,
                    EmployeeId = employee.Id,
                    EvaluationPeriodId = October.Id,
                    Version = 1,
                    TargetValue = 100m,
                    Status = TargetAssignmentStatus.Active,
                    CreatedAt = Now,
                    UpdatedAt = Now,
                };
                Db.TargetAssignments.Add(target);
                _targets[(kpi.Id, employee.Id)] = target;
            }

            var progress = new ActualProgress
            {
                Id = Guid.NewGuid(),
                TargetAssignmentId = target.Id,
                TargetAssignment = target,
                ProgressDate = date ?? new DateOnly(2026, 10, 10),
                Value = rawValue,
                Status = ActualProgressStatus.Valid,
                UpdatedAt = Now,
            };
            Db.ActualProgresses.Add(progress);

            var work = new Work
            {
                Id = Guid.NewGuid(),
                WorkTypeId = WorkType.Id,
                AssigneeEmployeeId = employee.Id,
                CreatedBy = Manager.Id,
                Code = $"W-{Guid.NewGuid():N}"[..8],
                Title = "Work",
                Status = WorkStatus.Done,
                UpdatedAt = Now,
            };
            Db.Works.Add(work);

            var submission = new ResultSubmission
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                SubmitterEmployeeId = employee.Id,
                SubmissionNo = 1,
                SubmittedAt = Now.AddHours(-2),
                Status = SubmissionStatus.Approved,
                UpdatedAt = Now,
            };
            Db.ResultSubmissions.Add(submission);
            var verification = new Verification
            {
                Id = Guid.NewGuid(),
                ResultSubmissionId = submission.Id,
                VerifierEmployeeId = Manager.Id,
                RoundNo = 1,
                Decision = VerificationDecision.Approved,
                VerifiedAt = Now.AddHours(-1),
                UpdatedAt = Now,
            };
            Db.Verifications.Add(verification);

            var transaction = AddTransaction(work, "TX-1", WorkTransactionStatus.Matched, imported: true);
            var result = new VerifiedWorkResult
            {
                Id = Guid.NewGuid(),
                VerificationId = verification.Id,
                WorkTransactionId = transaction.Id,
                VerifiedAt = Now.AddHours(-1),
                UpdatedAt = Now,
                Status = VerifiedResultStatus.Valid,
            };
            Db.VerifiedWorkResults.Add(result);
            var item = new WorkResultItem
            {
                Id = Guid.NewGuid(),
                VerifiedWorkResultId = result.Id,
                ActualProgressId = progress.Id,
                EmployeeId = employee.Id,
                RawValue = rawValue,
                MultiplierApplied = multiplier,
                Value = rawValue * multiplier,
                UpdatedAt = Now,
            };
            Db.WorkResultItems.Add(item);
            return new ResultGraph(item, result, work);
        }

        public WorkTransaction AddTransaction(Work work, string code, string status, bool imported)
        {
            ImportedTransaction? source = null;
            if (imported)
            {
                var batch = new ImportBatch
                {
                    Id = Guid.NewGuid(),
                    FileName = "import.xlsx",
                    ImportedBy = Manager.Id,
                    ImportedAt = Now.AddDays(-1),
                };
                source = new ImportedTransaction
                {
                    Id = Guid.NewGuid(),
                    ImportBatchId = batch.Id,
                    ImportBatch = batch,
                    TransactionCode = code,
                    EmployeeId = work.AssigneeEmployeeId,
                    ProductCode = "PROD",
                    TransactionDate = new DateOnly(2026, 10, 10),
                    Amount = 1000m,
                };
                Db.ImportedTransactions.Add(source);
            }

            var transaction = new WorkTransaction
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                TransactionCode = code,
                Status = status,
                ImportedTransactionId = source?.Id,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.WorkTransactions.Add(transaction);
            return transaction;
        }

        public RiskEvent AddRisk(Employee employee, RiskEventSeverity severity)
        {
            var risk = new RiskEvent
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                RecordedBy = Manager.Id,
                Code = $"RE-{Guid.NewGuid():N}"[..10],
                EventType = "PROCEDURE_VIOLATION",
                Description = "Risk event",
                OccurredAt = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
                Severity = severity,
                Status = RiskEventStatus.Confirmed,
                UpdatedAt = Now,
            };
            Db.RiskEvents.Add(risk);
            return risk;
        }

        private static Department Dept(string code, DepartmentLevelType level, Guid? parentId) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            LevelType = level,
            ParentDepartmentId = parentId,
            Status = RecordStatus.Active,
            UpdatedAt = Now,
        };

        private static Employee Person(string code, Department department, JobPosition position) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Nhân viên {code}",
            Email = $"{code.ToLowerInvariant()}@example.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };

        private static Account Login(Employee employee) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = employee.Email,
            PasswordHash = "hash",
            LastSyncAt = Now,
            UpdatedAt = Now,
        };

        private static KpiDefinition Kpi(string code, KpiGroup group) => new()
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = code,
            Name = code,
            Unit = "unit",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };

        private static EvaluationPeriod Period(string code, PeriodType type, DateOnly start, DateOnly end) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = type,
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

    private sealed class FakePermissions(IReadOnlyList<UserPermissionDto> granted) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(granted);

        public Task<bool> HasPermissionAsync(Guid id, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
