using G54.DAL.Entities;

namespace G54.UnitTests;

/// <summary>UC76-UC82: handover recipients, request, accept, reject and the manager's approve / reassign / reject, through the real API.</summary>
[Collection(ObjectiveKpiTargetDefinition.Name)]
public sealed class HandoverHttpTests(ObjectiveKpiTargetFixture api)
{
    private object Request(string to = "emp2", string? reason = "Khách hàng ở khu vực nhóm 2", decimal amount = 120_000_000m, decimal? share = null, Guid? toId = null) => new
    {
        toEmployeeId = toId ?? api.EmployeeIdOf(to),
        reason,
        amount,
        referrerSharePercent = share,
    };

    private async Task<(Guid Work, Guid Handover)> PendingAsync(string to = "emp2")
    {
        var work = await api.NewWorkAsync("emp1", WorkStatus.InProgress);
        return (work, await api.NewPendingHandoverAsync(work, "emp1", to));
    }

    private async Task<(Guid Assignee, string Status)> WorkStateAsync(Guid work)
    {
        var detail = await api.SendAsync(null, HttpMethod.Get, $"/works/{work}", "mgr");
        return (detail.Data.GetProperty("assigneeEmployeeId").GetGuid(), detail.Data.GetProperty("status").GetString()!);
    }

