using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Submission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace G54.UnitTests;

/// <summary>GET /submissions/mine and GET /submissions/{id}/reconciliation.</summary>
public sealed class MySubmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Person
    {
        public required Employee Employee { get; init; }
        public required Account Account { get; init; }
    }

    private sealed class Scenario
    {
        public required AppDbContext Context { get; init; }
        public required Person Me { get; init; }
        public required Person Other { get; init; }
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false).Options);

    private static MySubmissionService CreateService(AppDbContext context, DateTimeOffset? now = null) =>
        new(context, new FixedTime(now ?? Now), Options.Create(new SystemParametersOptions()));

    private static async Task<Scenario> SeedAsync(AppDbContext context)
    {
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "A",
            Name = "Phòng A",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "P1",
            Title = "Giao dịch viên",
            Status = RecordStatus.Active,
            UpdatedAt = Now,
        };
        context.Departments.Add(department);
        context.JobPositions.Add(position);

        Person NewPerson(string code)
        {
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = code,
                FullName = $"Nhân viên {code}",
                Email = $"{code.ToLowerInvariant()}@bpms.com",
                DepartmentId = department.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            var account = new Account
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                Username = employee.Email,
                PasswordHash = "hash",
                LastSyncAt = Now,
                UpdatedAt = Now,
            };
            context.Employees.Add(employee);
            context.Accounts.Add(account);
            return new Person { Employee = employee, Account = account };
        }

        var me = NewPerson("NV-0412");
        var other = NewPerson("NV-0999");
        await context.SaveChangesAsync();
        return new Scenario { Context = context, Me = me, Other = other };
    }

    private static Work AddWork(Scenario s, Person assignee, string code, string? productCode = "DEPOSIT")
    {
        WorkType? type = null;
        if (productCode is not null)
        {
            type = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = $"WT-{code}",
                Name = "Tiền gửi",
                ProductCode = productCode,
                UpdatedAt = Now,
            };
            s.Context.WorkTypes.Add(type);
        }

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = $"Công việc {code}",
            AssigneeEmployeeId = assignee.Employee.Id,
            CreatedBy = assignee.Employee.Id,
            WorkTypeId = type?.Id,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        s.Context.Works.Add(work);
        return work;
    }

    private static ResultSubmission AddSubmission(
        Scenario s,
        Work work,
        Person submitter,
        int no = 1,
        SubmissionStatus status = SubmissionStatus.Submitted,
        TimeSpan? after = null)
    {
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = submitter.Employee.Id,
            SubmissionNo = no,
            Status = status,
            SubmittedAt = Now + (after ?? TimeSpan.Zero),
            UpdatedAt = Now,
        };
        s.Context.ResultSubmissions.Add(submission);
        return submission;
    }

    private static WorkTransaction AddTransaction(
        Scenario s,
        Work work,
        string code,
        string status,
        ImportedTransaction? imported = null)
    {
        var transaction = new WorkTransaction
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            TransactionCode = code,
            Status = status,
            ImportedTransactionId = imported?.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        s.Context.WorkTransactions.Add(transaction);
        return transaction;
    }

    private static ImportedTransaction NewImport(Person employee, string code, string product, decimal amount) => new()
    {
        Id = Guid.NewGuid(),
        ImportBatchId = Guid.NewGuid(),
        TransactionCode = code,
        EmployeeId = employee.Employee.Id,
        Employee = employee.Employee,
        ProductCode = product,
        TransactionDate = DateOnly.FromDateTime(Now.UtcDateTime),
        Amount = amount,
    };

    [Fact]
    public async Task GetMine_ReturnsOnlyOwnLatestVersionPerWorkNewestFirst()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var workA = AddWork(s, s.Me, "WK-A");
        var workB = AddWork(s, s.Me, "WK-B");
        var workC = AddWork(s, s.Other, "WK-C");
        AddSubmission(s, workA, s.Me, 1, SubmissionStatus.Rejected, TimeSpan.FromHours(-30));
        var latestA = AddSubmission(s, workA, s.Me, 2, after: TimeSpan.FromHours(-2));
        var latestB = AddSubmission(s, workB, s.Me, 1, after: TimeSpan.FromHours(-1));
        AddSubmission(s, workC, s.Other);
        AddSubmission(s, AddWork(s, s.Me, "WK-DRAFT"), s.Me, 1, SubmissionStatus.Draft);
        await context.SaveChangesAsync();

        var result = await CreateService(context).GetMineAsync(s.Me.Account.Id, new MySubmissionFilterDto());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([latestB.Id, latestA.Id], result.Items.Select(i => i.SubmissionId));
        Assert.Equal("#SUB-WK-B-V1", result.Items[0].SubmissionCode);
        Assert.Equal(2, result.Items[1].SubmissionNo);
        Assert.Equal("SUBMITTED", result.Items[0].Status);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task GetMine_ComputesMatchStatusAndFiltersByIt()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var none = AddWork(s, s.Me, "WK-NONE");
        var matched = AddWork(s, s.Me, "WK-OK");
        var rejected = AddWork(s, s.Me, "WK-BAD");
        var waiting = AddWork(s, s.Me, "WK-WAIT");
        foreach (var work in new[] { none, matched, rejected, waiting })
        {
            AddSubmission(s, work, s.Me);
        }

        AddTransaction(s, matched, "TXN-1", WorkTransactionStatus.Matched);
        AddTransaction(s, rejected, "TXN-2", WorkTransactionStatus.Matched);
        AddTransaction(s, rejected, "TXN-3", WorkTransactionStatus.NotMatched);
        AddTransaction(s, waiting, "TXN-4", WorkTransactionStatus.Matched);
        AddTransaction(s, waiting, "TXN-5", WorkTransactionStatus.InReview);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var all = await service.GetMineAsync(s.Me.Account.Id, new MySubmissionFilterDto { PageSize = 100 });
        string StatusOf(string code) => all.Items.Single(i => i.WorkCode == code).MatchStatus;

        Assert.Equal("NONE", StatusOf("WK-NONE"));
        Assert.Equal("MATCHED", StatusOf("WK-OK"));
        Assert.Equal("NOT_MATCHED", StatusOf("WK-BAD"));
        Assert.Equal("IN_REVIEW", StatusOf("WK-WAIT"));
        var bad = all.Items.Single(i => i.WorkCode == "WK-BAD");
        Assert.Equal(2, bad.TransactionCount);
        Assert.Equal(1, bad.MatchedTransactionCount);
        Assert.Equal("TXN-2", bad.FirstTransactionCode);

        var filtered = await service.GetMineAsync(s.Me.Account.Id, new MySubmissionFilterDto { MatchStatus = "not_matched" });
        Assert.Equal(["WK-BAD"], filtered.Items.Select(i => i.WorkCode));
    }

    [Fact]
    public async Task GetMine_InvalidMatchStatus_IsAValidationError()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(context).GetMineAsync(s.Me.Account.Id, new MySubmissionFilterDto { MatchStatus = "BOGUS" }));
    }

    [Fact]
    public async Task GetMine_PeriodFilter_KeepsOnlySubmissionsSentInsideThePeriod()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var inside = AddSubmission(s, AddWork(s, s.Me, "WK-IN"), s.Me);
        AddSubmission(s, AddWork(s, s.Me, "WK-OUT"), s.Me, after: TimeSpan.FromDays(-40));
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-10",
            Name = "Tháng 10/2026",
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            UpdatedAt = Now,
        };
        context.EvaluationPeriods.Add(period);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var result = await service.GetMineAsync(s.Me.Account.Id, new MySubmissionFilterDto { PeriodId = period.Id });

        Assert.Equal([inside.Id], result.Items.Select(i => i.SubmissionId));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetMineAsync(s.Me.Account.Id, new MySubmissionFilterDto { PeriodId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task GetMine_PageBeyondTheEnd_ReturnsNoItemsButTheTotal()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        AddSubmission(s, AddWork(s, s.Me, "WK-1"), s.Me);
        await context.SaveChangesAsync();

        var result = await CreateService(context).GetMineAsync(
            s.Me.Account.Id, new MySubmissionFilterDto { Page = 999999 });

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetMine_RejectedSubmission_ShowsTheAppealDeadlineOnlyInsideTheWindow()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var open = AddSubmission(s, AddWork(s, s.Me, "WK-OPEN"), s.Me, status: SubmissionStatus.Rejected);
        var late = AddSubmission(s, AddWork(s, s.Me, "WK-LATE"), s.Me, status: SubmissionStatus.Rejected);
        AddRejection(s, open, Now.AddHours(-17));
        AddRejection(s, late, Now.AddHours(-60));
        await context.SaveChangesAsync();

        var result = await CreateService(context).GetMineAsync(s.Me.Account.Id, new MySubmissionFilterDto());

        Assert.Equal(Now.AddHours(31), result.Items.Single(i => i.SubmissionId == open.Id).AppealDeadline);
        Assert.Null(result.Items.Single(i => i.SubmissionId == late.Id).AppealDeadline);
    }

    private static void AddRejection(Scenario s, ResultSubmission submission, DateTimeOffset at) =>
        s.Context.Verifications.Add(new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = s.Other.Employee.Id,
            RoundNo = 1,
            Decision = VerificationDecision.Rejected,
            VerifiedAt = at,
            UpdatedAt = at,
        });

    [Fact]
    public async Task GetDetail_ComparesEachFieldOfTheImportedTransaction()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var work = AddWork(s, s.Me, "WK-1018", "DEPOSIT");
        AddSubmission(s, work, s.Me, 1, SubmissionStatus.Rejected, TimeSpan.FromHours(-20));
        var submission = AddSubmission(s, work, s.Me, 2);
        // Like the real reconcile, a rejected code is NOT linked to its imported row: it is found by the code.
        context.ImportedTransactions.Add(NewImport(s.Me, "TXN-77001", "credit_card", 95_000_000m));
        context.ImportedTransactions.Add(NewImport(s.Other, "TXN-77003", "DEPOSIT", 1m));
        AddTransaction(s, work, "TXN-77001", WorkTransactionStatus.NotMatched);
        AddTransaction(s, work, "TXN-77002", WorkTransactionStatus.InReview);
        AddTransaction(s, work, "TXN-77003", WorkTransactionStatus.NotMatched);
        await context.SaveChangesAsync();

        var detail = await CreateService(context).GetDetailAsync(submission.Id, s.Me.Account.Id);

        Assert.Equal("#SUB-WK-1018-V2", detail.SubmissionCode);
        Assert.Equal("NOT_MATCHED", detail.MatchStatus);
        Assert.Equal(["TXN-77001", "TXN-77002", "TXN-77003"], detail.Transactions.Select(t => t.TransactionCode));

        var fields = detail.Transactions[0].Fields.ToDictionary(f => f.Field);
        Assert.True(fields[MySubmissionField.TransactionCode].Matched);
        Assert.True(fields[MySubmissionField.Employee].Matched);
        Assert.False(fields[MySubmissionField.Product].Matched);
        Assert.Equal("DEPOSIT", fields[MySubmissionField.Product].Declared);
        Assert.Equal("credit_card", fields[MySubmissionField.Product].Imported);
        Assert.Equal("95000000", fields[MySubmissionField.Amount].Imported);
        Assert.Null(fields[MySubmissionField.Amount].Matched);

        // Not in any import yet: nothing to compare.
        Assert.All(detail.Transactions[1].Fields, f => Assert.Null(f.Matched));

        // Imported for another employee: the employee row is the one that differs (the product matches).
        var other = detail.Transactions[2].Fields.ToDictionary(f => f.Field);
        Assert.False(other[MySubmissionField.Employee].Matched);
        Assert.Equal("NV-0999", other[MySubmissionField.Employee].Imported);
        Assert.True(other[MySubmissionField.Product].Matched);
        Assert.Equal([2, 1], detail.Versions.Select(v => v.SubmissionNo));
    }

    [Fact]
    public async Task GetDetail_AppealState_FollowsTheSubmissionAndTheWindow()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var approved = AddSubmission(s, AddWork(s, s.Me, "WK-1"), s.Me, status: SubmissionStatus.Approved);
        var available = AddSubmission(s, AddWork(s, s.Me, "WK-2"), s.Me, status: SubmissionStatus.Rejected);
        var expired = AddSubmission(s, AddWork(s, s.Me, "WK-3"), s.Me, status: SubmissionStatus.Rejected);
        var appealed = AddSubmission(s, AddWork(s, s.Me, "WK-4"), s.Me, status: SubmissionStatus.Rejected);
        AddRejection(s, available, Now.AddHours(-1));
        AddRejection(s, expired, Now.AddHours(-49));
        AddRejection(s, appealed, Now.AddHours(-1));
        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = s.Me.Employee.Id,
            ResultSubmissionId = appealed.Id,
            Reason = "Dữ liệu nhập sai, đã gửi bằng chứng.",
            Status = AppealStatus.Open,
            FiledAt = Now,
            UpdatedAt = Now,
        };
        context.Appeals.Add(appeal);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        async Task<MySubmissionAppealDto> AppealOfAsync(ResultSubmission x) =>
            (await service.GetDetailAsync(x.Id, s.Me.Account.Id)).Appeal;

        Assert.Equal("NOT_APPLICABLE", (await AppealOfAsync(approved)).State);
        var open = await AppealOfAsync(available);
        Assert.Equal("AVAILABLE", open.State);
        Assert.Equal(Now.AddHours(47), open.Deadline);
        Assert.Equal("EXPIRED", (await AppealOfAsync(expired)).State);
        var inProgress = await AppealOfAsync(appealed);
        Assert.Equal("IN_PROGRESS", inProgress.State);
        Assert.Equal(appeal.Id, inProgress.AppealId);
    }

    [Fact]
    public async Task GetDetail_SomeoneElsesSubmission_IsForbidden()
    {
        using var context = CreateContext();
        var s = await SeedAsync(context);
        var theirs = AddSubmission(s, AddWork(s, s.Other, "WK-X"), s.Other);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetDetailAsync(theirs.Id, s.Me.Account.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDetailAsync(Guid.NewGuid(), s.Me.Account.Id));
    }

    [Fact]
    public async Task GetMine_UnknownAccount_IsUnauthorized()
    {
        using var context = CreateContext();
        await SeedAsync(context);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService(context).GetMineAsync(Guid.NewGuid(), new MySubmissionFilterDto()));
    }
}
