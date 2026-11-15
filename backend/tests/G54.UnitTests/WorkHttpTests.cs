using G54.DAL.Entities;

namespace G54.UnitTests;

/// <summary>UC72-UC75: create, view, change the status of a work and attach transaction codes to it, through the real API.</summary>
[Collection(ObjectiveKpiTargetDefinition.Name)]
public sealed class WorkHttpTests(ObjectiveKpiTargetFixture api)
{
    private static readonly string[] AllowedStatuses = ["InProgress", "Cancelled", "IN_PROGRESS", "CANCELLED"];

    private object Body(string? who = "emp1", string title = "Liên hệ khách hàng tiềm năng đợt 1", Guid? workType = null, Guid? assignee = null,
        string? priority = "High", string start = "2031-02-01", string end = "2031-02-28", decimal hours = 16.5m) => new
        {
            title,
            workTypeId = workType ?? api.WorkTypeId,
            assigneeEmployeeId = assignee ?? api.EmployeeIdOf(who!),
            priority,
            plannedStartDate = start,
            plannedEndDate = end,
            estimatedHours = hours,
            description = "Gọi điện tư vấn gói dịch vụ",
        };

    private static object Status(string status, string? reason = null) => new { status, reason };

    // ---- UC72 Create a work ----
    [Fact]
    public async Task UC72_01_CreateWork_Returns201New()
    {
        var result = await api.SendAsync("UC72-01", HttpMethod.Post, "/works", "mgr", Body());
        Assert.Equal(201, result.Code);
        Assert.NotNull(result.Location);
        Assert.Equal("New", result.Data.GetProperty("status").GetString());
        Assert.Equal("High", result.Data.GetProperty("priority").GetString());
        Assert.Equal(api.EmployeeIdOf("emp1"), result.Data.GetProperty("assigneeEmployeeId").GetGuid());
        Assert.StartsWith("WRK-", result.Data.GetProperty("code").GetString());
        Assert.Equal(16.5m, result.Data.GetProperty("estimatedHours").GetDecimal());
    }

