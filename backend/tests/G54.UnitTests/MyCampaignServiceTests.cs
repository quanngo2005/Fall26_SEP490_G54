using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.CampaignCustomer;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Employee-side "My campaigns" (docs/api/CAMPAIGN_CONTRACT.md §5): my campaigns summary, my customers list
/// (needs-action-first ordering), and the contact-update action, including the Work created on CLOSED.
/// </summary>
public sealed class MyCampaignServiceTests
{
    private static readonly string[] ExpectedNeedsActionThenClosedOrder =
        ["CUST-E-NEW1", "CUST-F-NEW2", "CUST-D-CALLED", "CUST-C-APPT", "CUST-B-MET", "CUST-A-CLOSED"];

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed record Fixture(
        AppDbContext Db,
        MyCampaignService Service,
        BusinessCampaign Campaign,
        Employee Emp1,
        Employee Emp2,
        WorkType WorkType);

    private static async Task<Fixture> CreateAsync(
        BusinessCampaignStatus campaignStatus = BusinessCampaignStatus.Running,
        bool linkWorkType = true,
        DateOnly? endDate = null)
    {
        var db = CreateInMemoryDbContext();

        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-MC-01",
            Name = "Mục tiêu thử nghiệm",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var dept = new Department { Id = Guid.NewGuid(), Code = "DEPT-MC", Name = "Phòng thử nghiệm", UpdatedAt = DateTimeOffset.UtcNow };
        var pos = new JobPosition { Id = Guid.NewGuid(), Code = "POS-MC", Title = "Chuyên viên", Status = RecordStatus.Active, UpdatedAt = DateTimeOffset.UtcNow };

        var emp1 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-MC-01",
            FullName = "Nguyễn Văn A",
            Email = "mc-a@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var emp2 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-MC-02",
            FullName = "Trần Thị B",
            Email = "mc-b@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT-MC", Name = "Tiết kiệm", Status = RecordStatus.Active, UpdatedAt = DateTimeOffset.UtcNow };

        var campaign = new BusinessCampaign
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = objective.Id,
            Code = "CMP-MC-01",
            Name = "Chiến dịch khách hàng của tôi",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = endDate ?? new DateOnly(2026, 6, 30),
            Status = campaignStatus,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Departments.Add(dept);
        db.JobPositions.Add(pos);
        db.Employees.AddRange(emp1, emp2);
        db.WorkTypes.Add(workType);
        db.BusinessObjectives.Add(objective);
        db.BusinessCampaigns.Add(campaign);

        db.Participants.AddRange(
            new Participant { Id = Guid.NewGuid(), BusinessCampaignId = campaign.Id, EmployeeId = emp1.Id, UpdatedAt = DateTimeOffset.UtcNow },
            new Participant { Id = Guid.NewGuid(), BusinessCampaignId = campaign.Id, EmployeeId = emp2.Id, UpdatedAt = DateTimeOffset.UtcNow });