    // ---- UC76 View handover recipients ----
    [Fact]
    public async Task UC76_01_Recipients_AreActiveColleaguesOfTheSameDepartmentSortedByName()
    {
        var result = await api.SendAsync("UC76-01", HttpMethod.Get, "/handovers/recipients", "emp1");
        Assert.Equal(200, result.Code);
        var items = result.Data.EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(api.EmployeeIdOf("emp2"), ids);
        Assert.Contains(api.EmployeeIdOf("emp3"), ids);
        Assert.Contains(api.EmployeeIdOf("mgr"), ids);
        Assert.DoesNotContain(api.EmployeeIdOf("emp1"), ids);
        Assert.DoesNotContain(api.InactiveEmployeeId, ids);
        var names = items.Select(i => i.GetProperty("fullName").GetString()!).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.GetProperty("employeeCode").GetString())));
    }

    [Fact]
    public async Task UC76_02_Recipients_Permissions()
    {
        var anonymous = await api.SendAsync("UC76-02", HttpMethod.Get, "/handovers/recipients", "none");
        var outsider = await api.SendAsync("UC76-02", HttpMethod.Get, "/handovers/recipients", "outsider");
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
        Assert.Equal("FORBIDDEN", outsider.ErrorCode);
    }

    // ---- UC77 Request a handover ----
    [Fact]
    public async Task UC77_01_EmployeeRequestsHandover_Returns201PendingAndKeepsTheWork()
    {
        var work = await api.NewWorkAsync("emp1", WorkStatus.InProgress);
        var result = await api.SendAsync("UC77-01", HttpMethod.Post, $"/works/{work}/handovers", "emp1", Request());
        Assert.Equal(201, result.Code);
        Assert.Equal("PENDING", result.Data.GetProperty("status").GetString());
        Assert.Equal(0m, result.Data.GetProperty("referrerSharePercent").GetDecimal());
        Assert.Equal(api.EmployeeIdOf("emp2"), result.Data.GetProperty("toEmployeeId").GetGuid());
        Assert.Equal((api.EmployeeIdOf("emp1"), "InProgress"), await WorkStateAsync(work));
    }

    [Fact]
    public async Task UC77_02_DirectManagerTransfersImmediately_WorkMovesToTheRecipient()
    {
        var work = await api.NewWorkAsync("emp1");
        var result = await api.SendAsync("UC77-02", HttpMethod.Post, $"/works/{work}/handovers", "mgr", Request(share: 40m));
        Assert.Equal(201, result.Code);
        Assert.Equal("ACCEPTED", result.Data.GetProperty("status").GetString());
        Assert.Equal(40m, result.Data.GetProperty("referrerSharePercent").GetDecimal());
        Assert.Equal((api.EmployeeIdOf("emp2"), "HandedOver"), await WorkStateAsync(work));
    }

    [Fact]
    public async Task UC77_03_InvalidRequest_Returns400()
    {
        var work = await api.NewWorkAsync("emp1");
        var url = $"/works/{work}/handovers";
        var self = await api.SendAsync("UC77-03", HttpMethod.Post, url, "emp1", Request("emp1"));
        var noReason = await api.SendAsync("UC77-03", HttpMethod.Post, url, "emp1", Request(reason: "  "));
        var noRecipient = await api.SendAsync("UC77-03", HttpMethod.Post, url, "emp1", new { reason = "x", amount = 1 });
        var negative = await api.SendAsync("UC77-03", HttpMethod.Post, url, "emp1", Request(amount: -1));
        var inactive = await api.SendAsync("UC77-03", HttpMethod.Post, url, "emp1", Request(toId: api.InactiveEmployeeId));
        var badShare = await api.SendAsync("UC77-03", HttpMethod.Post, url, "mgr", Request(share: 101m));
        Assert.All(new[] { self, noReason, noRecipient, negative, inactive, badShare }, r =>
        {
            Assert.Equal(400, r.Code);
            Assert.Equal("VALIDATION_ERROR", r.ErrorCode);
        });
    }

    [Fact]
    public async Task UC77_04_EmployeeCannotSetTheReferrerShare_Returns403()
    {
        var work = await api.NewWorkAsync("emp1");
        var result = await api.SendAsync("UC77-04", HttpMethod.Post, $"/works/{work}/handovers", "emp1", Request(share: 50m));
        Assert.Equal(403, result.Code);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
    }

    [Fact]
    public async Task UC77_05_AmountAboveTheThreshold_Returns422_ButTheThresholdItselfIsAllowed()
    {
        var work = await api.NewWorkAsync("emp1");
        var over = await api.SendAsync("UC77-05", HttpMethod.Post, $"/works/{work}/handovers", "emp1", Request(amount: 500_000_001m));
        var equal = await api.SendAsync("UC77-05", HttpMethod.Post, $"/works/{work}/handovers", "emp1", Request(amount: 500_000_000m));
        Assert.Equal(422, over.Code);
        Assert.Equal("HANDOVER_AMOUNT_EXCEEDS_THRESHOLD", over.ErrorCode);
        Assert.Equal(201, equal.Code);
    }

    [Fact]
    public async Task UC77_06_WrongWorkStateOrAlreadyPending_Returns409()
    {
        var submitted = await api.NewWorkAsync("emp1", WorkStatus.Submitted);
        var wrongState = await api.SendAsync("UC77-06", HttpMethod.Post, $"/works/{submitted}/handovers", "emp1", Request());
        var (work, _) = await PendingAsync();
        var second = await api.SendAsync("UC77-06", HttpMethod.Post, $"/works/{work}/handovers", "emp1", Request("emp3"));
        Assert.Equal(409, wrongState.Code);
        Assert.Equal("WORK_INVALID_STATE", wrongState.ErrorCode);
        Assert.Equal(409, second.Code);
        Assert.Equal("HANDOVER_ALREADY_PENDING", second.ErrorCode);
    }

    [Fact]
    public async Task UC77_07_NotTheAssigneeUnknownWorkAndNoToken()
    {
        var work = await api.NewWorkAsync("emp1");
        var notAssignee = await api.SendAsync("UC77-07", HttpMethod.Post, $"/works/{work}/handovers", "emp3", Request());
        var unknown = await api.SendAsync("UC77-07", HttpMethod.Post, $"/works/{Guid.NewGuid()}/handovers", "emp1", Request());
        var anonymous = await api.SendAsync("UC77-07", HttpMethod.Post, $"/works/{work}/handovers", "none", Request());
        Assert.Equal(403, notAssignee.Code);
        Assert.Equal(404, unknown.Code);
        Assert.Equal(401, anonymous.Code);
    }

    // ---- UC78 Accept a handover ----
    [Fact]
    public async Task UC78_01_RecipientAccepts_WorkMovesToTheRecipient()
    {
        var (work, handover) = await PendingAsync();
        var result = await api.SendAsync("UC78-01", HttpMethod.Post, $"/handovers/{handover}/accept", "emp2");
        Assert.Equal(200, result.Code);
        Assert.Equal("ACCEPTED", result.Data.GetProperty("status").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, result.Data.GetProperty("acceptedAt").ValueKind);
        Assert.Equal((api.EmployeeIdOf("emp2"), "HandedOver"), await WorkStateAsync(work));
    }

    [Fact]
    public async Task UC78_02_OnlyTheRecipientMayAccept()
    {
        var (_, handover) = await PendingAsync();
        var other = await api.SendAsync("UC78-02", HttpMethod.Post, $"/handovers/{handover}/accept", "emp3");
        var giver = await api.SendAsync("UC78-02", HttpMethod.Post, $"/handovers/{handover}/accept", "emp1");
        Assert.Equal(403, other.Code);
        Assert.Equal("FORBIDDEN", other.ErrorCode);
        Assert.Equal(403, giver.Code);
    }

    [Fact]
    public async Task UC78_03_AcceptTwice_Returns409NotPending()
    {
        var (_, handover) = await PendingAsync();
        await api.SendAsync(null, HttpMethod.Post, $"/handovers/{handover}/accept", "emp2");
        var again = await api.SendAsync("UC78-03", HttpMethod.Post, $"/handovers/{handover}/accept", "emp2");
        Assert.Equal(409, again.Code);
        Assert.Equal("HANDOVER_NOT_PENDING", again.ErrorCode);
    }

    [Fact]
    public async Task UC78_04_WorkNoLongerHandable_Returns409InvalidState()
    {
        var (work, handover) = await PendingAsync();
        await api.SendAsync(null, HttpMethod.Patch, $"/works/{work}/status", "mgr", new { status = "Cancelled", reason = "Huỷ" });
        var result = await api.SendAsync("UC78-04", HttpMethod.Post, $"/handovers/{handover}/accept", "emp2");
        Assert.Equal(409, result.Code);
        Assert.Equal("WORK_INVALID_STATE", result.ErrorCode);
    }

    [Fact]
    public async Task UC78_05_UnknownHandoverAndNoToken()
    {
        var unknown = await api.SendAsync("UC78-05", HttpMethod.Post, $"/handovers/{Guid.NewGuid()}/accept", "emp2");
        var anonymous = await api.SendAsync("UC78-05", HttpMethod.Post, $"/handovers/{Guid.NewGuid()}/accept", "none");
        Assert.Equal(404, unknown.Code);
        Assert.Equal("NOT_FOUND", unknown.ErrorCode);
        Assert.Equal(401, anonymous.Code);
    }

    // ---- UC79 Reject a handover ----
    [Fact]
    public async Task UC79_01_RecipientRejects_WorkStaysWithTheGiver()
    {
        var (work, handover) = await PendingAsync();
        var result = await api.SendAsync("UC79-01", HttpMethod.Post, $"/handovers/{handover}/reject", "emp2");
        Assert.Equal(200, result.Code);
        Assert.Equal("REJECTED", result.Data.GetProperty("status").GetString());
        Assert.Equal((api.EmployeeIdOf("emp1"), "InProgress"), await WorkStateAsync(work));
    }

    [Fact]
    public async Task UC79_02_AfterARejection_TheGiverCanRequestAgain()
    {
        var (work, handover) = await PendingAsync();
        await api.SendAsync(null, HttpMethod.Post, $"/handovers/{handover}/reject", "emp2");
        var again = await api.SendAsync("UC79-02", HttpMethod.Post, $"/works/{work}/handovers", "emp1", Request("emp3"));
        Assert.Equal(201, again.Code);
        Assert.Equal("PENDING", again.Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UC79_03_RejectPermissionsAndStates()
    {
        var (_, handover) = await PendingAsync();
        var other = await api.SendAsync("UC79-03", HttpMethod.Post, $"/handovers/{handover}/reject", "emp3");
        await api.SendAsync(null, HttpMethod.Post, $"/handovers/{handover}/reject", "emp2");
        var again = await api.SendAsync("UC79-03", HttpMethod.Post, $"/handovers/{handover}/reject", "emp2");
        var unknown = await api.SendAsync("UC79-03", HttpMethod.Post, $"/handovers/{Guid.NewGuid()}/reject", "emp2");
        var anonymous = await api.SendAsync("UC79-03", HttpMethod.Post, $"/handovers/{handover}/reject", "none");
        Assert.Equal(403, other.Code);
        Assert.Equal(409, again.Code);
        Assert.Equal("HANDOVER_NOT_PENDING", again.ErrorCode);
        Assert.Equal(404, unknown.Code);
        Assert.Equal(401, anonymous.Code);
    }

    // ---- UC80 Manager approves a handover ----
    [Fact]
    public async Task UC80_01_ManagerApproves_WorkMovesToTheRecipientWithoutTheirAnswer()
    {
        var (work, handover) = await PendingAsync();
        var result = await api.SendAsync("UC80-01", HttpMethod.Post, $"/handovers/{handover}/manager/approve", "mgr");
        Assert.Equal(200, result.Code);
        Assert.Equal("ACCEPTED", result.Data.GetProperty("status").GetString());
        Assert.Equal((api.EmployeeIdOf("emp2"), "HandedOver"), await WorkStateAsync(work));
    }

    [Fact]
    public async Task UC80_02_OnlyTheGiversDirectManagerMayApprove()
    {
        var (_, handover) = await PendingAsync();
        var recipient = await api.SendAsync("UC80-02", HttpMethod.Post, $"/handovers/{handover}/manager/approve", "emp2");
        var giver = await api.SendAsync("UC80-02", HttpMethod.Post, $"/handovers/{handover}/manager/approve", "emp1");
        var admin = await api.SendAsync("UC80-02", HttpMethod.Post, $"/handovers/{handover}/manager/approve", "admin");
        Assert.Equal(403, recipient.Code);
        Assert.Equal(403, giver.Code);
        Assert.Equal(403, admin.Code);
    }

    [Fact]
    public async Task UC80_03_ApproveStatesUnknownAndNoToken()
    {
        var (_, handover) = await PendingAsync();
        await api.SendAsync(null, HttpMethod.Post, $"/handovers/{handover}/manager/approve", "mgr");
        var again = await api.SendAsync("UC80-03", HttpMethod.Post, $"/handovers/{handover}/manager/approve", "mgr");
        var unknown = await api.SendAsync("UC80-03", HttpMethod.Post, $"/handovers/{Guid.NewGuid()}/manager/approve", "mgr");
        var anonymous = await api.SendAsync("UC80-03", HttpMethod.Post, $"/handovers/{handover}/manager/approve", "none");
        Assert.Equal(409, again.Code);
        Assert.Equal("HANDOVER_NOT_PENDING", again.ErrorCode);
        Assert.Equal(404, unknown.Code);
        Assert.Equal(401, anonymous.Code);
    }

    // ---- UC81 Manager reassigns a handover ----
    [Fact]
    public async Task UC81_01_ManagerReassigns_NewRecipientTakesTheWork()
    {
        var (work, handover) = await PendingAsync();
        var result = await api.SendAsync("UC81-01", HttpMethod.Post, $"/handovers/{handover}/manager/reassign", "mgr", new { toEmployeeId = api.EmployeeIdOf("emp3") });
        Assert.Equal(200, result.Code);
        Assert.Equal("ACCEPTED", result.Data.GetProperty("status").GetString());
        Assert.Equal(api.EmployeeIdOf("emp3"), result.Data.GetProperty("toEmployeeId").GetGuid());
        Assert.Equal((api.EmployeeIdOf("emp3"), "HandedOver"), await WorkStateAsync(work));
    }

    [Fact]
    public async Task UC81_02_ReassignToInvalidRecipient()
    {
        var (_, handover) = await PendingAsync();
        var url = $"/handovers/{handover}/manager/reassign";
        var missing = await api.SendAsync("UC81-02", HttpMethod.Post, url, "mgr", new { });
        var inactive = await api.SendAsync("UC81-02", HttpMethod.Post, url, "mgr", new { toEmployeeId = api.InactiveEmployeeId });
        var current = await api.SendAsync("UC81-02", HttpMethod.Post, url, "mgr", new { toEmployeeId = api.EmployeeIdOf("emp1") });
        var unknown = await api.SendAsync("UC81-02", HttpMethod.Post, url, "mgr", new { toEmployeeId = Guid.NewGuid() });
        Assert.All(new[] { missing, inactive, current }, r =>
        {
            Assert.Equal(400, r.Code);
            Assert.Equal("VALIDATION_ERROR", r.ErrorCode);
        });
        Assert.Equal(404, unknown.Code);
    }

    [Fact]
    public async Task UC81_03_ReassignPermissionsAndStates()
    {
        var (_, handover) = await PendingAsync();
        var body = new { toEmployeeId = api.EmployeeIdOf("emp3") };
        var recipient = await api.SendAsync("UC81-03", HttpMethod.Post, $"/handovers/{handover}/manager/reassign", "emp2", body);
        await api.SendAsync(null, HttpMethod.Post, $"/handovers/{handover}/manager/approve", "mgr");
        var notPending = await api.SendAsync("UC81-03", HttpMethod.Post, $"/handovers/{handover}/manager/reassign", "mgr", body);
        var anonymous = await api.SendAsync("UC81-03", HttpMethod.Post, $"/handovers/{handover}/manager/reassign", "none", body);
        Assert.Equal(403, recipient.Code);
        Assert.Equal(409, notPending.Code);
        Assert.Equal("HANDOVER_NOT_PENDING", notPending.ErrorCode);
        Assert.Equal(401, anonymous.Code);
    }

    // ---- UC82 Manager rejects a handover ----
    [Fact]
    public async Task UC82_01_ManagerRejects_WorkStaysWithTheGiver()
    {
        var (work, handover) = await PendingAsync();
        var result = await api.SendAsync("UC82-01", HttpMethod.Post, $"/handovers/{handover}/manager/reject", "mgr");
        Assert.Equal(200, result.Code);
        Assert.Equal("REJECTED", result.Data.GetProperty("status").GetString());
        Assert.Equal((api.EmployeeIdOf("emp1"), "InProgress"), await WorkStateAsync(work));
    }

    [Fact]
    public async Task UC82_02_RejectPermissionsStatesUnknownAndNoToken()
    {
        var (_, handover) = await PendingAsync();
        var recipient = await api.SendAsync("UC82-02", HttpMethod.Post, $"/handovers/{handover}/manager/reject", "emp2");
        var admin = await api.SendAsync("UC82-02", HttpMethod.Post, $"/handovers/{handover}/manager/reject", "admin");
        await api.SendAsync(null, HttpMethod.Post, $"/handovers/{handover}/manager/reject", "mgr");
        var again = await api.SendAsync("UC82-02", HttpMethod.Post, $"/handovers/{handover}/manager/reject", "mgr");
        var unknown = await api.SendAsync("UC82-02", HttpMethod.Post, $"/handovers/{Guid.NewGuid()}/manager/reject", "mgr");
        var anonymous = await api.SendAsync("UC82-02", HttpMethod.Post, $"/handovers/{handover}/manager/reject", "none");
        Assert.Equal(403, recipient.Code);
        Assert.Equal(403, admin.Code);
        Assert.Equal(409, again.Code);
        Assert.Equal("HANDOVER_NOT_PENDING", again.ErrorCode);
        Assert.Equal(404, unknown.Code);
        Assert.Equal(401, anonymous.Code);
    }
}
