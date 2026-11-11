namespace G54.UnitTests;

/// <summary>UC39-UC42: view, create, update/status change, approve and reject a KPI definition through the real API.</summary>
[Collection(ObjectiveKpiTargetDefinition.Name)]
public sealed class KpiHttpTests(ObjectiveKpiTargetFixture api)
{
    private object Body(string? name = null, string? code = null, string aggregationType = "SUM", string unit = "VND", Guid? group = null) => new
    {
        kpiGroupId = group ?? api.GroupId,
        code = code ?? string.Empty,
        name = name ?? $"Huy động vốn {Guid.NewGuid():N}"[..24],
        description = "KPI huy động vốn",
        formula = (string?)null,
        unit,
        aggregationType,
        direction = "HIGHER_BETTER",
        maxAchievementPercent = 150,
    };

    private async Task<Guid> CreateAsync(string? name = null, string who = "admin")
    {
        var result = await api.SendAsync(null, HttpMethod.Post, "/kpi-definitions", who, Body(name));
        Assert.Equal(201, result.Code);
        return result.Id;
    }

    private async Task SubmitAsync(Guid id, string who = "admin")
    {
        var result = await api.SendAsync(null, HttpMethod.Patch, $"/kpi-definitions/{id}/status", who, new { status = "PENDING_APPROVAL" });
        Assert.Equal(200, result.Code);
    }

    private async Task<string> StatusOfAsync(Guid id) =>
        (await api.SendAsync(null, HttpMethod.Get, $"/kpi-definitions/{id}", "admin")).Data.GetProperty("status").GetString()!;

