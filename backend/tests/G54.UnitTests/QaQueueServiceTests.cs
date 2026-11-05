using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Quality;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class QaQueueServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 3, 0, 0, TimeSpan.Zero);
    private const string LongText = "Số liệu trên chứng từ không khớp với báo cáo.";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly RecordingNotifier _notifier = new();
    private Department _branch = null!;
    private Department _childDept = null!;
    private Department _otherDept = null!;
    private Employee _qa = null!;

    public void Dispose() => _db.Dispose();

    private QaQueueService Queue => new(_db);

    private QualityAssuranceService Qa => new(_db, new FixedTime(Now), _notifier, NullLogger<QualityAssuranceService>.Instance);

    private async Task SeedOrganizationAsync()
    {
        _branch = NewDepartment("BR", DepartmentLevelType.Branch, null);
        _childDept = NewDepartment("D1", DepartmentLevelType.Department, _branch.Id);
        _otherDept = NewDepartment("D2", DepartmentLevelType.Department, null);
        _db.Departments.AddRange(_branch, _childDept, _otherDept);
        await _db.SaveChangesAsync();
        _qa = await AddEmployeeAsync("QA-USER", _otherDept);
    }

    private static Department NewDepartment(string code, DepartmentLevelType level, Guid? parent) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = $"Phòng {code}",
        LevelType = level,
        ParentDepartmentId = parent,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private async Task<Employee> AddEmployeeAsync(string code, Department department)
    {
        var job = await _db.JobPositions.FirstOrDefaultAsync();
        if (job is null)
        {
            job = new JobPosition { Id = Guid.NewGuid(), Code = "JP", Title = "Teller", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
            _db.JobPositions.Add(job);
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = department.Id,
            JobPositionId = job.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });
        await _db.SaveChangesAsync();
        return employee;
    }

    private async Task<(Employee Maker, ResultSubmission Submission)> AddApprovedAsync(string makerCode, Department department)
    {
        var maker = await AddEmployeeAsync(makerCode, department);
        var verifier = await AddEmployeeAsync($"V-{makerCode}", department);
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"WK-{makerCode}",
            Title = $"Công việc {makerCode}",
            AssigneeEmployeeId = maker.Id,
            CreatedBy = maker.Id,
            Status = WorkStatus.Verified,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = maker.Id,
            SubmissionNo = 1,
            Comment = "Đã hoàn thành.",
            Status = SubmissionStatus.Approved,
            SubmittedAt = Now,
            UpdatedAt = Now,
        };
        var verification = new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = verifier.Id,
            RoundNo = 1,
            Decision = VerificationDecision.Approved,
            VerifiedAt = Now,
            UpdatedAt = Now,
        };
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);
        _db.Verifications.Add(verification);
        _db.VerifiedWorkResults.Add(new VerifiedWorkResult { Id = Guid.NewGuid(), VerificationId = verification.Id, VerifiedAt = Now, UpdatedAt = Now, Status = VerifiedResultStatus.Valid });
        await _db.SaveChangesAsync();
        return (maker, submission);
    }

    private async Task<QaSample> AddSampleAsync(ResultSubmission submission, long codeNo, DateTimeOffset? at = null)
    {
        var sample = new QaSample
        {
            Id = Guid.NewGuid(),
            CodeNo = codeNo,
            ResultSubmissionId = submission.Id,
            SubmitterEmployeeId = submission.SubmitterEmployeeId,
            Status = QaSampleStatus.Pending,
            SampledAt = at ?? Now,
        };
        _db.QaSamples.Add(sample);
        await _db.SaveChangesAsync();
        return sample;
    }

    private async Task<QualityFlagDto> AddFlagAsync(ResultSubmission submission) =>
        await Qa.CreateFlagAsync(submission.Id, _qa.Id, new CreateQualityFlagDto(LongText, "HIGH"));

    [Fact]
    public async Task Queue_ListsFlagsAndSamplesTogether_WithTheCountOfEveryTab()
    {
        await SeedOrganizationAsync();
        var (_, first) = await AddApprovedAsync("A1", _childDept);
        var (_, second) = await AddApprovedAsync("A2", _childDept);
        var (_, third) = await AddApprovedAsync("A3", _childDept);
        await AddFlagAsync(first);
        await AddSampleAsync(second, 7);
        var passed = await AddSampleAsync(third, 8);
        await Qa.DecideSampleAsync(passed.Id, _qa.Id, new DecideQaSampleDto("PASSED", null, null, false));

        var pending = await Queue.GetQueueAsync(new QaQueueFilterDto());

        Assert.Equal(2, pending.TotalCount);
        Assert.Equal(new QaQueueCountsDto(2, 1, 0), pending.Counts);
        Assert.Equal(["FLAG", "SAMPLE"], pending.Items.Select(i => i.Kind).Order());
        Assert.All(pending.Items, i => Assert.Equal("PENDING", i.Tab));
        Assert.All(pending.Items, i => Assert.Equal(i.CreatedAt.AddDays(QaQueueService.ReviewDays), i.DueAt));
        var sample = pending.Items.Single(i => i.Kind == "SAMPLE");
        Assert.Equal("QA-0007", sample.Code);
        Assert.Equal("RANDOM_SAMPLE", sample.Source);
        Assert.Equal("Phòng D1", sample.DepartmentName);
        Assert.Equal("MANUAL_FLAG", pending.Items.Single(i => i.Kind == "FLAG").Source);
    }

    [Fact]
    public async Task Queue_MapsDecisionsToTheConfirmedAndRejectedTabs()
    {
        await SeedOrganizationAsync();
        var (_, dismissedSubmission) = await AddApprovedAsync("B1", _childDept);
        var (_, reworkSubmission) = await AddApprovedAsync("B2", _childDept);
        var (_, passedSubmission) = await AddApprovedAsync("B3", _childDept);
        var dismissed = await AddFlagAsync(dismissedSubmission);
        var rework = await AddFlagAsync(reworkSubmission);
        await Qa.DismissFlagAsync(dismissed.Id, _qa.Id);
        await Qa.RequestReworkAsync(rework.Id, _qa.Id, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ."));
        var passed = await AddSampleAsync(passedSubmission, 5);
        await Qa.DecideSampleAsync(passed.Id, _qa.Id, new DecideQaSampleDto("PASSED", null, null, false));

        var confirmed = await Queue.GetQueueAsync(new QaQueueFilterDto { Tab = "confirmed" });
        var rejected = await Queue.GetQueueAsync(new QaQueueFilterDto { Tab = "REJECTED" });

        Assert.Equal(["DISMISSED", "PASSED"], confirmed.Items.Select(i => i.Status).Order());
        Assert.Equal("REWORK_REQUESTED", Assert.Single(rejected.Items).Status);
        Assert.Equal(new QaQueueCountsDto(0, 2, 1), rejected.Counts);
    }

    [Fact]
    public async Task Queue_ShowsAFailedSampleOnlyThroughItsFlag()
    {
        await SeedOrganizationAsync();
        var (_, submission) = await AddApprovedAsync("C1", _childDept);
        var sample = await AddSampleAsync(submission, 11);
        await Qa.DecideSampleAsync(sample.Id, _qa.Id, new DecideQaSampleDto("FAILED", LongText, null, false));

        var queue = await Queue.GetQueueAsync(new QaQueueFilterDto());

        var item = Assert.Single(queue.Items);
        Assert.Equal("FLAG", item.Kind);
        Assert.Equal("SAMPLE_FAILED", item.Source);
        Assert.Equal("SAMPLE", item.Type);
        Assert.Equal("QA-0011", item.Code);
        Assert.Single((await Queue.GetQueueAsync(new QaQueueFilterDto { Kind = "SAMPLE" })).Items);
        Assert.Empty((await Queue.GetQueueAsync(new QaQueueFilterDto { Kind = "FLAG" })).Items);
        Assert.Equal(1, queue.Counts.Pending);
    }

    [Fact]
    public async Task Queue_FiltersByKindDepartmentSubtreePeriodAndSearch()
    {
        await SeedOrganizationAsync();
        var (_, inBranch) = await AddApprovedAsync("D1X", _childDept);
        var (_, elsewhere) = await AddApprovedAsync("D2X", _otherDept);
        await AddSampleAsync(inBranch, 21, new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero));
        await AddSampleAsync(elsewhere, 22, new DateTimeOffset(2026, 9, 10, 3, 0, 0, TimeSpan.Zero));
        await AddFlagAsync(inBranch);
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-10",
            Name = "10/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(period);
        await _db.SaveChangesAsync();

        var all = await Queue.GetQueueAsync(new QaQueueFilterDto());
        var samples = await Queue.GetQueueAsync(new QaQueueFilterDto { Kind = "sample" });
        var branch = await Queue.GetQueueAsync(new QaQueueFilterDto { DepartmentId = _branch.Id });
        var october = await Queue.GetQueueAsync(new QaQueueFilterDto { PeriodId = period.Id });
        var byCode = await Queue.GetQueueAsync(new QaQueueFilterDto { Search = "QA-0022" });
        var byNumber = await Queue.GetQueueAsync(new QaQueueFilterDto { Search = "21" });
        var byEmployee = await Queue.GetQueueAsync(new QaQueueFilterDto { Search = "d2x" });

        Assert.Equal(3, all.TotalCount);
        Assert.All(samples.Items, i => Assert.Equal("SAMPLE", i.Kind));
        Assert.Equal(2, samples.TotalCount);
        Assert.Equal(2, branch.TotalCount);
        Assert.All(branch.Items, i => Assert.Equal("Phòng D1", i.DepartmentName));
        Assert.Equal(2, october.TotalCount);
        Assert.DoesNotContain(october.Items, i => i.EmployeeCode == "D2X");
        Assert.Equal("D2X", Assert.Single(byCode.Items).EmployeeCode);
        Assert.Equal("D1X", Assert.Single(byNumber.Items).EmployeeCode);
        Assert.Equal("D2X", Assert.Single(byEmployee.Items).EmployeeCode);
    }

    [Fact]
    public async Task Queue_PagesTheMergedList_AndOrdersPendingByDueDate()
    {
        await SeedOrganizationAsync();
        for (var i = 1; i <= 3; i++)
        {
            var (_, submission) = await AddApprovedAsync($"E{i}", _childDept);
            await AddSampleAsync(submission, 30 + i, Now.AddDays(i));
        }

        var first = await Queue.GetQueueAsync(new QaQueueFilterDto { PageSize = 2 });
        var second = await Queue.GetQueueAsync(new QaQueueFilterDto { PageSize = 2, Page = 2 });

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(["QA-0031", "QA-0032"], first.Items.Select(i => i.Code));
        Assert.Equal("QA-0033", Assert.Single(second.Items).Code);
    }

    [Theory]
    [InlineData("NOPE", null)]
    [InlineData(null, "NOPE")]
    public async Task Queue_RejectsAnUnknownTabOrKind(string? tab, string? kind)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Queue.GetQueueAsync(new QaQueueFilterDto { Tab = tab, Kind = kind }));
    }

    [Fact]
    public async Task Queue_WithAnUnknownPeriod_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Queue.GetQueueAsync(new QaQueueFilterDto { PeriodId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Detail_OfASample_ReturnsTheSubmissionEvidenceAndMatchedTransactions()
    {
        await SeedOrganizationAsync();
        var (maker, submission) = await AddApprovedAsync("F1", _childDept);
        var sample = await AddSampleAsync(submission, 41);
        _db.Evidences.Add(new Evidence
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            FileName = "phieu_mo_the.pdf",
            FilePath = "x/phieu_mo_the.pdf",
            FileSize = 2048,
            MimeType = "application/pdf",
            UploadedAt = Now,
            UpdatedAt = Now,
        });
        var batch = new ImportBatch { Id = Guid.NewGuid(), FileName = "IMP-1024.xlsx", ImportedAt = Now };
        var imported = new ImportedTransaction
        {
            Id = Guid.NewGuid(),
            ImportBatchId = batch.Id,
            TransactionCode = "TX-1",
            EmployeeId = maker.Id,
            ProductCode = "THE",
            TransactionDate = new DateOnly(2026, 10, 20),
            Amount = 5m,
        };
        _db.ImportBatches.Add(batch);
        _db.ImportedTransactions.Add(imported);
        _db.WorkTransactions.Add(new WorkTransaction
        {
            Id = Guid.NewGuid(),
            WorkId = submission.WorkId,
            TransactionCode = "TX-1",
            Status = WorkTransactionStatus.Matched,
            ImportedTransactionId = imported.Id,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();

        var detail = await Queue.GetItemAsync("sample", sample.Id);

        Assert.Equal("SAMPLE", detail.Item.Kind);
        Assert.Equal("Đã hoàn thành.", detail.SubmissionComment);
        Assert.Equal("Teller", detail.JobPositionTitle);
        var evidence = Assert.Single(detail.Evidences);
        Assert.Equal("phieu_mo_the.pdf", evidence.FileName);
        var transaction = Assert.Single(detail.Transactions);
        Assert.Equal("MATCHED", transaction.Status);
        Assert.Equal("IMP-1024.xlsx", transaction.ImportBatchFileName);
        Assert.Equal("THE", transaction.ProductCode);
    }

    [Fact]
    public async Task Detail_OfAReworkedFlag_ReturnsItsReasonAndReworkComment()
    {
        await SeedOrganizationAsync();
        var (_, submission) = await AddApprovedAsync("G1", _childDept);
        var flag = await AddFlagAsync(submission);
        await Qa.RequestReworkAsync(flag.Id, _qa.Id, new RequestReworkDto("Làm lại báo cáo và đính kèm chứng từ."));

        var detail = await Queue.GetItemAsync("FLAG", flag.Id);

        Assert.Equal(LongText, detail.Reason);
        Assert.Equal("Làm lại báo cáo và đính kèm chứng từ.", detail.ReworkComment);
        Assert.NotNull(detail.ReviewedAt);
        Assert.Equal("REJECTED", detail.Item.Tab);
    }

    [Fact]
    public async Task Detail_ForAnUnknownItemOrKind_Fails()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Queue.GetItemAsync("FLAG", Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => Queue.GetItemAsync("SAMPLE", Guid.NewGuid()));
        await Assert.ThrowsAsync<ValidationException>(() => Queue.GetItemAsync("OTHER", Guid.NewGuid()));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
