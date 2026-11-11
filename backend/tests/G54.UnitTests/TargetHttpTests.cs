namespace G54.UnitTests;

/// <summary>UC43-UC44: assign a root target to a branch and split it to departments / employees through the real API.</summary>
[Collection(ObjectiveKpiTargetDefinition.Name)]
public sealed class TargetHttpTests(ObjectiveKpiTargetFixture api)
{
    private object Root(Guid kpi, decimal value = 1000, string? reason = "Kế hoạch năm") => new
    {
        kpiDefinitionId = kpi,
        evaluationPeriodId = api.PeriodId,
        departmentId = api.BranchId,
        targetValue = value,
        changeReason = reason,
    };

    private async Task<(Guid Kpi, ApiResult Root)> NewRootAsync(decimal value = 1000)
    {
        var kpi = await api.NewKpiAsync();
        var result = await api.SendAsync(null, HttpMethod.Post, "/target-assignments", "admin", Root(kpi, value));
        Assert.Equal(201, result.Code);
        return (kpi, result);
    }

    private static object Split(params (Guid? Dept, Guid? Employee, decimal Value)[] rows) => new
    {
        allocations = rows.Select(r => new { departmentId = r.Dept, employeeId = r.Employee, targetValue = r.Value }).ToArray(),
        changeReason = "Phân bổ",
    };

    // ---- UC43 Assign a root target to a branch ----
    [Fact]
    public async Task UC43_01_AssignRootTarget_Returns201Active()
    {
        var kpi = await api.NewKpiAsync();
        var result = await api.SendAsync("UC43-01", HttpMethod.Post, "/target-assignments", "admin", Root(kpi));
        Assert.Equal(201, result.Code);
        Assert.NotNull(result.Location);
        Assert.Equal("ACTIVE", result.Data.GetProperty("status").GetString());
        Assert.Equal(1, result.Data.GetProperty("version").GetInt32());
        Assert.Equal(1000m, result.Data.GetProperty("targetValue").GetDecimal());
        Assert.Equal(api.BranchId, result.Data.GetProperty("departmentId").GetGuid());
    }

    [Fact]
    public async Task UC43_02_AssignRootTarget_InvalidBody_Returns400()
    {
        var kpi = await api.NewKpiAsync();
        var noValue = await api.SendAsync("UC43-02", HttpMethod.Post, "/target-assignments", "admin",
            new { kpiDefinitionId = kpi, evaluationPeriodId = api.PeriodId, departmentId = api.BranchId });
        var noKpi = await api.SendAsync("UC43-02", HttpMethod.Post, "/target-assignments", "admin",
            new { evaluationPeriodId = api.PeriodId, departmentId = api.BranchId, targetValue = 5 });
        var longReason = await api.SendAsync("UC43-02", HttpMethod.Post, "/target-assignments", "admin", Root(kpi, reason: new string('x', 1001)));
        Assert.Equal(400, noValue.Code);
        Assert.Equal("VALIDATION_ERROR", noValue.ErrorCode);
        Assert.Equal(400, noKpi.Code);
        Assert.Equal(400, longReason.Code);
    }