    // ---- UC39 View KPI definitions ----
    [Fact]
    public async Task UC39_01_ListKpis_ReturnsPagedResult()
    {
        var id = await CreateAsync();
        var result = await api.SendAsync("UC39-01", HttpMethod.Get, "/kpi-definitions?pageSize=100", "admin");
        Assert.Equal(200, result.Code);
        Assert.Equal(1, result.Data.GetProperty("page").GetInt32());
        Assert.Equal(100, result.Data.GetProperty("pageSize").GetInt32());
        Assert.True(result.Data.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Contains(result.Data.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task UC39_02_ListKpis_FilterByKeywordAndStatus()
    {
        var name = $"Lọc thử {Guid.NewGuid():N}"[..18];
        var id = await CreateAsync(name);
        var byKeyword = await api.SendAsync("UC39-02", HttpMethod.Get, $"/kpi-definitions?keyword={Uri.EscapeDataString(name.ToLowerInvariant())}", "admin");
        var byStatus = await api.SendAsync("UC39-02", HttpMethod.Get, "/kpi-definitions?status=DRAFT&pageSize=100", "admin");
        var invalid = await api.SendAsync("UC39-02", HttpMethod.Get, "/kpi-definitions?status=BOGUS", "admin");
        Assert.Equal(id, Assert.Single(byKeyword.Data.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.All(byStatus.Data.GetProperty("items").EnumerateArray(), i => Assert.Equal("DRAFT", i.GetProperty("status").GetString()));
        Assert.Equal(400, invalid.Code);
        Assert.Equal("VALIDATION_ERROR", invalid.ErrorCode);
    }

    [Fact]
    public async Task UC39_03_KpiDetail_Returns200Or404()
    {
        var id = await CreateAsync();
        var found = await api.SendAsync("UC39-03", HttpMethod.Get, $"/kpi-definitions/{id}", "admin");
        var missing = await api.SendAsync("UC39-03", HttpMethod.Get, $"/kpi-definitions/{Guid.NewGuid()}", "admin");
        Assert.Equal(200, found.Code);
        Assert.Equal("DRAFT", found.Data.GetProperty("status").GetString());
        Assert.Equal("HIGHER_BETTER", found.Data.GetProperty("direction").GetString());
        Assert.Equal(404, missing.Code);
        Assert.Equal("NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task UC39_04_KpiGroupsAndSummary()
    {
        await CreateAsync();
        var groups = await api.SendAsync("UC39-04", HttpMethod.Get, "/kpi-definitions/groups", "admin");
        var summary = await api.SendAsync("UC39-04", HttpMethod.Get, "/kpi-definitions/summary", "admin");
        Assert.Equal(200, groups.Code);
        Assert.Contains(groups.Data.EnumerateArray(), g => g.GetProperty("id").GetGuid() == api.GroupId);
        Assert.Equal(200, summary.Code);
        Assert.True(summary.Data.GetProperty("draft").GetInt32() >= 1);
        Assert.True(summary.Data.GetProperty("total").GetInt32() >= summary.Data.GetProperty("draft").GetInt32());
    }

    [Fact]
    public async Task UC39_05_KpiUsageAndHistory()
    {
        var id = await CreateAsync();
        var usage = await api.SendAsync("UC39-05", HttpMethod.Get, $"/kpi-definitions/{id}/usage", "admin");
        var history = await api.SendAsync("UC39-05", HttpMethod.Get, $"/kpi-definitions/{id}/history", "admin");
        var missing = await api.SendAsync("UC39-05", HttpMethod.Get, $"/kpi-definitions/{Guid.NewGuid()}/usage", "admin");
        Assert.Equal(200, usage.Code);
        Assert.Equal(0, usage.Data.GetProperty("activeTargetCount").GetInt32());
        Assert.Equal(200, history.Code);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, history.Data.ValueKind);
        Assert.Equal(404, missing.Code);
    }

    [Fact]
    public async Task UC39_06_CodeSuggestion_BuildsCodeFromName()
    {
        var suggestion = await api.SendAsync("UC39-06", HttpMethod.Get, $"/kpi-definitions/code-suggestion?name={Uri.EscapeDataString("Tiền gửi - giới thiệu " + api.Suffix)}", "admin");
        var empty = await api.SendAsync("UC39-06", HttpMethod.Get, "/kpi-definitions/code-suggestion?name=", "admin");
        Assert.Equal(200, suggestion.Code);
        Assert.StartsWith("TIEN_GUI_GIOI_THIEU_", suggestion.Data.GetProperty("code").GetString());
        Assert.Equal(400, empty.Code);
    }

    [Fact]
    public async Task UC39_07_ViewKpis_WithoutToken_Returns401()
    {
        var list = await api.SendAsync("UC39-07", HttpMethod.Get, "/kpi-definitions", "none");
        var summary = await api.SendAsync("UC39-07", HttpMethod.Get, "/kpi-definitions/summary", "outsider");
        Assert.Equal(401, list.Code);
        Assert.Equal(403, summary.Code);
    }

    // ---- UC40 Create KPI definition ----
    [Fact]
    public async Task UC40_01_CreateKpi_Returns201DraftWithGeneratedCode()
    {
        var result = await api.SendAsync("UC40-01", HttpMethod.Post, "/kpi-definitions", "admin", Body($"Tiền gửi {api.Suffix} kỳ hạn"));
        Assert.Equal(201, result.Code);
        Assert.NotNull(result.Location);
        Assert.Equal("DRAFT", result.Data.GetProperty("status").GetString());
        Assert.Equal($"TIEN_GUI_{api.Suffix}_KY_HAN", result.Data.GetProperty("code").GetString());
        Assert.Equal("SUM", result.Data.GetProperty("aggregationType").GetString());
    }

    [Fact]
    public async Task UC40_02_CreateKpi_DuplicateExplicitCode_Returns409()
    {
        var code = $"DUP_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var first = await api.SendAsync(null, HttpMethod.Post, "/kpi-definitions", "admin", Body(code: code));
        var second = await api.SendAsync("UC40-02", HttpMethod.Post, "/kpi-definitions", "admin", Body(code: code));
        Assert.Equal(201, first.Code);
        Assert.Equal(409, second.Code);
        Assert.Equal("CONFLICT", second.ErrorCode);
    }

    [Fact]
    public async Task UC40_03_CreateKpi_InvalidFields_Returns400()
    {
        var enumName = await api.SendAsync("UC40-03", HttpMethod.Post, "/kpi-definitions", "admin", Body(aggregationType: "Sum"));
        var ordinal = await api.SendAsync("UC40-03", HttpMethod.Post, "/kpi-definitions", "admin", Body(aggregationType: "1"));
        var noUnit = await api.SendAsync("UC40-03", HttpMethod.Post, "/kpi-definitions", "admin", Body(unit: ""));
        var noName = await api.SendAsync("UC40-03", HttpMethod.Post, "/kpi-definitions", "admin", Body(name: ""));
        Assert.Equal(400, ordinal.Code);
        Assert.Equal("VALIDATION_ERROR", ordinal.ErrorCode);
        Assert.Equal(400, noUnit.Code);
        Assert.Equal(400, noName.Code);
        Assert.True(enumName.Code is 201 or 400);
    }

    [Fact]
    public async Task UC40_04_CreateKpi_UnknownGroup_IsRejectedWithoutServerError()
    {
        var result = await api.SendAsync("UC40-04", HttpMethod.Post, "/kpi-definitions", "admin", Body(group: Guid.NewGuid()));
        Assert.InRange(result.Code, 400, 499);
    }

    [Fact]
    public async Task UC40_05_BranchManagerRequestsKpi_Returns201DraftWithNote()
    {
        var note = "Cần KPI mới cho mục tiêu huy động vốn";
        var result = await api.SendAsync("UC40-05", HttpMethod.Post, "/kpi-definitions/requests", "bm", new { kpi = Body(), requestNote = note });
        var noNote = await api.SendAsync("UC40-05", HttpMethod.Post, "/kpi-definitions/requests", "bm", new { kpi = Body(), requestNote = "" });
        Assert.Equal(201, result.Code);
        Assert.Equal("DRAFT", result.Data.GetProperty("status").GetString());
        Assert.Equal(note, result.Data.GetProperty("requestNote").GetString());
        Assert.Equal(400, noNote.Code);
        Assert.Equal("VALIDATION_ERROR", noNote.ErrorCode);
    }

    [Fact]
    public async Task UC40_06_CreateKpi_WithoutManagePermission_Returns401Or403()
    {
        var anonymous = await api.SendAsync("UC40-06", HttpMethod.Post, "/kpi-definitions", "none", Body());
        var manager = await api.SendAsync("UC40-06", HttpMethod.Post, "/kpi-definitions", "bm", Body());
        var outsiderRequest = await api.SendAsync("UC40-06", HttpMethod.Post, "/kpi-definitions/requests", "outsider", new { kpi = Body(), requestNote = "x" });
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, manager.Code);
        Assert.Equal(403, outsiderRequest.Code);
    }

    // ---- UC41 Update KPI definition and change its status ----
    [Fact]
    public async Task UC41_01_UpdateKpi_Returns200AndKeepsCode()
    {
        var created = await api.SendAsync(null, HttpMethod.Post, "/kpi-definitions", "admin", Body());
        var code = created.Data.GetProperty("code").GetString();
        var result = await api.SendAsync("UC41-01", HttpMethod.Put, $"/kpi-definitions/{created.Id}", "admin", Body("Tên KPI đã sửa", code, "AVG", "%"));
        Assert.Equal(200, result.Code);
        Assert.Equal("Tên KPI đã sửa", result.Data.GetProperty("name").GetString());
        Assert.Equal("AVG", result.Data.GetProperty("aggregationType").GetString());
        Assert.Equal(code, result.Data.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UC41_02_UpdateKpi_UnknownId_Returns404()
    {
        var result = await api.SendAsync("UC41-02", HttpMethod.Put, $"/kpi-definitions/{Guid.NewGuid()}", "admin", Body(code: "ZZ"));
        Assert.Equal(404, result.Code);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UC41_03_SubmitForApproval_ThenUpdateIsRejectedWith409()
    {
        var created = await api.SendAsync(null, HttpMethod.Post, "/kpi-definitions", "admin", Body());
        var code = created.Data.GetProperty("code").GetString();
        var submit = await api.SendAsync("UC41-03", HttpMethod.Patch, $"/kpi-definitions/{created.Id}/status", "admin", new { status = "PENDING_APPROVAL" });
        var update = await api.SendAsync("UC41-03", HttpMethod.Put, $"/kpi-definitions/{created.Id}", "admin", Body(code: code));
        Assert.Equal(200, submit.Code);
        Assert.Equal("PENDING_APPROVAL", await StatusOfAsync(created.Id));
        Assert.Equal(409, update.Code);
        Assert.Equal("CONFLICT", update.ErrorCode);
    }

    [Fact]
    public async Task UC41_04_InvalidStatusTransitions()
    {
        var id = await CreateAsync();
        var toActive = await api.SendAsync("UC41-04", HttpMethod.Patch, $"/kpi-definitions/{id}/status", "admin", new { status = "ACTIVE" });
        var bogus = await api.SendAsync("UC41-04", HttpMethod.Patch, $"/kpi-definitions/{id}/status", "admin", new { status = "BOGUS" });
        var same = await api.SendAsync("UC41-04", HttpMethod.Patch, $"/kpi-definitions/{id}/status", "admin", new { status = "DRAFT" });
        var archived = await api.SendAsync("UC41-04", HttpMethod.Patch, $"/kpi-definitions/{id}/status", "admin", new { status = "ARCHIVED" });
        var afterArchive = await api.SendAsync("UC41-04", HttpMethod.Patch, $"/kpi-definitions/{id}/status", "admin", new { status = "DRAFT" });
        Assert.Equal(409, toActive.Code);
        Assert.Equal("CONFLICT", toActive.ErrorCode);
        Assert.Equal(400, bogus.Code);
        Assert.Equal(200, same.Code);
        Assert.Equal(200, archived.Code);
        Assert.Equal(409, afterArchive.Code);
    }

    [Fact]
    public async Task UC41_05_UpdateOrChangeStatus_WithoutPermission_Returns401Or403()
    {
        var id = await CreateAsync();
        var anonymous = await api.SendAsync("UC41-05", HttpMethod.Put, $"/kpi-definitions/{id}", "none", Body(code: "X"));
        var manager = await api.SendAsync("UC41-05", HttpMethod.Put, $"/kpi-definitions/{id}", "bm", Body(code: "X"));
        var outsider = await api.SendAsync("UC41-05", HttpMethod.Patch, $"/kpi-definitions/{id}/status", "outsider", new { status = "PENDING_APPROVAL" });
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, manager.Code);
        Assert.Equal(403, outsider.Code);
    }

    // ---- UC42 Approve or reject a KPI definition ----
    [Fact]
    public async Task UC42_01_Approve_ByAnotherUser_MakesKpiActive()
    {
        var id = await CreateAsync();
        await SubmitAsync(id);
        var result = await api.SendAsync("UC42-01", HttpMethod.Post, $"/kpi-definitions/{id}/approve", "admin2");
        var detail = await api.SendAsync("UC42-01", HttpMethod.Get, $"/kpi-definitions/{id}", "admin2");
        Assert.Equal(200, result.Code);
        Assert.Equal("ACTIVE", detail.Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UC42_02_Approve_ByTheMaker_Returns422()
    {
        var id = await CreateAsync();
        await SubmitAsync(id);
        var result = await api.SendAsync("UC42-02", HttpMethod.Post, $"/kpi-definitions/{id}/approve", "admin");
        Assert.Equal(422, result.Code);
        Assert.Equal("MAKER_CANNOT_BE_CHECKER", result.ErrorCode);
        Assert.Equal("PENDING_APPROVAL", await StatusOfAsync(id));
    }

    [Fact]
    public async Task UC42_03_Reject_RequiresReasonAndStoresIt()
    {
        var id = await CreateAsync();
        await SubmitAsync(id);
        var noReason = await api.SendAsync("UC42-03", HttpMethod.Post, $"/kpi-definitions/{id}/reject", "admin2", new { reason = "" });
        var result = await api.SendAsync("UC42-03", HttpMethod.Post, $"/kpi-definitions/{id}/reject", "admin2", new { reason = "Công thức chưa rõ" });
        var detail = await api.SendAsync("UC42-03", HttpMethod.Get, $"/kpi-definitions/{id}", "admin2");
        Assert.Equal(400, noReason.Code);
        Assert.Equal("VALIDATION_ERROR", noReason.ErrorCode);
        Assert.Equal(200, result.Code);
        Assert.Equal("REJECTED", detail.Data.GetProperty("status").GetString());
        Assert.Equal("Công thức chưa rõ", detail.Data.GetProperty("rejectReason").GetString());
    }

    [Fact]
    public async Task UC42_04_ApproveOrReject_WhenNotPending_Returns409()
    {
        var id = await CreateAsync();
        var approve = await api.SendAsync("UC42-04", HttpMethod.Post, $"/kpi-definitions/{id}/approve", "admin2");
        var reject = await api.SendAsync("UC42-04", HttpMethod.Post, $"/kpi-definitions/{id}/reject", "admin2", new { reason = "x" });
        var missing = await api.SendAsync("UC42-04", HttpMethod.Post, $"/kpi-definitions/{Guid.NewGuid()}/approve", "admin2");
        Assert.Equal(409, approve.Code);
        Assert.Equal(409, reject.Code);
        Assert.Equal(404, missing.Code);
    }

    [Fact]
    public async Task UC42_05_Approve_Permissions()
    {
        var id = await CreateAsync();
        await SubmitAsync(id);
        var anonymous = await api.SendAsync("UC42-05", HttpMethod.Post, $"/kpi-definitions/{id}/approve", "none");
        var outsider = await api.SendAsync("UC42-05", HttpMethod.Post, $"/kpi-definitions/{id}/approve", "outsider");
        var branchManager = await api.SendAsync("UC42-05", HttpMethod.Post, $"/kpi-definitions/{id}/approve", "bm");
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
        Assert.Equal(200, branchManager.Code);
    }
}
