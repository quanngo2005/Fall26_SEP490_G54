using System.Text.Json;

namespace G54.UnitTests;

/// <summary>UC83: view, create, update, activate / deactivate and delete a work type through the real API.</summary>
[Collection(ObjectiveKpiTargetDefinition.Name)]
public sealed class WorkTypeHttpTests(ObjectiveKpiTargetFixture api)
{
    private static string NewCode() => $"WT_{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private static object Kpi(Guid id, decimal multiplier = 1.5m) => new
    {
        kpiDefinitionId = id,
        isRequired = true,
        multiplier,
        effectiveFrom = "2031-01-01",
    };

    private static object Body(string? code = null, string name = "Huy động tiền gửi", string? productCode = null, params object[] kpis) => new
    {
        code = code ?? NewCode(),
        name,
        description = "Loại công việc huy động",
        productCode,
        kpis,
    };

    private async Task<Guid> CreateAsync(string? code = null, string? productCode = null, params object[] kpis)
    {
        var result = await api.SendAsync(null, HttpMethod.Post, "/work-types", "admin", Body(code, productCode: productCode, kpis: kpis));
        Assert.Equal(201, result.Code);
        return result.Id;
    }

    // ---- UC83 Manage work types ----
    [Fact]
    public async Task UC83_01_ViewWorkTypes_ListAndDetailForAnyAuthenticatedAccount()
    {
        var id = await CreateAsync();
        var list = await api.SendAsync("UC83-01", HttpMethod.Get, "/work-types?pageSize=100", "emp1");
        var detail = await api.SendAsync("UC83-01", HttpMethod.Get, $"/work-types/{id}", "emp1");
        var missing = await api.SendAsync("UC83-01", HttpMethod.Get, $"/work-types/{Guid.NewGuid()}", "emp1");
        Assert.Equal(200, list.Code);
        Assert.Contains(list.Data.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal(200, detail.Code);
        Assert.Equal("ACTIVE", detail.Data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, detail.Data.GetProperty("kpis").ValueKind);
        Assert.Equal(404, missing.Code);
        Assert.Equal("NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task UC83_02_ViewWorkTypes_FilterPageAndKpiOptions()
    {
        var code = NewCode();
        var id = await CreateAsync(code);
        await api.SendAsync(null, HttpMethod.Patch, $"/work-types/{await CreateAsync()}/status", "admin", new { status = "INACTIVE" });
        var byKeyword = await api.SendAsync("UC83-02", HttpMethod.Get, $"/work-types?keyword={code.ToLowerInvariant()}", "admin");
        var inactive = await api.SendAsync("UC83-02", HttpMethod.Get, "/work-types?status=INACTIVE&pageSize=100", "admin");
        var invalid = await api.SendAsync("UC83-02", HttpMethod.Get, "/work-types?status=BOGUS", "admin");
        var clamped = await api.SendAsync("UC83-02", HttpMethod.Get, "/work-types?pageSize=1000", "admin");
        var kpis = await api.SendAsync("UC83-02", HttpMethod.Get, "/work-types/kpis", "admin");
        Assert.Equal(id, Assert.Single(byKeyword.Data.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.All(inactive.Data.GetProperty("items").EnumerateArray(), i => Assert.Equal("INACTIVE", i.GetProperty("status").GetString()));
        Assert.Equal(400, invalid.Code);
        Assert.Equal("VALIDATION_ERROR", invalid.ErrorCode);
        Assert.Equal(100, clamped.Data.GetProperty("pageSize").GetInt32());
        Assert.Equal(200, kpis.Code);
        Assert.Equal(JsonValueKind.Array, kpis.Data.ValueKind);
    }

    [Fact]
    public async Task UC83_03_CreateWorkType_Returns201WithKpiMapping()
    {
        var kpi = await api.NewKpiAsync();
        var result = await api.SendAsync("UC83-03", HttpMethod.Post, "/work-types", "admin", Body(productCode: $"P{Guid.NewGuid():N}"[..10], kpis: Kpi(kpi)));
        Assert.Equal(201, result.Code);
        Assert.NotNull(result.Location);
        Assert.Equal("ACTIVE", result.Data.GetProperty("status").GetString());
        var mapping = Assert.Single(result.Data.GetProperty("kpis").EnumerateArray());
        Assert.Equal(kpi, mapping.GetProperty("kpiDefinitionId").GetGuid());
        Assert.Equal(1.5m, mapping.GetProperty("multiplier").GetDecimal());
    }

    [Fact]
    public async Task UC83_04_CreateWorkType_DuplicateCodeOrProductCode_Returns409()
    {
        var code = NewCode();
        var product = $"P{Guid.NewGuid():N}"[..10];
        await CreateAsync(code, product);
        var duplicateCode = await api.SendAsync("UC83-04", HttpMethod.Post, "/work-types", "admin", Body(code));
        var duplicateProduct = await api.SendAsync("UC83-04", HttpMethod.Post, "/work-types", "admin", Body(productCode: product));
        Assert.Equal(409, duplicateCode.Code);
        Assert.Equal("CONFLICT", duplicateCode.ErrorCode);
        Assert.Equal(409, duplicateProduct.Code);
    }

    [Fact]
    public async Task UC83_05_CreateWorkType_InvalidFields_Returns400()
    {
        var kpi = await api.NewKpiAsync();
        var noName = await api.SendAsync("UC83-05", HttpMethod.Post, "/work-types", "admin", Body(name: ""));
        var zeroMultiplier = await api.SendAsync("UC83-05", HttpMethod.Post, "/work-types", "admin", Body(kpis: Kpi(kpi, 0m)));
        var bigMultiplier = await api.SendAsync("UC83-05", HttpMethod.Post, "/work-types", "admin", Body(kpis: Kpi(kpi, 11m)));
        var unknownKpi = await api.SendAsync("UC83-05", HttpMethod.Post, "/work-types", "admin", Body(kpis: Kpi(Guid.NewGuid())));
        Assert.All(new[] { noName, zeroMultiplier, bigMultiplier }, r =>
        {
            Assert.Equal(400, r.Code);
            Assert.Equal("VALIDATION_ERROR", r.ErrorCode);
        });
        Assert.InRange(unknownKpi.Code, 400, 404);
    }

    [Fact]
    public async Task UC83_06_UpdateWorkType_ReplacesFieldsAndKpis()
    {
        var kpi = await api.NewKpiAsync();
        var id = await CreateAsync();
        var code = NewCode();
        var result = await api.SendAsync("UC83-06", HttpMethod.Put, $"/work-types/{id}", "admin", Body(code, "Tên đã sửa", kpis: Kpi(kpi, 2m)));
        var other = await CreateAsync(NewCode());
        var duplicate = await api.SendAsync("UC83-06", HttpMethod.Put, $"/work-types/{other}", "admin", Body(code));
        var missing = await api.SendAsync("UC83-06", HttpMethod.Put, $"/work-types/{Guid.NewGuid()}", "admin", Body());
        Assert.Equal(200, result.Code);
        Assert.Equal("Tên đã sửa", result.Data.GetProperty("name").GetString());
        Assert.Equal(code, result.Data.GetProperty("code").GetString());
        Assert.Equal(2m, Assert.Single(result.Data.GetProperty("kpis").EnumerateArray()).GetProperty("multiplier").GetDecimal());
        Assert.Equal(409, duplicate.Code);
        Assert.Equal(404, missing.Code);
    }

    [Fact]
    public async Task UC83_07_ChangeStatus_ActivateDeactivateAndInvalidValues()
    {
        var id = await CreateAsync();
        var inactive = await api.SendAsync("UC83-07", HttpMethod.Patch, $"/work-types/{id}/status", "admin", new { status = "inactive" });
        var detail = await api.SendAsync("UC83-07", HttpMethod.Get, $"/work-types/{id}", "admin");
        var bogus = await api.SendAsync("UC83-07", HttpMethod.Patch, $"/work-types/{id}/status", "admin", new { status = "BOGUS" });
        var ordinal = await api.SendAsync("UC83-07", HttpMethod.Patch, $"/work-types/{id}/status", "admin", new { status = "1" });
        var blank = await api.SendAsync("UC83-07", HttpMethod.Patch, $"/work-types/{id}/status", "admin", new { status = "" });
        var missing = await api.SendAsync("UC83-07", HttpMethod.Patch, $"/work-types/{Guid.NewGuid()}/status", "admin", new { status = "ACTIVE" });
        Assert.Equal(200, inactive.Code);
        Assert.Equal("INACTIVE", detail.Data.GetProperty("status").GetString());
        Assert.All(new[] { bogus, ordinal, blank }, r =>
        {
            Assert.Equal(400, r.Code);
            Assert.Equal("VALIDATION_ERROR", r.ErrorCode);
        });
        Assert.Equal(404, missing.Code);
    }

    [Fact]
    public async Task UC83_08_DeleteWorkType_UnusedIsRemoved_UsedOrUnknownIsRefused()
    {
        var unused = await CreateAsync();
        var removed = await api.SendAsync("UC83-08", HttpMethod.Delete, $"/work-types/{unused}", "admin");
        var gone = await api.SendAsync("UC83-08", HttpMethod.Get, $"/work-types/{unused}", "admin");
        var inUse = await CreateAsync();
        await api.NewWorkAsync("emp1", workType: inUse);
        var used = await api.SendAsync("UC83-08", HttpMethod.Delete, $"/work-types/{inUse}", "admin");
        var missing = await api.SendAsync("UC83-08", HttpMethod.Delete, $"/work-types/{Guid.NewGuid()}", "admin");
        Assert.Equal(200, removed.Code);
        Assert.Equal(404, gone.Code);
        Assert.Equal(409, used.Code);
        Assert.Equal(404, missing.Code);
    }

    [Fact]
    public async Task UC83_09_ManageWorkTypes_Permissions()
    {
        var id = await CreateAsync();
        var anonymous = await api.SendAsync("UC83-09", HttpMethod.Post, "/work-types", "none", Body());
        var create = await api.SendAsync("UC83-09", HttpMethod.Post, "/work-types", "emp1", Body());
        var update = await api.SendAsync("UC83-09", HttpMethod.Put, $"/work-types/{id}", "emp1", Body());
        var status = await api.SendAsync("UC83-09", HttpMethod.Patch, $"/work-types/{id}/status", "emp1", new { status = "INACTIVE" });
        var delete = await api.SendAsync("UC83-09", HttpMethod.Delete, $"/work-types/{id}", "emp1");
        var listAnonymous = await api.SendAsync("UC83-09", HttpMethod.Get, "/work-types", "none");
        Assert.Equal(401, anonymous.Code);
        Assert.All(new[] { create, update, status, delete }, r =>
        {
            Assert.Equal(403, r.Code);
            Assert.Equal("FORBIDDEN", r.ErrorCode);
        });
        Assert.Equal(401, listAnonymous.Code);
    }
}
