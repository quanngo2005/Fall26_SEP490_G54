using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.RiskEvent;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class RiskEventScreenServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
    private const string Reason = "Ghi nhầm nhân viên, đã đối chiếu lại.";

    private static readonly UserPermissionDto[] QaGrants =
    [
        new("risk_event", "read", "All", "RISK_EVENT_READ"),
        new("risk_event", "create", "All", "RISK_EVENT_CREATE"),
        new("risk_event", "reverse", "All", "RISK_EVENT_REVERSE"),
    ];

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly FakeStorage _storage = new();
    private Department _branch = null!;
    private Department _inBranch = null!;
    private Department _elsewhere = null!;
    private Employee _qa = null!;
    private EvaluationPeriod _period = null!;

    public void Dispose() => _db.Dispose();

    private RiskEventScreenService Screens(params UserPermissionDto[] grants) => new(
        _db,
        new FixedTime(Now),
        new FakePermissionService(null, grants),
        _storage,
        NullLogger<RiskEventScreenService>.Instance);

    private RiskEventService Events => new(_db, NullLogger<RiskEventService>.Instance, new FixedTime(Now));

    private async Task SeedAsync()
    {
        _branch = NewDepartment("BR", DepartmentLevelType.Branch, null);
        _inBranch = NewDepartment("D1", DepartmentLevelType.Department, _branch.Id);
        _elsewhere = NewDepartment("D2", DepartmentLevelType.Department, null);
        _db.Departments.AddRange(_branch, _inBranch, _elsewhere);
        _period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-10",
            Name = "10/2026",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Status = PeriodStatus.Open,
        };
        _db.EvaluationPeriods.Add(_period);
        await _db.SaveChangesAsync();
        _qa = await AddEmployeeAsync("QA-USER", _elsewhere);
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

    private static CreateRiskEventRequestDto Request(Employee employee, bool record = true, decimal? points = null, string? workCode = null) => new()
    {
        EmployeeId = employee.Id,
        EventType = "WRONG_CUSTOMER_INFO",
        Description = "Khai sai thông tin khách hàng.",
        Severity = "MEDIUM",
        OccurredAt = OccurredAt,
        PenaltyPoints = points,
        WorkCode = workCode,
        Record = record,
    };

    private async Task<RiskEventDto> RecordAsync(Employee employee, decimal? points = 3m, string? workCode = null) =>
        await Events.CreateAsync(Request(employee, true, points, workCode), _qa.Id);

    // ----- Record at once ---------------------------------------------------------------------

    [Fact]
    public async Task Create_WithRecord_ConfirmsTheEventAndWritesThePenaltyTheRecorderEntered()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);

        var recorded = await RecordAsync(employee, 3m);

        Assert.Equal("CONFIRMED", recorded.Status);
        Assert.NotNull(recorded.ContributionId);
        var contribution = await _db.PerformanceContributions.AsNoTracking().SingleAsync();
        Assert.Equal(-3m, contribution.ComponentScore);
        Assert.Equal(_period.Id, contribution.EvaluationPeriodId);
        var stored = await _db.RiskEvents.AsNoTracking().SingleAsync();
        Assert.Equal(3m, stored.PenaltyPoints);
        Assert.Equal(Now, stored.RecordedAt);
    }

    [Fact]
    public async Task Create_WithRecordAndNoPoints_UsesThePenaltyOfTheSeverity()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);

        await RecordAsync(employee, null);

        Assert.Equal(-5m, (await _db.PerformanceContributions.AsNoTracking().SingleAsync()).ComponentScore);
    }

    [Fact]
    public async Task Create_WithoutRecord_KeepsTheOpenFlow()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);

        var created = await Events.CreateAsync(Request(employee, false), _qa.Id);

        Assert.Equal("OPEN", created.Status);
        Assert.Empty(_db.PerformanceContributions);
    }

    [Fact]
    public async Task Create_WithRecordInALockedPeriod_IsRefusedAndStoresNothing()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);
        _period.Status = PeriodStatus.Locked;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => RecordAsync(employee));

        Assert.Equal(ErrorCodes.RiskEventPeriodUnavailable, ex.ErrorCode);
        Assert.Empty(_db.RiskEvents);
        Assert.Empty(_db.PerformanceContributions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.5)]
    [InlineData(1.234)]
    public async Task Create_RejectsAPenaltyOutsideTheRange(double points)
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);

        await Assert.ThrowsAsync<ValidationException>(() => RecordAsync(employee, (decimal)points));
    }

    [Fact]
    public async Task Create_LinksTheWorkByCode_AndRejectsAnUnknownOne()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WK-1051",
            Title = "Mở thẻ",
            AssigneeEmployeeId = employee.Id,
            CreatedBy = employee.Id,
            Status = WorkStatus.Verified,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        _db.Works.Add(work);
        await _db.SaveChangesAsync();

        await RecordAsync(employee, 3m, " WK-1051 ");
        await Assert.ThrowsAsync<ValidationException>(() => RecordAsync(employee, 3m, "WK-9999"));

        Assert.Equal(work.Id, (await _db.RiskEvents.AsNoTracking().SingleAsync()).WorkId);
    }

    [Fact]
    public void ThePenaltyOfAnEvent_IsTheEnteredPointsOtherwiseTheSeverity()
    {
        Assert.Equal(3m, EvaluationService.RiskPenalty(new RiskEvent { Severity = RiskEventSeverity.High, PenaltyPoints = 3m }));
        Assert.Equal(10m, EvaluationService.RiskPenalty(new RiskEvent { Severity = RiskEventSeverity.High }));
    }

    // ----- List and details --------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsTheEventsWithTheCountOfEachState_AndFiltersThem()
    {
        await SeedAsync();
        var first = await AddEmployeeAsync("NV1", _inBranch);
        var second = await AddEmployeeAsync("NV2", _elsewhere);
        var keep = await RecordAsync(first, 3m);
        var reversed = await RecordAsync(second, 4m);
        await Screens(QaGrants).ReverseAsync(_qa.Id, reversed.Id, new ReverseRiskEventDto(Reason));

        // The database numbers events from a sequence; InMemory does not, so number them here.
        (await _db.RiskEvents.SingleAsync(r => r.Id == keep.Id)).CodeNo = 1;
        (await _db.RiskEvents.SingleAsync(r => r.Id == reversed.Id)).CodeNo = 2;
        await _db.SaveChangesAsync();
        const string keepCode = "RSK-0001";
        const string reversedCode = "RSK-0002";
        var all = await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto());
        var onlyReversed = await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { State = "reversed" });
        var inBranch = await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { DepartmentId = _branch.Id });
        var byCode = await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { Search = reversedCode });
        var byEmployee = await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { Search = "nv1" });
        var inPeriod = await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { PeriodId = _period.Id });
        var other = await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { Severity = "HIGH" });

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(new RiskEventCountsDto(1, 1), all.Counts);
        Assert.Equal(["Phòng D1", "Phòng D2"], all.Departments.Select(d => d.Name));
        var item = all.Items.Single(i => i.Id == keep.Id);
        Assert.Equal(keepCode, item.Code);
        Assert.Equal(3m, item.PenaltyPoints);
        Assert.Equal("RECORDED", item.State);
        Assert.Equal(reversed.Id, Assert.Single(onlyReversed.Items).Id);
        Assert.Equal("REVERSED", onlyReversed.Items[0].State);
        Assert.Equal(keep.Id, Assert.Single(inBranch.Items).Id);
        Assert.Equal(reversed.Id, Assert.Single(byCode.Items).Id);
        Assert.Equal(keep.Id, Assert.Single(byEmployee.Items).Id);
        Assert.Equal(2, inPeriod.TotalCount);
        Assert.Empty(other.Items);
        await Assert.ThrowsAsync<ValidationException>(() => Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { State = "NOPE" }));
        await Assert.ThrowsAsync<NotFoundException>(() => Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { PeriodId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task List_ShowsAManagerOnlyThePeopleOfTheirScope()
    {
        await SeedAsync();
        var inside = await AddEmployeeAsync("IN", _inBranch);
        var outside = await AddEmployeeAsync("OUT", _elsewhere);
        var manager = await AddEmployeeAsync("MGR", _branch);
        var seen = await RecordAsync(inside);
        var hidden = await RecordAsync(outside);
        var branchRead = new UserPermissionDto("risk_event", "read", "Branch", "RISK_EVENT_READ");

        var list = await Screens(branchRead).ListAsync(manager.Id, new RiskEventFilterDto());

        Assert.Equal(seen.Id, Assert.Single(list.Items).Id);
        Assert.Equal("Phòng D1", Assert.Single(list.Departments).Name);
        await Assert.ThrowsAsync<NotFoundException>(() => Screens(branchRead).GetDetailsAsync(manager.Id, hidden.Id));
        var details = await Screens(branchRead).GetDetailsAsync(manager.Id, seen.Id);
        Assert.False(details.CanReverse);
    }

    [Fact]
    public async Task List_WithoutTheReadPermission_IsForbidden()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Screens().ListAsync(_qa.Id, new RiskEventFilterDto()));
    }

    [Fact]
    public async Task Details_ReturnsTheEventWithItsPeriodRecorderAndAttachments()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);
        var recorded = await RecordAsync(employee, 3m);
        await Screens(QaGrants).AddEvidenceAsync(_qa.Id, recorded.Id, [PdfFile("bien_ban_0045.pdf")]);

        var details = await Screens(QaGrants).GetDetailsAsync(_qa.Id, recorded.Id);

        Assert.Equal("Khai sai thông tin khách hàng.", details.Description);
        Assert.Equal("2026-10", details.PeriodCode);
        Assert.Equal("Teller", details.JobPositionTitle);
        Assert.Equal(_qa.FullName, details.RecordedByName);
        Assert.Equal(Now, details.RecordedAt);
        Assert.Equal("bien_ban_0045.pdf", Assert.Single(details.Attachments).FileName);
        Assert.True(details.CanReverse);
        Assert.Null(details.ReversedAt);
    }

    // ----- Reverse ----------------------------------------------------------------------------

    [Fact]
    public async Task Reverse_DismissesTheEventKeepsTheReasonAndTheContribution()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);
        var recorded = await RecordAsync(employee, 3m);

        var details = await Screens(QaGrants).ReverseAsync(_qa.Id, recorded.Id, new ReverseRiskEventDto("  " + Reason + "  "));

        Assert.Equal("DISMISSED", details.Item.Status);
        Assert.Equal("REVERSED", details.Item.State);
        Assert.Equal(Reason, details.ReversalReason);
        Assert.Equal(_qa.FullName, details.ReversedByName);
        Assert.Equal(Now, details.ReversedAt);
        Assert.False(details.CanReverse);
        Assert.Equal(-3m, (await _db.PerformanceContributions.AsNoTracking().SingleAsync()).ComponentScore);
        var stored = await _db.RiskEvents.AsNoTracking().SingleAsync();
        Assert.Equal(_qa.Id, stored.ReversedBy);
    }

    [Fact]
    public async Task Reverse_TwiceOrAnOpenEvent_IsRefused()
    {
        await SeedAsync();
        var employee = await AddEmployeeAsync("NV1", _inBranch);
        var recorded = await RecordAsync(employee);
        var open = await Events.CreateAsync(Request(employee, false), _qa.Id);
        await Screens(QaGrants).ReverseAsync(_qa.Id, recorded.Id, new ReverseRiskEventDto(Reason));

        var again = await Assert.ThrowsAsync<ConflictException>(() => Screens(QaGrants).ReverseAsync(_qa.Id, recorded.Id, new ReverseRiskEventDto(Reason)));
        var notRecorded = await Assert.ThrowsAsync<ConflictException>(() => Screens(QaGrants).ReverseAsync(_qa.Id, open.Id, new ReverseRiskEventDto(Reason)));

        Assert.Equal(ErrorCodes.RiskEventInvalidState, again.ErrorCode);
        Assert.Equal(ErrorCodes.RiskEventInvalidState, notRecorded.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ngắn")]
    public async Task Reverse_NeedsAReasonOfAtLeastTenCharacters(string? reason)
    {
        await SeedAsync();
        var recorded = await RecordAsync(await AddEmployeeAsync("NV1", _inBranch));

        await Assert.ThrowsAsync<ValidationException>(() => Screens(QaGrants).ReverseAsync(_qa.Id, recorded.Id, new ReverseRiskEventDto(reason)));
        Assert.Equal(RiskEventStatus.Confirmed, (await _db.RiskEvents.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Reverse_InALockedPeriod_IsRefused()
    {
        await SeedAsync();
        var recorded = await RecordAsync(await AddEmployeeAsync("NV1", _inBranch));
        _period.Status = PeriodStatus.Closed;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Screens(QaGrants).ReverseAsync(_qa.Id, recorded.Id, new ReverseRiskEventDto(Reason)));

        Assert.Equal(ErrorCodes.RiskEventPeriodUnavailable, ex.ErrorCode);
        Assert.Equal(RiskEventStatus.Confirmed, (await _db.RiskEvents.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Reverse_OfAnEventAboutTheCaller_IsForbidden_AndAnUnknownEventIsNotFound()
    {
        await SeedAsync();
        var recorded = await RecordAsync(await AddEmployeeAsync("NV1", _inBranch));
        var subject = await _db.Employees.AsNoTracking().SingleAsync(e => e.EmployeeCode == "NV1");

        await Assert.ThrowsAsync<ForbiddenException>(() => Screens(QaGrants).ReverseAsync(subject.Id, recorded.Id, new ReverseRiskEventDto(Reason)));
        await Assert.ThrowsAsync<NotFoundException>(() => Screens(QaGrants).ReverseAsync(_qa.Id, Guid.NewGuid(), new ReverseRiskEventDto(Reason)));
    }

    [Fact]
    public async Task AReversedEvent_NoLongerCountsAsAPenaltyInTheEvaluation()
    {
        await SeedAsync();
        var recorded = await RecordAsync(await AddEmployeeAsync("NV1", _inBranch), 3m);
        await Screens(QaGrants).ReverseAsync(_qa.Id, recorded.Id, new ReverseRiskEventDto(Reason));

        var counted = await _db.RiskEvents.AsNoTracking().Where(r => r.Status != RiskEventStatus.Dismissed).ToListAsync();

        Assert.Empty(counted);
    }

    // ----- Evidence ---------------------------------------------------------------------------

    [Fact]
    public async Task AddEvidence_AttachesTheFilesAndReturnsEveryAttachment()
    {
        await SeedAsync();
        var recorded = await RecordAsync(await AddEmployeeAsync("NV1", _inBranch));

        var first = await Screens(QaGrants).AddEvidenceAsync(_qa.Id, recorded.Id, [PdfFile("a.pdf")]);
        var both = await Screens(QaGrants).AddEvidenceAsync(_qa.Id, recorded.Id, [PdfFile("b.pdf")]);

        Assert.Single(first);
        Assert.Equal(["a.pdf", "b.pdf"], both.Select(a => a.FileName));
        Assert.All(_db.Evidences, e => Assert.Equal(recorded.Id, e.RiskEventId));
    }

    [Fact]
    public async Task AddEvidence_RefusesNoFilesAndAReversedEvent_AndRemovesTheStoredFilesWhenSavingFails()
    {
        await SeedAsync();
        var recorded = await RecordAsync(await AddEmployeeAsync("NV1", _inBranch));

        await Assert.ThrowsAsync<ValidationException>(() => Screens(QaGrants).AddEvidenceAsync(_qa.Id, recorded.Id, []));
        await Assert.ThrowsAsync<NotFoundException>(() => Screens(QaGrants).AddEvidenceAsync(_qa.Id, Guid.NewGuid(), [PdfFile("a.pdf")]));
        await Screens(QaGrants).ReverseAsync(_qa.Id, recorded.Id, new ReverseRiskEventDto(Reason));
        await Assert.ThrowsAsync<ConflictException>(() => Screens(QaGrants).AddEvidenceAsync(_qa.Id, recorded.Id, [PdfFile("a.pdf")]));
        Assert.Empty(_db.Evidences);
    }

    private static FormFile PdfFile(string name) => new FormFile(new MemoryStream("%PDF-1.4"u8.ToArray()), 0, 8, "files", name);

    private sealed class FakeStorage : IStorageService
    {
        public Task<IReadOnlyList<StoredFileMetadata>> UploadEvidenceFilesAsync(IEnumerable<IFormFile> files, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredFileMetadata>>(
                files.Select(f => new StoredFileMetadata(f.FileName, $"{Guid.NewGuid():N}.pdf", "application/pdf", f.Length)).ToList());

        public Task<Stream> OpenEvidenceReadAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteEvidenceFilesAsync(IEnumerable<StoredFileMetadata> files, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task List_SortsByTheChosenColumn_AndRejectsAnUnknownOne()
    {
        await SeedAsync();
        var low = await RecordAsync(await AddEmployeeAsync("NV-B", _inBranch), 1m);
        var high = await RecordAsync(await AddEmployeeAsync("NV-C", _inBranch), 9m);
        var mid = await RecordAsync(await AddEmployeeAsync("NV-A", _inBranch), 5m);

        async Task<string[]> Order(string? by, string? dir) =>
            (await Screens(QaGrants).ListAsync(_qa.Id, new RiskEventFilterDto { SortBy = by, SortDir = dir })).Items
            .Select(i => i.EmployeeCode).ToArray();

        Assert.Equal(["NV-C", "NV-A", "NV-B"], await Order("penalty", null));
        Assert.Equal(["NV-B", "NV-A", "NV-C"], await Order("penalty", "asc"));
        Assert.Equal(["NV-A", "NV-B", "NV-C"], await Order("employee", null));
        Assert.Equal(["NV-C", "NV-B", "NV-A"], await Order("EMPLOYEE", "DESC"));
        Assert.Equal(3, (await Order(null, null)).Length);
        await Assert.ThrowsAsync<ValidationException>(() => Order("password", null));
        await Assert.ThrowsAsync<ValidationException>(() => Order("code", "sideways"));
        Assert.NotEqual(low.Id, high.Id);
        Assert.NotEqual(mid.Id, low.Id);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