        if (linkWorkType)
        {
            db.CampaignWorkTypes.Add(new CampaignWorkType
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                WorkTypeId = workType.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();

        return new Fixture(
            db,
            new MyCampaignService(db, new WorkTransactionService(db, TimeProvider.System)),
            campaign,
            emp1,
            emp2,
            workType);
    }

    private static CampaignCustomer NewCustomer(
        Guid campaignId, string cif, Guid? assigneeId, CampaignContactStatus status = CampaignContactStatus.NotContacted, string name = "Khách hàng") => new()
        {
            Id = Guid.NewGuid(),
            BusinessCampaignId = campaignId,
            CustomerCif = cif,
            CustomerName = name,
            ContactStatus = status,
            AssigneeEmployeeId = assigneeId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>A customer that already has a lead Work (as allocate/the migration would have created it).</summary>
    private static (CampaignCustomer Customer, Work Work) NewCustomerWithWork(
        BusinessCampaign campaign, WorkType workType, string cif, Guid assigneeId, string name = "Khách hàng",
        CampaignContactStatus status = CampaignContactStatus.NotContacted, string? note = null)
    {
        var now = DateTimeOffset.UtcNow;
        var customer = new CampaignCustomer
        {
            Id = Guid.NewGuid(),
            BusinessCampaignId = campaign.Id,
            CustomerCif = cif,
            CustomerName = name,
            Note = note,
            ContactStatus = status,
            AssigneeEmployeeId = assigneeId,
            AssignedAt = now,
            UpdatedAt = now,
        };
        var workCodePrefix = $"TSK-{campaign.Code}-LEAD-";
        var workCode = (workCodePrefix + Guid.NewGuid().ToString("N"))[..(workCodePrefix.Length + 8)];
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = workCode,
            Title = $"[Chiến dịch {campaign.Code}] {name}",
            Description = $"Nguồn: chiến dịch {campaign.Code} - {campaign.Name}. Mã KH: {cif}, Tên KH: {name}. Sản phẩm: {workType.Name}.",
            BusinessCampaignId = campaign.Id,
            WorkTypeId = workType.Id,
            AssigneeEmployeeId = assigneeId,
            CreatedBy = assigneeId,
            CustomerCif = cif,
            CustomerName = name,
            Status = WorkStatus.New,
            Priority = WorkPriority.Medium,
            PlannedStartDate = DateOnly.FromDateTime(now.UtcDateTime),
            PlannedEndDate = null,
            AssignedAt = now,
            UpdatedAt = now,
        };
        customer.WorkId = work.Id;
        return (customer, work);
    }

    // ---- GetMyCampaignsAsync --------------------------------------------------------------------------------

    [Fact]
    public async Task GetMyCampaignsAsync_OnlyReturnsCampaignsWhereIHaveAssignedCustomers_WithMyCounts()
    {
        var f = await CreateAsync();
        var mine1 = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id, CampaignContactStatus.Called);
        var mine2 = NewCustomer(f.Campaign.Id, "CUST-002", f.Emp1.Id, CampaignContactStatus.Closed);
        var others = NewCustomer(f.Campaign.Id, "CUST-003", f.Emp2.Id);
        f.Db.CampaignCustomers.AddRange(mine1, mine2, others);
        await f.Db.SaveChangesAsync();

        var result = await f.Service.GetMyCampaignsAsync(f.Emp1.Id);

        var summary = Assert.Single(result);
        Assert.Equal(f.Campaign.Id, summary.CampaignId);
        Assert.Equal(2, summary.TotalAssigned);
        Assert.Equal(1, summary.Closed);
        Assert.Equal(1, summary.ByStatus.Single(s => s.Status == "CALLED").Count);
        Assert.Equal(1, summary.ByStatus.Single(s => s.Status == "CLOSED").Count);
    }

    [Fact]
    public async Task GetMyCampaignsAsync_NoAssignedCustomers_ReturnsEmptyList()
    {
        var f = await CreateAsync();
        var result = await f.Service.GetMyCampaignsAsync(f.Emp1.Id);
        Assert.Empty(result);
    }

    // ---- GetMyCustomersAsync ---------------------------------------------------------------------------------

