using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.CampaignCustomer;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

public sealed class CampaignCustomerServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed record Fixture(
        AppDbContext Db,
        CampaignCustomerService Service,
        BusinessCampaign Campaign,
        Employee Emp1,
        Employee Emp2,
        WorkType WorkType,
        Guid ManagerAccountId);

    private static async Task<Fixture> CreateAsync(
        BusinessCampaignStatus campaignStatus = BusinessCampaignStatus.Running,
        bool withParticipants = true)
    {
        var db = CreateInMemoryDbContext();

        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-CC-01",
            Name = "Mục tiêu thử nghiệm",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var dept = new Department { Id = Guid.NewGuid(), Code = "DEPT-CC", Name = "Phòng thử nghiệm", UpdatedAt = DateTimeOffset.UtcNow };
        var pos = new JobPosition { Id = Guid.NewGuid(), Code = "POS-CC", Title = "Chuyên viên", Status = RecordStatus.Active, UpdatedAt = DateTimeOffset.UtcNow };

        var emp1 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-CC-01",
            FullName = "Nguyễn Văn A",
            Email = "a@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var emp2 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-CC-02",
            FullName = "Trần Thị B",
            Email = "b@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var inactiveEmp = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-CC-03",
            FullName = "Lê Văn C",
            Email = "c@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Inactive,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // The JWT subject is an account id, so the acting manager is resolved through its account.
        var manager = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-CC-QL",
            FullName = "Phạm Quản Lý",
            Email = "ql@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var managerAccount = new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = manager.Id,
            Username = "ql@g54.test",
            PasswordHash = "x",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT-CC", Name = "Tiết kiệm", Status = RecordStatus.Active, UpdatedAt = DateTimeOffset.UtcNow };

        var campaign = new BusinessCampaign
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = objective.Id,
            Code = "CMP-CC-01",
            Name = "Chiến dịch khách hàng",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = campaignStatus,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Departments.Add(dept);
        db.JobPositions.Add(pos);
        db.Employees.AddRange(emp1, emp2, inactiveEmp, manager);
        db.Accounts.Add(managerAccount);
        db.WorkTypes.Add(workType);
        db.BusinessObjectives.Add(objective);
        db.BusinessCampaigns.Add(campaign);
        db.CampaignWorkTypes.Add(new CampaignWorkType
        {
            Id = Guid.NewGuid(),
            BusinessCampaignId = campaign.Id,
            WorkTypeId = workType.Id,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        if (withParticipants)
        {
            db.Participants.AddRange(
                new Participant { Id = Guid.NewGuid(), BusinessCampaignId = campaign.Id, EmployeeId = emp1.Id, UpdatedAt = DateTimeOffset.UtcNow },
                new Participant { Id = Guid.NewGuid(), BusinessCampaignId = campaign.Id, EmployeeId = emp2.Id, UpdatedAt = DateTimeOffset.UtcNow },
                new Participant { Id = Guid.NewGuid(), BusinessCampaignId = campaign.Id, EmployeeId = inactiveEmp.Id, UpdatedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync();

        return new Fixture(db, new CampaignCustomerService(db, TimeProvider.System), campaign, emp1, emp2, workType, managerAccount.Id);
    }

    private static CampaignCustomer NewCustomer(Guid campaignId, string cif, string name = "Khách hàng", Guid? assigneeId = null) => new()
    {
        Id = Guid.NewGuid(),
        BusinessCampaignId = campaignId,
        CustomerCif = cif,
        CustomerName = name,
        ContactStatus = CampaignContactStatus.NotContacted,
        AssigneeEmployeeId = assigneeId,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    // ---- ListAsync -----------------------------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_UnknownCampaign_ThrowsNotFound()
    {
        var f = await CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ListAsync(Guid.NewGuid(), null, null, null, false, 1, 20));
    }

    [Fact]
    public async Task ListAsync_FiltersByStatusAssigneeAndUnassigned_AndPages()
    {
        // Search uses PostgreSQL ILike (EF.Functions.ILike), which the InMemory provider cannot translate, so it
        // is not covered here; same convention as EmployeeServiceTests (covered against a real database instead).
        var f = await CreateAsync();
        var c1 = NewCustomer(f.Campaign.Id, "CUST-001", "Nguyễn An", f.Emp1.Id);
        c1.ContactStatus = CampaignContactStatus.Called;
        c1.CallAnswered = true;
        var c2 = NewCustomer(f.Campaign.Id, "CUST-002", "Trần Bình", f.Emp2.Id);
        c2.ContactStatus = CampaignContactStatus.Closed;
        c2.ClosedWorkTypeId = f.WorkType.Id;
        var c3 = NewCustomer(f.Campaign.Id, "CUST-003", "Phạm Cường"); // unassigned
        f.Db.CampaignCustomers.AddRange(c1, c2, c3);
        await f.Db.SaveChangesAsync();

        // status
        var byStatus = await f.Service.ListAsync(f.Campaign.Id, null, "CLOSED", null, false, 1, 20);
        Assert.Equal(1, byStatus.Page.TotalCount);
        Assert.Equal("CUST-002", byStatus.Page.Items[0].CustomerCif);

        // assignee
        var byAssignee = await f.Service.ListAsync(f.Campaign.Id, null, null, f.Emp1.Id, false, 1, 20);
        Assert.Equal(1, byAssignee.Page.TotalCount);

        // unassigned only
        var unassigned = await f.Service.ListAsync(f.Campaign.Id, null, null, null, true, 1, 20);
        Assert.Equal(1, unassigned.Page.TotalCount);
        Assert.Equal("CUST-003", unassigned.Page.Items[0].CustomerCif);

        // paging
        var page1 = await f.Service.ListAsync(f.Campaign.Id, null, null, null, false, 1, 2);
        Assert.Equal(3, page1.Page.TotalCount);
        Assert.Equal(2, page1.Page.Items.Count);

        // summary: computed over the whole campaign, not the filtered page
        var summary = unassigned.Summary;
        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Unassigned);
        Assert.Equal(1, summary.ByStatus.Single(s => s.Status == "CLOSED").Count);
        Assert.Equal(1, summary.ByStatus.Single(s => s.Status == "CALLED").Count);
        var emp2Summary = summary.ByAssignee.Single(a => a.EmployeeId == f.Emp2.Id);
        Assert.Equal(1, emp2Summary.Assigned);
        Assert.Equal(1, emp2Summary.Closed);
    }

    [Theory]
    [InlineData(CampaignContactStatus.NotContacted, null, 0, null, null, null, "Chưa liên hệ")]
    [InlineData(CampaignContactStatus.Called, true, 0, null, null, null, "Gọi thành công")]
    [InlineData(CampaignContactStatus.Called, false, 2, null, null, null, "Không nghe máy 2 lần")]
    public void ResultText_ComputesExpectedText(
        CampaignContactStatus status, bool? callAnswered, int missedCallCount,
        DateTimeOffset? appointmentAt, DateTimeOffset? metAt, string? closedWorkTypeName, string expected)
    {
        var text = CampaignCustomerResultText.Compute(status, callAnswered, missedCallCount, appointmentAt, metAt, closedWorkTypeName);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void ResultText_AppointmentSet_ShowsVietnamLocalTime()
    {
        var at = new DateTimeOffset(2026, 12, 1, 2, 30, 0, TimeSpan.Zero);
        var text = CampaignCustomerResultText.Compute(CampaignContactStatus.AppointmentSet, null, 0, at, null, null);
        Assert.Equal("Đã hẹn gặp vào 01/12/2026 09:30", text);
    }

    [Fact]
    public void ResultText_Met_ShowsVietnamLocalTimeAcrossMidnight()
    {
        var at = new DateTimeOffset(2026, 12, 2, 20, 0, 0, TimeSpan.Zero);
        var text = CampaignCustomerResultText.Compute(CampaignContactStatus.Met, null, 0, null, at, null);
        Assert.Equal("Đã gặp vào 03/12/2026 03:00", text);
    }

    [Fact]
    public void ResultText_Closed_IncludesWorkTypeName()
    {
        var text = CampaignCustomerResultText.Compute(CampaignContactStatus.Closed, null, 0, null, null, "Tiết kiệm");
        Assert.Equal("Đã chốt được sản phẩm Tiết kiệm", text);
    }

    // ---- AllocateAsync --------------------------------------------------------------------------------------

    [Fact]
    public async Task AllocateAsync_RoundRobin_AssignsOnlyUnassignedInCifOrder()
    {
        var f = await CreateAsync();
        var c1 = NewCustomer(f.Campaign.Id, "CUST-003");
        var c2 = NewCustomer(f.Campaign.Id, "CUST-001");
        var c3 = NewCustomer(f.Campaign.Id, "CUST-002", assigneeId: f.Emp1.Id); // already assigned
        f.Db.CampaignCustomers.AddRange(c1, c2, c3);
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId);

        Assert.Equal(2, result.AssignedCount);
        Assert.Equal(1, result.ByEmployee.Single(e => e.EmployeeId == f.Emp1.Id).Count);
        Assert.Equal(1, result.ByEmployee.Single(e => e.EmployeeId == f.Emp2.Id).Count);

        var reloaded = await f.Db.CampaignCustomers.OrderBy(c => c.CustomerCif).ToListAsync();
        // CIF order: CUST-001, CUST-002 (already assigned), CUST-003 -> unassigned in CIF order are CUST-001 then CUST-003
        Assert.Equal(f.Emp1.Id, reloaded.First(c => c.CustomerCif == "CUST-001").AssigneeEmployeeId);
        Assert.Equal(f.Emp2.Id, reloaded.First(c => c.CustomerCif == "CUST-003").AssigneeEmployeeId);
        Assert.Equal(f.Emp1.Id, reloaded.First(c => c.CustomerCif == "CUST-002").AssigneeEmployeeId); // untouched
        Assert.All(reloaded.Where(c => c.CustomerCif != "CUST-002"), c => Assert.NotNull(c.AssignedAt));
    }

    [Fact]
    public async Task AllocateAsync_EqualSplit_GivesRemainderToFirstParticipants()
    {
        var f = await CreateAsync();
        for (var i = 0; i < 5; i++)
        {
            f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, $"CUST-{i:D3}"));
        }
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AllocateAsync(f.Campaign.Id, "EQUAL_SPLIT", null, f.ManagerAccountId);

        Assert.Equal(5, result.AssignedCount);
        // 5 split over 2 active participants -> 3/2, remainder to the first (emp1, ordered by EmployeeCode)
        Assert.Equal(3, result.ByEmployee.Single(e => e.EmployeeId == f.Emp1.Id).Count);
        Assert.Equal(2, result.ByEmployee.Single(e => e.EmployeeId == f.Emp2.Id).Count);
    }

    [Fact]
    public async Task AllocateAsync_LoadBalancing_PrefersParticipantWithFewerOpenCustomers()
    {
        var f = await CreateAsync();
        // emp1 already has 2 open customers, emp2 has 0 -> next pick should go to emp2 first
        f.Db.CampaignCustomers.AddRange(
            NewCustomer(f.Campaign.Id, "CUST-OLD-1", assigneeId: f.Emp1.Id),
            NewCustomer(f.Campaign.Id, "CUST-OLD-2", assigneeId: f.Emp1.Id),
            NewCustomer(f.Campaign.Id, "CUST-NEW-1"));
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AllocateAsync(f.Campaign.Id, "LOAD_BALANCING", null, f.ManagerAccountId);

        Assert.Equal(1, result.AssignedCount);
        Assert.Equal(f.Emp2.Id, result.ByEmployee.Single().EmployeeId);
    }

    [Fact]
    public async Task AllocateAsync_EmployeeIdsSubset_RestrictsParticipants()
    {
        var f = await CreateAsync();
        f.Db.CampaignCustomers.AddRange(
            NewCustomer(f.Campaign.Id, "CUST-001"),
            NewCustomer(f.Campaign.Id, "CUST-002"));
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", [f.Emp2.Id], f.ManagerAccountId);

        Assert.Equal(2, result.AssignedCount);
        Assert.Equal(2, result.ByEmployee.Single(e => e.EmployeeId == f.Emp2.Id).Count);
        Assert.DoesNotContain(result.ByEmployee, e => e.EmployeeId == f.Emp1.Id);
    }

    [Fact]
    public async Task AllocateAsync_InactiveParticipant_IsExcludedFromAllocation()
    {
        var f = await CreateAsync();
        var inactive = await f.Db.Employees.FirstAsync(e => e.Status == EmployeeStatus.Inactive);
        f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, "CUST-001"));
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId);

        Assert.DoesNotContain(result.ByEmployee, e => e.EmployeeId == inactive.Id);
    }

    [Fact]
    public async Task AllocateAsync_NoActiveParticipants_ThrowsValidationError()
    {
        var f = await CreateAsync(withParticipants: false);
        f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, "CUST-001"));
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId));
        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
    }

    [Fact]
    public async Task AllocateAsync_NoUnassignedCustomers_ReturnsZero_NotAnError()
    {
        var f = await CreateAsync();
        f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, "CUST-001", assigneeId: f.Emp1.Id));
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId);

        Assert.Equal(0, result.AssignedCount);
        Assert.Empty(result.ByEmployee);
    }

    [Fact]
    public async Task AllocateAsync_CampaignNotPlannedOrRunning_ThrowsCampaignNotRunning()
    {
        var f = await CreateAsync(campaignStatus: BusinessCampaignStatus.Completed);
        f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, "CUST-001"));
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId));
        Assert.Equal(ErrorCodes.CampaignNotRunning, ex.ErrorCode);
    }

    [Fact]
    public async Task AllocateAsync_UnknownCampaign_ThrowsNotFound()
    {
        var f = await CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(
            () => f.Service.AllocateAsync(Guid.NewGuid(), "ROUND_ROBIN", null, f.ManagerAccountId));
    }

    [Fact]
    public async Task AllocateAsync_CreatesOneLeadWorkPerCustomer_WithUniqueCodesAndNoDeadline()
    {
        var f = await CreateAsync();
        f.Db.CampaignCustomers.AddRange(
            NewCustomer(f.Campaign.Id, "CUST-001"),
            NewCustomer(f.Campaign.Id, "CUST-002"),
            NewCustomer(f.Campaign.Id, "CUST-003"));
        await f.Db.SaveChangesAsync();

        var result = await f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId);

        Assert.Equal(3, result.AssignedCount);
        var customers = await f.Db.CampaignCustomers.Include(c => c.Work).OrderBy(c => c.CustomerCif).ToListAsync();
        Assert.All(customers, c =>
        {
            Assert.NotNull(c.Work);
            Assert.Equal(WorkStatus.New, c.Work!.Status);
            Assert.Null(c.Work.PlannedEndDate);
            Assert.NotNull(c.Work.PlannedStartDate);
            Assert.Equal(c.AssigneeEmployeeId, c.Work.AssigneeEmployeeId);
            Assert.Equal(f.WorkType.Id, c.Work.WorkTypeId);
        });
        Assert.Equal(3, customers.Select(c => c.Work!.Code).Distinct().Count());
        var managerId = (await f.Db.Accounts.SingleAsync(a => a.Id == f.ManagerAccountId)).EmployeeId;
        Assert.All(customers, c => Assert.Equal(managerId, c.Work!.CreatedBy));
    }

    [Fact]
    public async Task AllocateAsync_UnknownCaller_ThrowsForbidden_AndSavesNothing()
    {
        var f = await CreateAsync();
        f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, "CUST-001"));
        await f.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, Guid.NewGuid()));
        Assert.False(await f.Db.Works.AnyAsync());
    }

    [Fact]
    public async Task AllocateAsync_SendsOneAggregateNotificationPerEmployee()
    {
        var notifier = new RecordingNotifier();
        var f = await CreateAsync();
        var service = new CampaignCustomerService(f.Db, TimeProvider.System, notifier);
        f.Db.CampaignCustomers.AddRange(
            NewCustomer(f.Campaign.Id, "CUST-001"),
            NewCustomer(f.Campaign.Id, "CUST-002"));
        await f.Db.SaveChangesAsync();

        await service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.Emp1.Id);

        // Round robin over 2 participants for 2 customers assigns one each; the creator (emp1) does not notify
        // themselves, so only emp2 gets a draft.
        var draft = Assert.Single(notifier.Drafts);
        Assert.Equal(f.Emp2.Id, draft.RecipientEmployeeId);
        Assert.Contains("khách hàng", draft.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllocateAsync_MoreThan2000Unassigned_ThrowsValidationError()
    {
        var f = await CreateAsync();
        for (var i = 0; i < 2001; i++)
        {
            f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, $"CUST-{i:D5}"));
        }

        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId));
        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
    }

    [Fact]
    public async Task AllocateAsync_WorkTypeNotLinkedToCampaign_ThrowsWorkTypeNotInCampaign()
    {
        var f = await CreateAsync();
        f.Db.CampaignWorkTypes.RemoveRange(f.Db.CampaignWorkTypes.Where(cwt => cwt.BusinessCampaignId == f.Campaign.Id));
        f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, "CUST-001"));
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", null, f.ManagerAccountId));
        Assert.Equal(ErrorCodes.WorkTypeNotInCampaign, ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    // ---- ReassignAsync --------------------------------------------------------------------------------------

    [Fact]
    public async Task ReassignAsync_Ok_UpdatesAssigneeAndReturnsDto()
    {
        var f = await CreateAsync();
        var customer = NewCustomer(f.Campaign.Id, "CUST-001", assigneeId: f.Emp1.Id);
        f.Db.CampaignCustomers.Add(customer);
        await f.Db.SaveChangesAsync();

        var dto = await f.Service.ReassignAsync(f.Campaign.Id, customer.Id, f.Emp2.Id, f.ManagerAccountId);

        Assert.Equal(f.Emp2.Id, dto.AssigneeEmployeeId);
        Assert.Equal(f.Emp2.FullName, dto.AssigneeEmployeeName);

        var reloaded = await f.Db.CampaignCustomers.FirstAsync(c => c.Id == customer.Id);
        Assert.Equal(f.Emp2.Id, reloaded.AssigneeEmployeeId);
        Assert.NotNull(reloaded.AssignedAt);
    }

    [Fact]
    public async Task ReassignAsync_ClosedCustomer_Throws409()
    {
        var f = await CreateAsync();
        var customer = NewCustomer(f.Campaign.Id, "CUST-001", assigneeId: f.Emp1.Id);
        customer.ContactStatus = CampaignContactStatus.Closed;
        f.Db.CampaignCustomers.Add(customer);
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.ReassignAsync(f.Campaign.Id, customer.Id, f.Emp2.Id, f.ManagerAccountId));
        Assert.Equal(ErrorCodes.CampaignCustomerClosed, ex.ErrorCode);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ReassignAsync_TargetNotActiveParticipant_ThrowsEmployeeNotParticipant()
    {
        var f = await CreateAsync();
        var customer = NewCustomer(f.Campaign.Id, "CUST-001", assigneeId: f.Emp1.Id);
        f.Db.CampaignCustomers.Add(customer);
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => f.Service.ReassignAsync(f.Campaign.Id, customer.Id, Guid.NewGuid(), f.ManagerAccountId));
        Assert.Equal(ErrorCodes.EmployeeNotParticipant, ex.ErrorCode);
    }

    [Fact]
    public async Task ReassignAsync_CustomerFromAnotherCampaign_ThrowsNotFound_NoIdor()
    {
        var f = await CreateAsync();
        // A row that belongs to a different campaign must 404 when addressed through f.Campaign.Id, even though
        // both the campaign and the row exist (IDOR check: the query must filter on both ids).
        var otherCampaignId = Guid.NewGuid();
        var customer = NewCustomer(otherCampaignId, "CUST-001", assigneeId: f.Emp1.Id);
        f.Db.CampaignCustomers.Add(customer);
        await f.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => f.Service.ReassignAsync(f.Campaign.Id, customer.Id, f.Emp2.Id, f.ManagerAccountId));
    }

    [Fact]
    public async Task ReassignAsync_UnknownCampaign_ThrowsNotFound()
    {
        var f = await CreateAsync();
        await Assert.ThrowsAsync<NotFoundException>(
            () => f.Service.ReassignAsync(Guid.NewGuid(), Guid.NewGuid(), f.Emp2.Id, f.ManagerAccountId));
    }

    [Fact]
    public async Task ReassignAsync_MovesTheLeadWork_AndRecordsActivity()
    {
        var f = await CreateAsync();
        f.Db.CampaignCustomers.Add(NewCustomer(f.Campaign.Id, "CUST-001"));
        await f.Db.SaveChangesAsync();
        await f.Service.AllocateAsync(f.Campaign.Id, "ROUND_ROBIN", [f.Emp1.Id], f.ManagerAccountId);
        var customer = await f.Db.CampaignCustomers.Include(c => c.Work).SingleAsync(c => c.CustomerCif == "CUST-001");
        var workId = customer.WorkId!.Value;
        Assert.Equal(f.Emp1.Id, customer.Work!.AssigneeEmployeeId);

        await f.Service.ReassignAsync(f.Campaign.Id, customer.Id, f.Emp2.Id, f.ManagerAccountId);

        var work = await f.Db.Works.SingleAsync(w => w.Id == workId);
        Assert.Equal(f.Emp2.Id, work.AssigneeEmployeeId);
        Assert.Equal(1, await f.Db.WorkActivities.CountAsync(a => a.WorkId == workId && a.Action == "ASSIGNED"));
    }
}
