using ClosedXML.Excel;
using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Reconciliation;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

/// <summary>The reconciliation screens: scope, filters, counts, field comparison, actions and export (InMemory).</summary>
public sealed class ReconciliationServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);

    private static readonly UserPermissionDto[] QaGrants =
    [
        new("reconciliation", "read", "All", "RECONCILIATION_READ"),
        new("quality_flag", "create", "All", "QUALITY_FLAG_CREATE"),
        new("risk_event", "create", "All", "RISK_EVENT_CREATE"),
    ];

    private static readonly UserPermissionDto[] ManagerGrants =
    [
        new("reconciliation", "read", "Dept", "RECONCILIATION_READ"),
    ];

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private Department _branch = null!;
    private Department _sales = null!;
    private Department _other = null!;
    private JobPosition _teller = null!;
    private WorkType _cardType = null!;
    private WorkType _depositType = null!;
    private Employee _qa = null!;
    private Employee _manager = null!;
    private Employee _anh = null!;
    private Employee _bao = null!;
    private ImportBatch _batch = null!;

    public void Dispose() => _db.Dispose();

    private ReconciliationService Service(params UserPermissionDto[] grants) => Service(Now, grants);

    private ReconciliationService Service(DateTimeOffset now, params UserPermissionDto[] grants) => new(
        _db,
        new FixedTime(now),
        new FakePermissionService(null, grants),
        Options.Create(new SystemParametersOptions()));

    private async Task SeedAsync()
    {
        _branch = NewDepartment("BR", DepartmentLevelType.Branch, null);
        _sales = NewDepartment("D1", DepartmentLevelType.Department, _branch.Id);
        _other = NewDepartment("D2", DepartmentLevelType.Department, null);
        _db.Departments.AddRange(_branch, _sales, _other);
        _teller = new JobPosition { Id = Guid.NewGuid(), Code = "TELLER", Title = "Teller", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        _db.JobPositions.Add(_teller);
        _cardType = new WorkType { Id = Guid.NewGuid(), Code = "CARD", Name = "Thẻ ghi nợ", ProductCode = "DEBIT_CARD", Status = RecordStatus.Active, UpdatedAt = Now };
        _depositType = new WorkType { Id = Guid.NewGuid(), Code = "DEP", Name = "Tiền gửi", ProductCode = "TERM_DEPOSIT", Status = RecordStatus.Active, UpdatedAt = Now };
        _db.WorkTypes.AddRange(_cardType, _depositType);
        await _db.SaveChangesAsync();

        _qa = await AddEmployeeAsync("QA1", _other);
        _manager = await AddEmployeeAsync("MGR1", _sales);
        _anh = await AddEmployeeAsync("NV0231", _sales);
        _bao = await AddEmployeeAsync("NV0310", _other);

        _batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            FileName = "06_Giao_dich.xlsx",
            TotalRows = 5,
            ImportedRows = 5,
            ImportedBy = _qa.Id,
            ImportedAt = new DateTimeOffset(2026, 10, 20, 11, 0, 0, TimeSpan.Zero),
        };
        _db.ImportBatches.Add(_batch);
        await _db.SaveChangesAsync();
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
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = department.Id,
            JobPositionId = _teller.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });
        await _db.SaveChangesAsync();
        return employee;
    }

    private async Task<Work> AddWorkAsync(string code, Employee assignee, WorkType type, string? cif = null)
    {
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = $"Việc {code}",
            AssigneeEmployeeId = assignee.Id,
            CreatedBy = _manager.Id,
            WorkTypeId = type.Id,
            CustomerCif = cif,
            Status = WorkStatus.Submitted,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
            AssignedAt = Now,
        };
        _db.Works.Add(work);
        await _db.SaveChangesAsync();
        return work;
    }

    private async Task<WorkTransaction> AddCodeAsync(
        Work work,
        string code,
        string status,
        string? reason = null,
        DateTimeOffset? createdAt = null)
    {
        var tx = new WorkTransaction
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            TransactionCode = code,
            Status = status,
            Reason = reason,
            CreatedAt = createdAt ?? Now,
            UpdatedAt = createdAt ?? Now,
        };
        _db.WorkTransactions.Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    private async Task<ImportedTransaction> AddImportedAsync(
        string code,
        Employee employee,
        string product,
        decimal amount,
        string? customer = null,
        string? refer = null,
        DateOnly? date = null)
    {
        var row = new ImportedTransaction
        {
            Id = Guid.NewGuid(),
            ImportBatchId = _batch.Id,
            TransactionCode = code,
            CustomerCode = customer,
            EmployeeId = employee.Id,
            ProductCode = product,
            TransactionDate = date ?? new DateOnly(2026, 10, 21),
            Amount = amount,
            ReferEmployeeCode = refer,
        };
        _db.ImportedTransactions.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// Anh (sales): one matched, one product mismatch, one waiting for the file. Bao (other department): one employee mismatch.
    /// </summary>
    private async Task<(WorkTransaction Matched, WorkTransaction Mismatch, WorkTransaction Waiting, WorkTransaction OtherDepartment)> SeedCodesAsync()
    {
        await SeedAsync();
        var card = await AddWorkAsync("WK-1", _anh, _cardType);
        var deposit = await AddWorkAsync("WK-2", _anh, _depositType, "CIF-77");
        var other = await AddWorkAsync("WK-3", _bao, _cardType);

        await AddImportedAsync("TX-OK", _anh, "DEBIT_CARD", 1_500_000m);
        await AddImportedAsync("TX-PRODUCT", _anh, "DEBIT_CARD", 95_000_000m, "CIF-77");
        await AddImportedAsync("TX-EMP", _anh, "DEBIT_CARD", 200_000m);

        var matched = await AddCodeAsync(card, "TX-OK", WorkTransactionStatus.Matched, null, Now.AddDays(-2));
        var mismatch = await AddCodeAsync(deposit, "TX-PRODUCT", WorkTransactionStatus.NotMatched, "PRODUCT_MISMATCH", Now.AddDays(-1));
        var waiting = await AddCodeAsync(deposit, "TX-WAIT", WorkTransactionStatus.InReview, null, Now.AddHours(-5));
        var otherDepartment = await AddCodeAsync(other, "TX-EMP", WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", Now.AddHours(-3));
        return (matched, mismatch, waiting, otherDepartment);
    }

    // ----- Scope ------------------------------------------------------------------------------

    [Fact]
    public async Task List_AsQa_SeesEveryDepartmentWithTheCountsOfAllStatuses()
    {
        await SeedCodesAsync();

        var list = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto());

        Assert.Equal(4, list.TotalCount);
        Assert.Equal(new ReconciliationCountsDto(4, 2, 1, 1), list.Counts);
        Assert.Equal(["TX-EMP", "TX-WAIT", "TX-PRODUCT", "TX-OK"], list.Items.Select(i => i.TransactionCode));
        Assert.Equal(2, list.Options.Departments.Count);
        Assert.Contains(list.Options.Products, p => p.Code == "DEBIT_CARD" && p.Name == "Thẻ ghi nợ");
    }

    [Fact]
    public async Task List_AsDepartmentManager_OnlySeesTheirOwnDepartment()
    {
        await SeedCodesAsync();

        var list = await Service(ManagerGrants).ListAsync(_manager.Id, new ReconciliationFilterDto());

        Assert.Equal(["TX-WAIT", "TX-PRODUCT", "TX-OK"], list.Items.Select(i => i.TransactionCode));
        Assert.Equal(3, list.Counts.All);
        Assert.Equal([_sales.Id], list.Options.Departments.Select(d => d.Id));
    }

    [Fact]
    public async Task List_AsBranchManager_SeesTheWholeBranchButNotAnotherOne()
    {
        await SeedCodesAsync();
        var branchGrant = new UserPermissionDto("reconciliation", "read", "Branch", "RECONCILIATION_READ");

        var list = await Service(branchGrant).ListAsync(_manager.Id, new ReconciliationFilterDto());

        Assert.Equal(3, list.TotalCount);
        Assert.DoesNotContain(list.Items, i => i.EmployeeCode == "NV0310");
    }

    [Fact]
    public async Task List_WithoutThePermission_IsForbidden()
    {
        await SeedCodesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service().ListAsync(_anh.Id, new ReconciliationFilterDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(new UserPermissionDto("reconciliation", "read", "Own", "RECONCILIATION_READ")).ListAsync(_anh.Id, new ReconciliationFilterDto()));
    }

    // ----- Filters ----------------------------------------------------------------------------

    [Fact]
    public async Task List_StatusFilter_NarrowsTheRowsButNotTheCounts()
    {
        await SeedCodesAsync();

        var list = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(Status: "not_matched"));

        Assert.Equal(2, list.TotalCount);
        Assert.All(list.Items, i => Assert.Equal(WorkTransactionStatus.NotMatched, i.Status));
        Assert.Equal(4, list.Counts.All);
    }

    [Fact]
    public async Task List_ReasonFilter_TreatsAWaitingCodeAsNoImportedTransaction()
    {
        await SeedCodesAsync();
        var service = Service(QaGrants);

        var waiting = await service.ListAsync(_qa.Id, new ReconciliationFilterDto(Reason: "NO_IMPORTED_TRANSACTION"));
        var product = await service.ListAsync(_qa.Id, new ReconciliationFilterDto(Reason: "product_mismatch"));

        Assert.Equal("TX-WAIT", Assert.Single(waiting.Items).TransactionCode);
        Assert.Equal("NO_IMPORTED_TRANSACTION", waiting.Items[0].Reason);
        Assert.Equal("TX-PRODUCT", Assert.Single(product.Items).TransactionCode);
    }

    [Fact]
    public async Task List_UnknownStatusOrReason_IsAValidationError()
    {
        await SeedCodesAsync();
        var service = Service(QaGrants);

        await Assert.ThrowsAsync<ValidationException>(() => service.ListAsync(_qa.Id, new ReconciliationFilterDto(Status: "DONE")));
        await Assert.ThrowsAsync<ValidationException>(() => service.ListAsync(_qa.Id, new ReconciliationFilterDto(Reason: "AMOUNT_MISMATCH")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(_qa.Id, new ReconciliationFilterDto(From: new DateOnly(2026, 10, 5), To: new DateOnly(2026, 10, 1))));
    }

    [Fact]
    public async Task List_ProductFilter_UsesTheImportedProductAndFallsBackToTheWorkType()
    {
        await SeedCodesAsync();

        var card = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(ProductCode: "DEBIT_CARD"));
        var deposit = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(ProductCode: "TERM_DEPOSIT"));

        // TX-PRODUCT is a term-deposit work whose import says DEBIT_CARD: the file wins. TX-WAIT has no import yet.
        Assert.Equal(["TX-EMP", "TX-PRODUCT", "TX-OK"], card.Items.Select(i => i.TransactionCode));
        Assert.Equal("TX-WAIT", Assert.Single(deposit.Items).TransactionCode);
        Assert.Equal("TERM_DEPOSIT", deposit.Items[0].ProductCode);
    }

    [Fact]
    public async Task List_DateRange_FollowsTheVietnameseBusinessDay()
    {
        await SeedCodesAsync();
        var inBusinessDay = await AddWorkAsync("WK-9", _anh, _cardType);
        // 18:30 UTC on the 24th is 01:30 on the 25th in Vietnam.
        await AddCodeAsync(inBusinessDay, "TX-LATE", WorkTransactionStatus.InReview, null, new DateTimeOffset(2026, 10, 24, 18, 30, 0, TimeSpan.Zero));

        var onTheDay = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(From: new DateOnly(2026, 10, 25), To: new DateOnly(2026, 10, 25)));
        var theDayBefore = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(From: new DateOnly(2026, 10, 24), To: new DateOnly(2026, 10, 24)));

        // TX-PRODUCT was added at 10:00 and TX-LATE at 01:30 Vietnam time on the 25th.
        Assert.Equal(["TX-PRODUCT", "TX-LATE"], onTheDay.Items.Select(i => i.TransactionCode));
        Assert.DoesNotContain(theDayBefore.Items, i => i.TransactionCode == "TX-LATE");
    }

    [Fact]
    public async Task List_Period_SetsTheDefaultRange()
    {
        await SeedCodesAsync();
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-10",
            Name = "10/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 10, 26),
            EndDate = new DateOnly(2026, 10, 31),
            Status = PeriodStatus.Open,
        };
        _db.EvaluationPeriods.Add(period);
        await _db.SaveChangesAsync();

        var list = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(PeriodId: period.Id));

        Assert.Equal(["TX-EMP", "TX-WAIT"], list.Items.Select(i => i.TransactionCode));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(PeriodId: Guid.NewGuid())));
    }

    [Fact]
    public async Task List_Paging_ReturnsTheRequestedPageAndAnEmptyOneBeyondTheEnd()
    {
        await SeedCodesAsync();
        var service = Service(QaGrants);

        var second = await service.ListAsync(_qa.Id, new ReconciliationFilterDto(Page: 2, PageSize: 3));
        var beyond = await service.ListAsync(_qa.Id, new ReconciliationFilterDto(Page: int.MaxValue, PageSize: 3));

        Assert.Equal("TX-OK", Assert.Single(second.Items).TransactionCode);
        Assert.Equal(4, second.TotalCount);
        Assert.Empty(beyond.Items);
        Assert.Equal(4, beyond.TotalCount);
    }

    // ----- Rows -------------------------------------------------------------------------------

    [Fact]
    public async Task List_ShowsTheImportedValuesEvenForACodeThatDidNotMatch()
    {
        var seeded = await SeedCodesAsync();

        var list = await Service(QaGrants).ListAsync(_qa.Id, new ReconciliationFilterDto(Status: "NOT_MATCHED"));
        var mismatch = list.Items.Single(i => i.Id == seeded.Mismatch.Id);

        // The code is not linked to its import row (NOT_MATCHED), yet the screen still shows what the file holds.
        Assert.Equal("DEBIT_CARD", mismatch.ProductCode);
        Assert.Equal(95_000_000m, mismatch.Amount);
        Assert.Equal("CIF-77", mismatch.CustomerCif);
        Assert.Equal(new DateOnly(2026, 10, 21), mismatch.TransactionDate);
        Assert.Equal("PRODUCT_MISMATCH", mismatch.Reason);
    }

    // ----- Detail -----------------------------------------------------------------------------

    [Fact]
    public async Task Detail_ComparesEveryFieldAgainstTheImportedRow()
    {
        var seeded = await SeedCodesAsync();

        var detail = await Service(QaGrants).GetDetailAsync(_qa.Id, seeded.Mismatch.Id);

        var fields = detail.Fields.ToDictionary(f => f.Field);
        Assert.True(fields["TRANSACTION_CODE"].Matched);
        Assert.True(fields["CUSTOMER"].Matched);
        Assert.True(fields["EMPLOYEE"].Matched);
        Assert.False(fields["PRODUCT"].Matched);
        Assert.Equal("TERM_DEPOSIT", fields["PRODUCT"].Declared);
        Assert.Equal("DEBIT_CARD", fields["PRODUCT"].Imported);
        Assert.Null(fields["REFERRAL"].Matched);
        Assert.Null(fields["AMOUNT"].Matched);
        Assert.Equal("95000000", fields["AMOUNT"].Imported);
        Assert.DoesNotContain(detail.Fields, f => f.Field == "TRANSACTION_DATE");
        Assert.Equal(4, detail.ComparedFields);
        Assert.Equal(3, detail.MatchedFields);
        Assert.Equal("06_Giao_dich.xlsx", detail.Import!.FileName);
        Assert.Equal("Name QA1", detail.Import.ImportedByName);
        Assert.Equal(["TX-PRODUCT", "TX-WAIT"], detail.Siblings.Select(s => s.TransactionCode));
    }

    [Fact]
    public async Task Detail_OfACodeWaitingForTheFile_HasNothingToCompare()
    {
        var seeded = await SeedCodesAsync();

        var detail = await Service(QaGrants).GetDetailAsync(_qa.Id, seeded.Waiting.Id);

        Assert.Null(detail.Import);
        Assert.Equal(0, detail.ComparedFields);
        Assert.All(detail.Fields, f => Assert.Null(f.Matched));
        Assert.Equal("NO_IMPORTED_TRANSACTION", detail.Item.Reason);
        Assert.Contains(detail.History, h => h.Kind == "CODE_ADDED");
        Assert.DoesNotContain(detail.History, h => h.Kind == "IMPORTED");
    }

    [Fact]
    public async Task Detail_ChecksTheReferrerAndTheDateAgainstTheAcceptedHandover()
    {
        await SeedAsync();
        var work = await AddWorkAsync("WK-H", _anh, _cardType);
        var tx = await AddCodeAsync(work, "TX-H", WorkTransactionStatus.NotMatched, "REFERRER_MISMATCH");
        await AddImportedAsync("TX-H", _anh, "DEBIT_CARD", 1m, null, "NV9999", new DateOnly(2026, 10, 10));
        _db.TaskHandovers.Add(new TaskHandover
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            FromEmployeeId = _bao.Id,
            ToEmployeeId = _anh.Id,
            Reason = "Khách hẹn gặp chuyên viên.",
            Status = HandoverStatus.Accepted,
            HandoverAt = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero),
            AcceptedAt = new DateTimeOffset(2026, 10, 15, 4, 0, 0, TimeSpan.Zero),
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();

        var fields = (await Service(QaGrants).GetDetailAsync(_qa.Id, tx.Id)).Fields.ToDictionary(f => f.Field);

        Assert.Equal("NV0310", fields["REFERRAL"].Declared);
        Assert.Equal("NV9999", fields["REFERRAL"].Imported);
        Assert.False(fields["REFERRAL"].Matched);
        Assert.Equal("2026-10-15", fields["TRANSACTION_DATE"].Declared);
        Assert.False(fields["TRANSACTION_DATE"].Matched);
    }

    [Fact]
    public async Task Detail_OutsideTheCallersScope_LooksLikeAMissingCode()
    {
        var seeded = await SeedCodesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => Service(ManagerGrants).GetDetailAsync(_manager.Id, seeded.OtherDepartment.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(QaGrants).GetDetailAsync(_qa.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Detail_Actions_ExplainWhyAQualityFlagIsNotPossible()
    {
        var seeded = await SeedCodesAsync();
        var service = Service(QaGrants);

        var none = (await service.GetDetailAsync(_qa.Id, seeded.Mismatch.Id)).Actions;
        Assert.False(none.CanFlag);
        Assert.Equal("NO_APPROVED_SUBMISSION", none.FlagBlockedReason);
        Assert.True(none.CanRecordRisk);

        var submission = AddSubmission(seeded.Mismatch.WorkId, _anh, 1, SubmissionStatus.Approved);
        await _db.SaveChangesAsync();
        var ready = (await service.GetDetailAsync(_qa.Id, seeded.Mismatch.Id)).Actions;
        Assert.True(ready.CanFlag);
        Assert.Equal(submission.Id, ready.FlagSubmissionId);
        Assert.Null(ready.FlagBlockedReason);

        _db.QualityFlags.Add(new QualityFlag
        {
            Id = Guid.NewGuid(),
            CodeNo = 1,
            ResultSubmissionId = submission.Id,
            EmployeeId = _anh.Id,
            FlaggedByEmployeeId = _qa.Id,
            Severity = QualityFlagSeverity.Medium,
            Status = QualityFlagStatus.Open,
            Reason = "Chứng từ không khớp.",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();
        var flagged = await service.GetDetailAsync(_qa.Id, seeded.Mismatch.Id);
        Assert.Equal("OPEN_FLAG", flagged.Actions.FlagBlockedReason);
        Assert.Contains(flagged.History, h => h.Kind == "QUALITY_FLAG" && h.Reference == "QA-0001");

        var viewOnly = (await Service(ManagerGrants).GetDetailAsync(_manager.Id, seeded.Mismatch.Id)).Actions;
        Assert.False(viewOnly.CanFlag);
        Assert.Equal("NO_PERMISSION", viewOnly.FlagBlockedReason);
        Assert.False(viewOnly.CanRecordRisk);
    }

    [Fact]
    public async Task Detail_Actions_ANonOwnerQaCannotFlagTheirOwnWorkOrRecordRiskAgainstThemselves()
    {
        await SeedAsync();
        var own = await AddWorkAsync("WK-OWN", _qa, _cardType);
        var tx = await AddCodeAsync(own, "TX-OWN", WorkTransactionStatus.InReview);
        AddSubmission(own.Id, _qa, 1, SubmissionStatus.Approved);
        await _db.SaveChangesAsync();

        var actions = (await Service(QaGrants).GetDetailAsync(_qa.Id, tx.Id)).Actions;

        Assert.False(actions.CanFlag);
        Assert.Equal("OWN_WORK", actions.FlagBlockedReason);
        Assert.False(actions.CanRecordRisk);
    }

    [Fact]
    public async Task Detail_Appeal_FollowsTheWindowOfTheRejectedSubmission()
    {
        var seeded = await SeedCodesAsync();
        var rejectedAt = Now.AddHours(-10);
        var submission = AddSubmission(seeded.Mismatch.WorkId, _anh, 1, SubmissionStatus.Rejected);
        _db.Verifications.Add(new Verification
        {
            Id = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
            VerifierEmployeeId = _manager.Id,
            RoundNo = 1,
            Decision = VerificationDecision.Rejected,
            VerifiedAt = rejectedAt,
            UpdatedAt = rejectedAt,
        });
        await _db.SaveChangesAsync();

        var available = (await Service(QaGrants).GetDetailAsync(_qa.Id, seeded.Mismatch.Id)).Appeal;
        Assert.Equal("AVAILABLE", available.State);
        Assert.Equal(rejectedAt.AddHours(48), available.Deadline);
        Assert.Equal(submission.Id, available.SubmissionId);

        var expired = (await Service(Now.AddDays(5), QaGrants).GetDetailAsync(_qa.Id, seeded.Mismatch.Id)).Appeal;
        Assert.Equal("EXPIRED", expired.State);

        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = _anh.Id,
            ResultSubmissionId = submission.Id,
            Reason = "Mã GD đúng, xin xem lại.",
            FiledAt = Now.AddHours(-2),
            Status = AppealStatus.Open,
            UpdatedAt = Now,
        };
        _db.Appeals.Add(appeal);
        await _db.SaveChangesAsync();
        var inProgress = await Service(QaGrants).GetDetailAsync(_qa.Id, seeded.Mismatch.Id);
        Assert.Equal("IN_PROGRESS", inProgress.Appeal.State);
        Assert.Equal(appeal.Id, inProgress.Appeal.AppealId);
        Assert.Contains(inProgress.History, h => h.Kind == "APPEAL");
    }

    [Fact]
    public async Task Detail_Appeal_AMatchStatusWithNoSubmissionHasNoAppeal()
    {
        var seeded = await SeedCodesAsync();

        var detail = await Service(QaGrants).GetDetailAsync(_qa.Id, seeded.Mismatch.Id);

        Assert.Equal("NOT_APPLICABLE", detail.Appeal.State);
        Assert.Null(detail.Submission);
    }

    // ----- Export -----------------------------------------------------------------------------

    [Fact]
    public async Task Export_WritesTheFilteredRowsWithVietnameseLabels()
    {
        await SeedCodesAsync();

        var file = await Service(QaGrants).ExportAsync(_qa.Id, new ReconciliationFilterDto(Status: "NOT_MATCHED"));

        Assert.EndsWith(".xlsx", file.FileName, StringComparison.Ordinal);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("Đối soát");
        Assert.Equal("Mã việc", sheet.Cell(4, 1).GetString());
        Assert.Equal("TX-EMP", sheet.Cell(5, 7).GetString());
        Assert.Equal("Không khớp", sheet.Cell(5, 14).GetString());
        Assert.Equal("Sai mã nhân viên", sheet.Cell(5, 15).GetString());
        Assert.Equal("TX-PRODUCT", sheet.Cell(6, 7).GetString());
        Assert.Equal(95_000_000d, sheet.Cell(6, 11).GetDouble());
        Assert.True(sheet.Cell(7, 1).IsEmpty());
    }

    [Fact]
    public async Task Export_IsLimitedToTheCallersScope()
    {
        await SeedCodesAsync();

        var file = await Service(ManagerGrants).ExportAsync(_manager.Id, new ReconciliationFilterDto());

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var codes = workbook.Worksheet("Đối soát").Column(7).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.DoesNotContain("TX-EMP", codes);
        Assert.Contains("TX-OK", codes);
    }

    private ResultSubmission AddSubmission(Guid workId, Employee submitter, int no, SubmissionStatus status)
    {
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            SubmitterEmployeeId = submitter.Id,
            SubmissionNo = no,
            Status = status,
            SubmittedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1),
        };
        _db.ResultSubmissions.Add(submission);
        return submission;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