    [Fact]
    public async Task UC43_03_ViewTarget_ByIdAndList()
    {
        var (kpi, created) = await NewRootAsync();
        var byId = await api.SendAsync("UC43-03", HttpMethod.Get, $"/target-assignments/{created.Id}", "admin");
        var list = await api.SendAsync("UC43-03", HttpMethod.Get, $"/target-assignments?kpiDefinitionId={kpi}", "admin");
        var missing = await api.SendAsync("UC43-03", HttpMethod.Get, $"/target-assignments/{Guid.NewGuid()}", "admin");
        Assert.Equal(200, byId.Code);
        Assert.Equal(created.Id, byId.Id);
        Assert.Equal(created.Id, Assert.Single(list.Data.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(404, missing.Code);
        Assert.Equal("NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task UC43_04_AssignRootTarget_DuplicateForSameKpiPeriodAndBranch_IsRejectedWithoutServerError()
    {
        var (kpi, _) = await NewRootAsync();
        var again = await api.SendAsync("UC43-04", HttpMethod.Post, "/target-assignments", "admin", Root(kpi, 500));
        Assert.Equal(409, again.Code);
    }

    [Fact]
    public async Task UC43_05_AssignRootTarget_UnknownKpiNegativeValueOrNonBranchDepartment_IsRejectedWithoutServerError()
    {
        var kpi = await api.NewKpiAsync();
        var unknownKpi = await api.SendAsync("UC43-05", HttpMethod.Post, "/target-assignments", "admin", Root(Guid.NewGuid()));
        var negative = await api.SendAsync("UC43-05", HttpMethod.Post, "/target-assignments", "admin", Root(kpi, -5));
        var notBranch = await api.SendAsync("UC43-05", HttpMethod.Post, "/target-assignments", "admin", new
        {
            kpiDefinitionId = kpi,
            evaluationPeriodId = api.PeriodId,
            departmentId = api.DeptAId,
            targetValue = 10,
        });
        var unknownDepartment = await api.SendAsync("UC43-05", HttpMethod.Post, "/target-assignments", "admin", new
        {
            kpiDefinitionId = kpi,
            evaluationPeriodId = api.PeriodId,
            departmentId = Guid.NewGuid(),
            targetValue = 10,
        });
        Assert.InRange(unknownKpi.Code, 400, 499);
        Assert.InRange(negative.Code, 400, 499);
        Assert.Equal(400, notBranch.Code);
        Assert.Equal("VALIDATION_ERROR", notBranch.ErrorCode);
        Assert.Equal(404, unknownDepartment.Code);
    }

    [Fact]
    public async Task UC43_06_AssignRootTarget_Permissions()
    {
        var kpi = await api.NewKpiAsync();
        var anonymous = await api.SendAsync("UC43-06", HttpMethod.Post, "/target-assignments", "none", Root(kpi));
        var outsider = await api.SendAsync("UC43-06", HttpMethod.Post, "/target-assignments", "outsider", Root(kpi));
        var anonymousView = await api.SendAsync("UC43-06", HttpMethod.Get, "/target-assignments", "none");
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
        Assert.Equal("FORBIDDEN", outsider.ErrorCode);
        Assert.Equal(401, anonymousView.Code);
    }

    // ---- UC44 Allocate a target to departments / employees ----
    [Fact]
    public async Task UC44_01_SplitToDepartments_Returns201WithChildren()
    {
        var (_, root) = await NewRootAsync();
        var result = await api.SendAsync("UC44-01", HttpMethod.Post, $"/target-assignments/{root.Id}/allocations", "admin",
            Split((api.DeptAId, null, 600), (api.DeptBId, null, 400)));
        Assert.Equal(201, result.Code);
        Assert.NotNull(result.Location);
        var children = result.Data.EnumerateArray().ToList();
        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.Equal(root.Id, c.GetProperty("parentAssignmentId").GetGuid()));
        Assert.Equal(1000m, children.Sum(c => c.GetProperty("targetValue").GetDecimal()));
    }

    [Fact]
    public async Task UC44_02_SplitDepartmentTargetToEmployee_Returns201()
    {
        var (_, root) = await NewRootAsync();
        var departments = await api.SendAsync(null, HttpMethod.Post, $"/target-assignments/{root.Id}/allocations", "admin",
            Split((api.DeptAId, null, 600), (api.DeptBId, null, 400)));
        var deptA = departments.Data.EnumerateArray().First(c => c.GetProperty("departmentId").GetGuid() == api.DeptAId).GetProperty("id").GetGuid();
        var result = await api.SendAsync("UC44-02", HttpMethod.Post, $"/target-assignments/{deptA}/allocations", "admin",
            Split((null, api.MemberId, 600)));
        Assert.Equal(201, result.Code);
        Assert.Equal(api.MemberId, Assert.Single(result.Data.EnumerateArray()).GetProperty("employeeId").GetGuid());
    }

    [Fact]
    public async Task UC44_03_Split_InvalidRows_Returns400()
    {
        var (_, root) = await NewRootAsync();
        var url = $"/target-assignments/{root.Id}/allocations";
        var both = await api.SendAsync("UC44-03", HttpMethod.Post, url, "admin", Split((api.DeptAId, api.MemberId, 1000)));
        var duplicate = await api.SendAsync("UC44-03", HttpMethod.Post, url, "admin", Split((api.DeptAId, null, 500), (api.DeptAId, null, 500)));
        var empty = await api.SendAsync("UC44-03", HttpMethod.Post, url, "admin", new { allocations = Array.Empty<object>() });
        Assert.Equal(400, both.Code);
        Assert.Equal("VALIDATION_ERROR", both.ErrorCode);
        Assert.Equal(400, duplicate.Code);
        Assert.Equal(400, empty.Code);
    }

    [Fact]
    public async Task UC44_04_SplitToEmployee_UnknownInactiveOrOutsideTree()
    {
        var (_, root) = await NewRootAsync();
        var departments = await api.SendAsync(null, HttpMethod.Post, $"/target-assignments/{root.Id}/allocations", "admin",
            Split((api.DeptAId, null, 600), (api.DeptBId, null, 400)));
        var deptA = departments.Data.EnumerateArray().First(c => c.GetProperty("departmentId").GetGuid() == api.DeptAId).GetProperty("id").GetGuid();
        var url = $"/target-assignments/{deptA}/allocations";
        var unknown = await api.SendAsync("UC44-04", HttpMethod.Post, url, "admin", Split((null, Guid.NewGuid(), 600)));
        var inactive = await api.SendAsync("UC44-04", HttpMethod.Post, url, "admin", Split((null, api.InactiveMemberId, 600)));
        var outside = await api.SendAsync("UC44-04", HttpMethod.Post, url, "admin", Split((null, api.OtherBranchMemberId, 600)));
        Assert.Equal(404, unknown.Code);
        Assert.Equal("NOT_FOUND", unknown.ErrorCode);
        Assert.Equal(400, inactive.Code);
        Assert.Equal(400, outside.Code);
    }

    [Fact]
    public async Task UC44_05_Split_SumDifferentFromParent_IsRejectedWithoutServerError()
    {
        var (_, root) = await NewRootAsync();
        var result = await api.SendAsync("UC44-05", HttpMethod.Post, $"/target-assignments/{root.Id}/allocations", "admin",
            Split((api.DeptAId, null, 600), (api.DeptBId, null, 300)));
        Assert.InRange(result.Code, 400, 499);
    }

    [Fact]
    public async Task UC44_06_Reallocate_RequiresReasonAndCreatesNewVersion()
    {
        var (_, root) = await NewRootAsync();
        await api.SendAsync(null, HttpMethod.Post, $"/target-assignments/{root.Id}/allocations", "admin",
            Split((api.DeptAId, null, 600), (api.DeptBId, null, 400)));
        var url = $"/target-assignments/{root.Id}/allocations";
        var noReason = await api.SendAsync("UC44-06", HttpMethod.Put, url, "admin", new
        {
            allocations = new[] { new { departmentId = api.DeptAId, targetValue = 700m }, new { departmentId = api.DeptBId, targetValue = 300m } },
        });
        var result = await api.SendAsync("UC44-06", HttpMethod.Put, url, "admin", new
        {
            allocations = new[] { new { departmentId = api.DeptAId, targetValue = 700m }, new { departmentId = api.DeptBId, targetValue = 300m } },
            changeReason = "Điều chỉnh theo kế hoạch quý",
        });
        Assert.Equal(400, noReason.Code);
        Assert.Equal("VALIDATION_ERROR", noReason.ErrorCode);
        Assert.Equal(200, result.Code);
        var values = result.Data.EnumerateArray().Select(c => c.GetProperty("targetValue").GetDecimal()).OrderBy(v => v).ToList();
        Assert.Equal([300m, 700m], values);
        Assert.All(result.Data.EnumerateArray(), c => Assert.Equal(2, c.GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task UC44_07_Allocate_Permissions()
    {
        var (_, root) = await NewRootAsync();
        var body = Split((api.DeptAId, null, 600), (api.DeptBId, null, 400));
        var anonymous = await api.SendAsync("UC44-07", HttpMethod.Post, $"/target-assignments/{root.Id}/allocations", "none", body);
        var outsider = await api.SendAsync("UC44-07", HttpMethod.Post, $"/target-assignments/{root.Id}/allocations", "outsider", body);
        var unknown = await api.SendAsync("UC44-07", HttpMethod.Post, $"/target-assignments/{Guid.NewGuid()}/allocations", "admin", body);
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
        Assert.Equal(404, unknown.Code);
    }
}