    [Fact]
    public async Task UC72_02_CreateWork_PriorityAliasAndDefaults()
    {
        var alias = await api.SendAsync("UC72-02", HttpMethod.Post, "/works", "mgr", Body(priority: "p1"));
        var minimal = await api.SendAsync("UC72-02", HttpMethod.Post, "/works", "admin", new
        {
            title = "Việc tối thiểu",
            workTypeId = api.WorkTypeId,
            assigneeEmployeeId = api.EmployeeIdOf("emp2"),
        });
        Assert.Equal(201, alias.Code);
        Assert.Equal("Critical", alias.Data.GetProperty("priority").GetString());
        Assert.Equal(201, minimal.Code);
        Assert.Equal("Medium", minimal.Data.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task UC72_03_CreateWork_InvalidFields_Returns400()
    {
        var shortTitle = await api.SendAsync("UC72-03", HttpMethod.Post, "/works", "mgr", Body(title: "ab"));
        var endBeforeStart = await api.SendAsync("UC72-03", HttpMethod.Post, "/works", "mgr", Body(start: "2031-03-10", end: "2031-03-01"));
        var negativeHours = await api.SendAsync("UC72-03", HttpMethod.Post, "/works", "mgr", Body(hours: -1));
        var noWorkType = await api.SendAsync("UC72-03", HttpMethod.Post, "/works", "mgr", Body(workType: Guid.NewGuid()));
        var inactiveAssignee = await api.SendAsync("UC72-03", HttpMethod.Post, "/works", "mgr", Body(assignee: api.InactiveEmployeeId));
        Assert.All(new[] { shortTitle, endBeforeStart, negativeHours, noWorkType, inactiveAssignee }, r =>
        {
            Assert.Equal(400, r.Code);
            Assert.Equal("VALIDATION_ERROR", r.ErrorCode);
        });
    }

    [Fact]
    public async Task UC72_04_CreateWork_UnknownCampaignOrAssignee_Returns404()
    {
        var campaign = await api.SendAsync("UC72-04", HttpMethod.Post, "/works", "mgr", new
        {
            title = "Việc có chiến dịch",
            workTypeId = api.WorkTypeId,
            assigneeEmployeeId = api.EmployeeIdOf("emp1"),
            businessCampaignId = Guid.NewGuid(),
        });
        var assignee = await api.SendAsync("UC72-04", HttpMethod.Post, "/works", "mgr", Body(assignee: Guid.NewGuid()));
        Assert.Equal(404, campaign.Code);
        Assert.Equal("NOT_FOUND", campaign.ErrorCode);
        Assert.InRange(assignee.Code, 400, 404);
    }

    [Fact]
    public async Task UC72_05_CreateWork_Permissions()
    {
        var anonymous = await api.SendAsync("UC72-05", HttpMethod.Post, "/works", "none", Body());
        var employee = await api.SendAsync("UC72-05", HttpMethod.Post, "/works", "emp1", Body());
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, employee.Code);
        Assert.Equal("FORBIDDEN", employee.ErrorCode);
    }

    // ---- UC73 View works ----
    [Fact]
    public async Task UC73_01_ListWorks_ManagerSeesDepartmentWorksNewestFirst()
    {
        var first = await api.NewWorkAsync("emp1");
        var second = await api.NewWorkAsync("emp2");
        var result = await api.SendAsync("UC73-01", HttpMethod.Get, "/works?pageSize=100", "mgr");
        Assert.Equal(200, result.Code);
        Assert.Equal(1, result.Data.GetProperty("page").GetInt32());
        var ids = result.Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(first, ids);
        Assert.Contains(second, ids);
        Assert.True(ids.IndexOf(second) < ids.IndexOf(first), "newest update first");
    }

    [Fact]
    public async Task UC73_02_ListWorks_FilterByStatusAssigneePriorityAndKeyword()
    {
        var inProgress = await api.NewWorkAsync("emp1", WorkStatus.InProgress);
        await api.NewWorkAsync("emp1", WorkStatus.Cancelled);
        var byStatus = await api.SendAsync("UC73-02", HttpMethod.Get, "/works?status=in_progress,Cancelled&pageSize=100", "mgr");
        var byAssignee = await api.SendAsync("UC73-02", HttpMethod.Get, $"/works?assigneeEmployeeId={api.EmployeeIdOf("emp3")}", "mgr");
        var byKeyword = await api.SendAsync("UC73-02", HttpMethod.Get, "/works?keyword=" + Uri.EscapeDataString("LIÊN HỆ KHÁCH") + "&pageSize=100", "mgr");
        var byPriority = await api.SendAsync("UC73-02", HttpMethod.Get, "/works?priority=medium&pageSize=100", "mgr");
        var statuses = byStatus.Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("status").GetString()).Distinct().ToList();
        Assert.Contains(inProgress, byStatus.Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.All(statuses, s => Assert.Contains(s, AllowedStatuses));
        Assert.Empty(byAssignee.Data.GetProperty("items").EnumerateArray());
        Assert.NotEmpty(byKeyword.Data.GetProperty("items").EnumerateArray());
        Assert.NotEmpty(byPriority.Data.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task UC73_03_ListWorks_InvalidFilter_Returns400()
    {
        var status = await api.SendAsync("UC73-03", HttpMethod.Get, "/works?status=BOGUS", "mgr");
        var priority = await api.SendAsync("UC73-03", HttpMethod.Get, "/works?priority=BOGUS", "mgr");
        var department = await api.SendAsync("UC73-03", HttpMethod.Get, $"/works?departmentId={Guid.NewGuid()}", "mgr");
        Assert.Equal(400, status.Code);
        Assert.Equal("VALIDATION_ERROR", status.ErrorCode);
        Assert.Equal(400, priority.Code);
        Assert.Equal(404, department.Code);
    }

    [Fact]
    public async Task UC73_04_ListWorks_OwnScopeSeesOnlyOwnWorks()
    {
        var mine = await api.NewWorkAsync("emp1");
        var others = await api.NewWorkAsync("emp2");
        var result = await api.SendAsync("UC73-04", HttpMethod.Get, "/works?pageSize=100", "emp1");
        var ids = result.Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(200, result.Code);
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(others, ids);
    }

    [Fact]
    public async Task UC73_05_WorkDetail_Returns200Or404()
    {
        var id = await api.NewWorkAsync("emp1");
        var found = await api.SendAsync("UC73-05", HttpMethod.Get, $"/works/{id}", "emp1");
        var missing = await api.SendAsync("UC73-05", HttpMethod.Get, $"/works/{Guid.NewGuid()}", "emp1");
        Assert.Equal(200, found.Code);
        Assert.Equal(id, found.Id);
        Assert.Equal("New", found.Data.GetProperty("status").GetString());
        Assert.Equal(api.EmployeeIdOf("emp1"), found.Data.GetProperty("assigneeEmployeeId").GetGuid());
        Assert.Equal(404, missing.Code);
        Assert.Equal("NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task UC73_06_ListWorks_PagingAndPermissions()
    {
        var beyond = await api.SendAsync("UC73-06", HttpMethod.Get, "/works?page=999999", "mgr");
        var clamped = await api.SendAsync("UC73-06", HttpMethod.Get, "/works?pageSize=1000", "mgr");
        var anonymous = await api.SendAsync("UC73-06", HttpMethod.Get, "/works", "none");
        var outsider = await api.SendAsync("UC73-06", HttpMethod.Get, "/works", "outsider");
        Assert.Empty(beyond.Data.GetProperty("items").EnumerateArray());
        Assert.Equal(100, clamped.Data.GetProperty("pageSize").GetInt32());
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
    }

    // ---- UC74 Change the status of a work ----
    [Fact]
    public async Task UC74_01_AssigneeStartsWork_Returns200InProgress()
    {
        var id = await api.NewWorkAsync("emp1");
        var result = await api.SendAsync("UC74-01", HttpMethod.Patch, $"/works/{id}/status", "emp1", Status("InProgress"));
        Assert.Equal(200, result.Code);
        Assert.Equal("InProgress", result.Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UC74_02_ManagerCancelsWork_RequiresReason()
    {
        var id = await api.NewWorkAsync("emp1", WorkStatus.InProgress);
        var noReason = await api.SendAsync("UC74-02", HttpMethod.Patch, $"/works/{id}/status", "mgr", Status("Cancelled"));
        var result = await api.SendAsync("UC74-02", HttpMethod.Patch, $"/works/{id}/status", "mgr", Status("Cancelled", "Khách hàng huỷ nhu cầu"));
        Assert.Equal(400, noReason.Code);
        Assert.Equal("VALIDATION_ERROR", noReason.ErrorCode);
        Assert.Equal(200, result.Code);
        Assert.Equal("Cancelled", result.Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UC74_03_ManagerCompletesVerifiedWork_ButNotANewOne()
    {
        var verified = await api.NewWorkAsync("emp1", WorkStatus.Verified);
        var fresh = await api.NewWorkAsync("emp1");
        var done = await api.SendAsync("UC74-03", HttpMethod.Patch, $"/works/{verified}/status", "mgr", Status("Done"));
        var invalid = await api.SendAsync("UC74-03", HttpMethod.Patch, $"/works/{fresh}/status", "mgr", Status("Done"));
        Assert.Equal(200, done.Code);
        Assert.Equal("Done", done.Data.GetProperty("status").GetString());
        Assert.Equal(409, invalid.Code);
        Assert.Equal("WORK_INVALID_STATE", invalid.ErrorCode);
    }

    [Fact]
    public async Task UC74_04_OnlyTheRightPersonMayChangeTheStatus()
    {
        var id = await api.NewWorkAsync("emp1");
        var otherEmployee = await api.SendAsync("UC74-04", HttpMethod.Patch, $"/works/{id}/status", "emp2", Status("InProgress"));
        var employeeCancels = await api.SendAsync("UC74-04", HttpMethod.Patch, $"/works/{id}/status", "emp1", Status("Cancelled", "x"));
        var managerStarts = await api.SendAsync("UC74-04", HttpMethod.Patch, $"/works/{id}/status", "mgr", Status("InProgress"));
        Assert.Equal(403, otherEmployee.Code);
        Assert.Equal("FORBIDDEN", otherEmployee.ErrorCode);
        Assert.Equal(403, employeeCancels.Code);
        Assert.Equal(403, managerStarts.Code);
    }

    [Fact]
    public async Task UC74_05_SystemOnlyOrUnknownTargetStatus_Returns400()
    {
        var id = await api.NewWorkAsync("emp1");
        var submitted = await api.SendAsync("UC74-05", HttpMethod.Patch, $"/works/{id}/status", "emp1", Status("Submitted"));
        var bogus = await api.SendAsync("UC74-05", HttpMethod.Patch, $"/works/{id}/status", "emp1", Status("BOGUS"));
        var blank = await api.SendAsync("UC74-05", HttpMethod.Patch, $"/works/{id}/status", "emp1", Status(""));
        Assert.All(new[] { submitted, bogus, blank }, r =>
        {
            Assert.Equal(400, r.Code);
            Assert.Equal("VALIDATION_ERROR", r.ErrorCode);
        });
    }

    [Fact]
    public async Task UC74_06_SameStatusUnknownWorkAndNoToken()
    {
        var id = await api.NewWorkAsync("emp1", WorkStatus.InProgress);
        var same = await api.SendAsync("UC74-06", HttpMethod.Patch, $"/works/{id}/status", "emp1", Status("IN_PROGRESS"));
        var missing = await api.SendAsync("UC74-06", HttpMethod.Patch, $"/works/{Guid.NewGuid()}/status", "emp1", Status("InProgress"));
        var anonymous = await api.SendAsync("UC74-06", HttpMethod.Patch, $"/works/{id}/status", "none", Status("InProgress"));
        Assert.Equal(409, same.Code);
        Assert.Equal("WORK_INVALID_STATE", same.ErrorCode);
        Assert.Equal(404, missing.Code);
        Assert.Equal(401, anonymous.Code);
    }

    // ---- UC75 Attach transaction codes to a work ----
    [Fact]
    public async Task UC75_01_AssigneeAddsCodes_WorkMovesToSubmitted()
    {
        var id = await api.NewWorkAsync("emp1", WorkStatus.InProgress);
        var code = $"TX{Guid.NewGuid():N}"[..14];
        var result = await api.SendAsync("UC75-01", HttpMethod.Post, $"/works/{id}/transactions", "emp1", new { transactionCodes = new[] { code } });
        Assert.Equal(200, result.Code);
        Assert.Equal("SUBMITTED", result.Data.GetProperty("workStatus").GetString());
        var item = Assert.Single(result.Data.GetProperty("items").EnumerateArray());
        Assert.Equal(code, item.GetProperty("transactionCode").GetString());
        Assert.Equal("IN_REVIEW", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UC75_02_ListCodesOfAWork()
    {
        var id = await api.NewWorkAsync("emp1");
        var code = $"TX{Guid.NewGuid():N}"[..14];
        await api.SendAsync(null, HttpMethod.Post, $"/works/{id}/transactions", "emp1", new { transactionCodes = new[] { code } });
        var result = await api.SendAsync("UC75-02", HttpMethod.Get, $"/works/{id}/transactions", "emp1");
        Assert.Equal(200, result.Code);
        Assert.Equal(id, result.Data.GetProperty("workId").GetGuid());
        Assert.Equal(code, Assert.Single(result.Data.GetProperty("items").EnumerateArray()).GetProperty("transactionCode").GetString());
    }

    [Fact]
    public async Task UC75_03_DuplicateCodeAndLimitOfTen_Return409()
    {
        var first = await api.NewWorkAsync("emp1");
        var second = await api.NewWorkAsync("emp1");
        var code = $"TX{Guid.NewGuid():N}"[..14];
        await api.SendAsync(null, HttpMethod.Post, $"/works/{first}/transactions", "emp1", new { transactionCodes = new[] { code } });
        var duplicate = await api.SendAsync("UC75-03", HttpMethod.Post, $"/works/{second}/transactions", "emp1", new { transactionCodes = new[] { code } });
        var eleven = Enumerable.Range(0, 11).Select(i => $"L{i}{Guid.NewGuid():N}"[..14]).ToArray();
        var limit = await api.SendAsync("UC75-03", HttpMethod.Post, $"/works/{second}/transactions", "emp1", new { transactionCodes = eleven });
        Assert.Equal(409, duplicate.Code);
        Assert.Equal("WORK_TRANSACTION_DUPLICATE", duplicate.ErrorCode);
        Assert.Equal(409, limit.Code);
        Assert.Equal("WORK_TRANSACTION_LIMIT", limit.ErrorCode);
    }

    [Fact]
    public async Task UC75_04_RemoveACodeThatIsNotMatched()
    {
        var id = await api.NewWorkAsync("emp1");
        var code = $"TX{Guid.NewGuid():N}"[..14];
        var added = await api.SendAsync(null, HttpMethod.Post, $"/works/{id}/transactions", "emp1", new { transactionCodes = new[] { code } });
        var transactionId = added.Data.GetProperty("items")[0].GetProperty("id").GetGuid();
        var removed = await api.SendAsync("UC75-04", HttpMethod.Delete, $"/works/{id}/transactions/{transactionId}", "emp1");
        var again = await api.SendAsync("UC75-04", HttpMethod.Delete, $"/works/{id}/transactions/{transactionId}", "emp1");
        Assert.Equal(200, removed.Code);
        Assert.Empty(removed.Data.GetProperty("items").EnumerateArray());
        Assert.Equal(404, again.Code);
    }

    [Fact]
    public async Task UC75_05_InvalidBodyAndPermissions()
    {
        var id = await api.NewWorkAsync("emp1");
        var empty = await api.SendAsync("UC75-05", HttpMethod.Post, $"/works/{id}/transactions", "emp1", new { transactionCodes = Array.Empty<string>() });
        var tooLong = await api.SendAsync("UC75-05", HttpMethod.Post, $"/works/{id}/transactions", "emp1", new { transactionCodes = new List<string> { new('x', 101) } });
        var notAssignee = await api.SendAsync("UC75-05", HttpMethod.Post, $"/works/{id}/transactions", "emp2", new { transactionCodes = new List<string> { "ABC123" } });
        var unknown = await api.SendAsync("UC75-05", HttpMethod.Get, $"/works/{Guid.NewGuid()}/transactions", "emp1");
        var anonymous = await api.SendAsync("UC75-05", HttpMethod.Get, $"/works/{id}/transactions", "none");
        Assert.Equal(400, empty.Code);
        Assert.Equal("VALIDATION_ERROR", empty.ErrorCode);
        Assert.Equal(400, tooLong.Code);
        Assert.Equal(403, notAssignee.Code);
        Assert.Equal(404, unknown.Code);
        Assert.Equal(401, anonymous.Code);
    }
}