    [Fact]
    public async Task GetMyCustomersAsync_UnknownCampaign_ThrowsNotFound()
    {
        var f = await CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetMyCustomersAsync(Guid.NewGuid(), f.Emp1.Id, null, null, 1, 20));
    }

    [Fact]
    public async Task GetMyCustomersAsync_OnlyReturnsMyRows_AndEmptyPageWhenNone()
    {
        var f = await CreateAsync();
        var mine = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(mine);
        await f.Db.SaveChangesAsync();

        var myPage = await f.Service.GetMyCustomersAsync(f.Campaign.Id, f.Emp1.Id, null, null, 1, 20);
        Assert.Equal(1, myPage.TotalCount);
        Assert.Equal("CUST-001", myPage.Items[0].CustomerCif);

        // Emp2 is a valid employee but has no rows in this campaign: empty page, not an error.
        var emptyPage = await f.Service.GetMyCustomersAsync(f.Campaign.Id, f.Emp2.Id, null, null, 1, 20);
        Assert.Equal(0, emptyPage.TotalCount);
        Assert.Empty(emptyPage.Items);
    }

    [Fact]
    public async Task GetMyCustomersAsync_OrdersNeedsActionFirstThenClosedLast_ThenByCif()
    {
        var f = await CreateAsync();
        var closed = NewCustomer(f.Campaign.Id, "CUST-A-CLOSED", f.Emp1.Id, CampaignContactStatus.Closed);
        var met = NewCustomer(f.Campaign.Id, "CUST-B-MET", f.Emp1.Id, CampaignContactStatus.Met);
        var appt = NewCustomer(f.Campaign.Id, "CUST-C-APPT", f.Emp1.Id, CampaignContactStatus.AppointmentSet);
        var called = NewCustomer(f.Campaign.Id, "CUST-D-CALLED", f.Emp1.Id, CampaignContactStatus.Called);
        var notContacted2 = NewCustomer(f.Campaign.Id, "CUST-F-NEW2", f.Emp1.Id, CampaignContactStatus.NotContacted);
        var notContacted1 = NewCustomer(f.Campaign.Id, "CUST-E-NEW1", f.Emp1.Id, CampaignContactStatus.NotContacted);
        f.Db.CampaignCustomers.AddRange(closed, met, appt, called, notContacted2, notContacted1);
        await f.Db.SaveChangesAsync();

        var page = await f.Service.GetMyCustomersAsync(f.Campaign.Id, f.Emp1.Id, null, null, 1, 20);

        Assert.Equal(ExpectedNeedsActionThenClosedOrder, page.Items.Select(i => i.CustomerCif).ToArray());
    }

    // ---- UpdateContactAsync ----------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateContactAsync_AnotherEmployeesRow_ThrowsNotFound()
    {
        var f = await CreateAsync();
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp2.Id); // belongs to Emp2
        f.Db.CampaignCustomers.Add(row);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CALLED", true, null, null, null);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto));
    }

    [Fact]
    public async Task UpdateContactAsync_CampaignNotRunning_ThrowsConflict409()
    {
        var f = await CreateAsync(campaignStatus: BusinessCampaignStatus.Planned);
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(row);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CALLED", true, null, null, null);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto));
        Assert.Equal(ErrorCodes.CampaignNotRunning, ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateContactAsync_AlreadyClosed_ThrowsConflict409()
    {
        var f = await CreateAsync();
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id, CampaignContactStatus.Closed);
        row.ClosedWorkTypeId = f.WorkType.Id;
        f.Db.CampaignCustomers.Add(row);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("MET", null, null, DateTimeOffset.UtcNow, null);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto));
        Assert.Equal(ErrorCodes.CampaignCustomerClosed, ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateContactAsync_Called_WithoutCallAnswered_ThrowsValidation()
    {
        var f = await CreateAsync();
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(row);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CALLED", null, null, null, null);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto));
    }

    [Fact]
    public async Task UpdateContactAsync_Called_MissedCall_IncrementsMissedCallCount()
    {
        var f = await CreateAsync();
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(row);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CALLED", false, null, null, null);
        var result1 = await f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto);
        Assert.Equal(1, result1.MissedCallCount);

        var result2 = await f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto);
        Assert.Equal(2, result2.MissedCallCount);
        Assert.Equal("Không nghe máy 2 lần", result2.ResultText);
    }

    [Fact]
    public async Task UpdateContactAsync_AppointmentSet_WithoutAppointmentAt_ThrowsValidation()
    {
        var f = await CreateAsync();
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(row);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("APPOINTMENT_SET", null, null, null, null);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto));
    }

    [Fact]
    public async Task UpdateContactAsync_Met_WithoutMetAt_ThrowsValidation()
    {
        var f = await CreateAsync();
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(row);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("MET", null, null, null, null);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto));
    }

    [Fact]
    public async Task UpdateContactAsync_Closed_WithoutTransactionCodes_ThrowsValidation()
    {
        var f = await CreateAsync();
        var (customer, work) = NewCustomerWithWork(f.Campaign, f.WorkType, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(customer);
        f.Db.Works.Add(work);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CLOSED", null, null, null, null);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, customer.Id, f.Emp1.Id, dto));
    }

    [Fact]
    public async Task UpdateContactAsync_Closed_WorkTypeNotLinkedToCampaign_ThrowsWorkTypeNotInCampaign()
    {
        // No CampaignWorkType row for this campaign: the campaign has no product configured.
        var f = await CreateAsync(linkWorkType: false);
        var (customer, work) = NewCustomerWithWork(f.Campaign, f.WorkType, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(customer);
        f.Db.Works.Add(work);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CLOSED", null, null, null, ["TXN-001"]);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, customer.Id, f.Emp1.Id, dto));
        Assert.Equal(ErrorCodes.WorkTypeNotInCampaign, ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateContactAsync_Closed_RecordsCodesAndSubmitsTheExistingLeadWork()
    {
        var f = await CreateAsync();
        var (customer, work) = NewCustomerWithWork(f.Campaign, f.WorkType, "CUST-CLOSE-01", f.Emp1.Id, name: "Nguyễn Văn Chốt");
        f.Db.CampaignCustomers.Add(customer);
        f.Db.Works.Add(work);
        await f.Db.SaveChangesAsync();
        var workId = work.Id;

        var dto = new UpdateCampaignCustomerContactRequestDto("CLOSED", null, null, null, ["TXN-001"]);
        var result = await f.Service.UpdateContactAsync(f.Campaign.Id, customer.Id, f.Emp1.Id, dto);

        Assert.Equal("CLOSED", result.ContactStatus);
        Assert.Equal(f.WorkType.Id, result.ClosedWorkTypeId);
        Assert.Equal(workId, result.WorkId);
        Assert.Equal("Đã chốt được sản phẩm Tiết kiệm", result.ResultText);

        // No second Work is created: the lead Work from allocation is reused.
        Assert.Equal(1, await f.Db.Works.CountAsync());
        var reloadedWork = await f.Db.Works.SingleAsync(w => w.Id == workId);
        Assert.Equal(WorkStatus.Submitted, reloadedWork.Status);
        Assert.Equal(1, await f.Db.WorkTransactions.CountAsync(t => t.WorkId == workId));
        Assert.Equal(
            "Nguồn: chiến dịch CMP-MC-01 - Chiến dịch khách hàng của tôi. Mã KH: CUST-CLOSE-01, Tên KH: Nguyễn Văn Chốt. Sản phẩm: Tiết kiệm.",
            reloadedWork.Description);
    }

    [Fact]
    public async Task UpdateContactAsync_Closed_WithNote_AppendsNoteToWorkDescription()
    {
        var f = await CreateAsync();
        var (customer, work) = NewCustomerWithWork(f.Campaign, f.WorkType, "CUST-NOTE-01", f.Emp1.Id, note: "Ghi chú gốc bị thay bởi ghi chú chốt");
        f.Db.CampaignCustomers.Add(customer);
        f.Db.Works.Add(work);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CLOSED", null, null, null, ["TXN-002"], "Sổ 500 triệu đáo hạn 25/10");
        var result = await f.Service.UpdateContactAsync(f.Campaign.Id, customer.Id, f.Emp1.Id, dto);

        var reloadedWork = await f.Db.Works.SingleAsync(w => w.Id == result.WorkId);
        Assert.EndsWith("Sản phẩm: Tiết kiệm. Ghi chú: Sổ 500 triệu đáo hạn 25/10", reloadedWork.Description);
    }

    [Fact]
    public async Task UpdateContactAsync_RemovedParticipant_ThrowsEmployeeNotParticipant_AndLeavesWorkUnchanged()
    {
        var f = await CreateAsync();
        var (customer, work) = NewCustomerWithWork(f.Campaign, f.WorkType, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(customer);
        f.Db.Works.Add(work);
        f.Db.Participants.RemoveRange(f.Db.Participants.Where(p => p.EmployeeId == f.Emp1.Id));
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CLOSED", null, null, null, ["TXN-003"]);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, customer.Id, f.Emp1.Id, dto));

        Assert.Equal(ErrorCodes.EmployeeNotParticipant, ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(WorkStatus.New, (await f.Db.Works.SingleAsync(w => w.Id == work.Id)).Status);
    }

    [Fact]
    public async Task UpdateContactAsync_InactiveParticipant_ThrowsEmployeeNotParticipant()
    {
        var f = await CreateAsync();
        var row = NewCustomer(f.Campaign.Id, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(row);
        var emp1 = await f.Db.Employees.SingleAsync(e => e.Id == f.Emp1.Id);
        emp1.Status = EmployeeStatus.Inactive;
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CALLED", true, null, null, null);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => f.Service.UpdateContactAsync(f.Campaign.Id, row.Id, f.Emp1.Id, dto));

        Assert.Equal(ErrorCodes.EmployeeNotParticipant, ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateContactAsync_Called_MirrorsWorkStatusToInProgress_WithActivityOnlyOnChange()
    {
        var f = await CreateAsync();
        var (customer, work) = NewCustomerWithWork(f.Campaign, f.WorkType, "CUST-001", f.Emp1.Id);
        f.Db.CampaignCustomers.Add(customer);
        f.Db.Works.Add(work);
        await f.Db.SaveChangesAsync();

        var dto = new UpdateCampaignCustomerContactRequestDto("CALLED", true, null, null, null);
        await f.Service.UpdateContactAsync(f.Campaign.Id, customer.Id, f.Emp1.Id, dto);

        var reloadedWork = await f.Db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(WorkStatus.InProgress, reloadedWork.Status);
        Assert.Equal(1, await f.Db.WorkActivities.CountAsync(a => a.WorkId == work.Id));

        // A second CALLED update keeps the Work at IN_PROGRESS: no further activity is recorded.
        var dto2 = new UpdateCampaignCustomerContactRequestDto("CALLED", false, null, null, null);
        await f.Service.UpdateContactAsync(f.Campaign.Id, customer.Id, f.Emp1.Id, dto2);
        Assert.Equal(1, await f.Db.WorkActivities.CountAsync(a => a.WorkId == work.Id));
    }
}
