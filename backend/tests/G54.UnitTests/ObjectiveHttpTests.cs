using System.Net;
using System.Text.Json;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

/// <summary>UC35-UC38: create, update, view and delete a business objective through the real API.</summary>
[Collection(ObjectiveKpiTargetDefinition.Name)]
public sealed class ObjectiveHttpTests(ObjectiveKpiTargetFixture api)
{
    private static string NewCode() => $"OBJ-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static object Body(string? code = null, string name = "Tăng trưởng huy động vốn", string start = "2031-01-01",
        string end = "2031-12-31", Guid? parent = null) =>
        new { code = code ?? NewCode(), name, description = "Mục tiêu kinh doanh", startDate = start, endDate = end, parentObjectiveId = parent };

    private async Task<Guid> CreateAsync(Guid? parent = null, string? code = null)
    {
        var result = await api.SendAsync(null, HttpMethod.Post, "/business-objectives", "admin", Body(code, parent: parent));
        Assert.Equal(201, result.Code);
        return result.Id;
    }

    private Task SetStatusAsync(Guid id, BusinessObjectiveStatus status) => api.WithDbAsync(async db =>
    {
        var objective = await db.BusinessObjectives.FirstAsync(o => o.Id == id);
        objective.Status = status;
        await db.SaveChangesAsync();
    });

    // ---- UC35 Create business objective ----
    [Fact]
    public async Task UC35_01_CreateObjective_Returns201AndActive()
    {
        var code = NewCode();
        var result = await api.SendAsync("UC35-01", HttpMethod.Post, "/business-objectives", "admin", Body($" {code} "));
        Assert.Equal(201, result.Code);
        Assert.NotNull(result.Location);
        Assert.Equal(code, result.Data.GetProperty("code").GetString());
        Assert.Equal("ACTIVE", result.Data.GetProperty("status").GetString());
        Assert.Equal("2031-01-01", result.Data.GetProperty("startDate").GetString());
    }

    [Fact]
    public async Task UC35_02_CreateObjective_DuplicateCode_Returns409()
    {
        var code = NewCode();
        await CreateAsync(code: code);
        var result = await api.SendAsync("UC35-02", HttpMethod.Post, "/business-objectives", "admin", Body(code));
        Assert.Equal(409, result.Code);
        Assert.Equal("CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task UC35_03_CreateObjective_EndBeforeStart_Returns400()
    {
        var result = await api.SendAsync("UC35-03", HttpMethod.Post, "/business-objectives", "admin", Body(start: "2031-12-31", end: "2031-01-01"));
        Assert.Equal(400, result.Code);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task UC35_04_CreateObjective_MissingName_Returns400()
    {
        var result = await api.SendAsync("UC35-04", HttpMethod.Post, "/business-objectives", "admin", Body(name: ""));
        Assert.Equal(400, result.Code);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task UC35_05_CreateObjective_UnknownParent_Returns404()
    {
        var result = await api.SendAsync("UC35-05", HttpMethod.Post, "/business-objectives", "admin", Body(parent: Guid.NewGuid()));
        Assert.Equal(404, result.Code);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UC35_06_CreateObjective_WithoutManagePermission_Returns401Or403()
    {
        var anonymous = await api.SendAsync("UC35-06", HttpMethod.Post, "/business-objectives", "none", Body());
        var outsider = await api.SendAsync("UC35-06", HttpMethod.Post, "/business-objectives", "outsider", Body());
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
        Assert.Equal("FORBIDDEN", outsider.ErrorCode);
    }

    // ---- UC36 Update business objective ----
    [Fact]
    public async Task UC36_01_UpdateObjective_Returns200WithNewValues()
    {
        var id = await CreateAsync();
        var code = NewCode();
        var result = await api.SendAsync("UC36-01", HttpMethod.Put, $"/business-objectives/{id}", "admin", Body(code, "Tên mới", "2031-02-01", "2031-11-30"));
        Assert.Equal(200, result.Code);
        Assert.Equal(code, result.Data.GetProperty("code").GetString());
        Assert.Equal("Tên mới", result.Data.GetProperty("name").GetString());
        Assert.Equal("2031-02-01", result.Data.GetProperty("startDate").GetString());
    }

    [Fact]
    public async Task UC36_02_UpdateObjective_UnknownId_Returns404()
    {
        var result = await api.SendAsync("UC36-02", HttpMethod.Put, $"/business-objectives/{Guid.NewGuid()}", "admin", Body());
        Assert.Equal(404, result.Code);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UC36_03_UpdateObjective_CodeOfAnotherObjective_Returns409()
    {
        var other = NewCode();
        await CreateAsync(code: other);
        var id = await CreateAsync();
        var result = await api.SendAsync("UC36-03", HttpMethod.Put, $"/business-objectives/{id}", "admin", Body(other));
        Assert.Equal(409, result.Code);
        Assert.Equal("CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task UC36_04_UpdateObjective_ParentIsItselfOrDescendant_Returns400()
    {
        var root = await CreateAsync();
        var child = await CreateAsync(root);
        var self = await api.SendAsync("UC36-04", HttpMethod.Put, $"/business-objectives/{root}", "admin", Body(parent: root));
        var cycle = await api.SendAsync("UC36-04", HttpMethod.Put, $"/business-objectives/{root}", "admin", Body(parent: child));
        Assert.Equal(400, self.Code);
        Assert.Equal(400, cycle.Code);
        Assert.Equal("VALIDATION_ERROR", cycle.ErrorCode);
    }

    [Fact]
    public async Task UC36_05_UpdateObjective_ClosedObjective_Returns409()
    {
        var id = await CreateAsync();
        await SetStatusAsync(id, BusinessObjectiveStatus.Closed);
        var result = await api.SendAsync("UC36-05", HttpMethod.Put, $"/business-objectives/{id}", "admin", Body());
        Assert.Equal(409, result.Code);
    }

    [Fact]
    public async Task UC36_06_UpdateObjective_WithoutManagePermission_Returns401Or403()
    {
        var id = await CreateAsync();
        var anonymous = await api.SendAsync("UC36-06", HttpMethod.Put, $"/business-objectives/{id}", "none", Body());
        var outsider = await api.SendAsync("UC36-06", HttpMethod.Put, $"/business-objectives/{id}", "outsider", Body());
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
    }

    // ---- UC37 View business objectives ----
    [Fact]
    public async Task UC37_01_ListObjectives_OrderedByCodeAndWithoutCancelled()
    {
        var keep = await CreateAsync(code: $"LST-A{Guid.NewGuid():N}"[..12].ToUpperInvariant());
        var cancelled = await CreateAsync(code: $"LST-B{Guid.NewGuid():N}"[..12].ToUpperInvariant());
        await SetStatusAsync(cancelled, BusinessObjectiveStatus.Cancelled);
        var result = await api.SendAsync("UC37-01", HttpMethod.Get, "/business-objectives", "admin");
        Assert.Equal(200, result.Code);
        var items = result.Data.EnumerateArray().ToList();
        var codes = items.Select(i => i.GetProperty("code").GetString()!).ToList();
        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal), codes);
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == keep);
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == cancelled);
    }

    [Fact]
    public async Task UC37_02_ListObjectives_FilterByStatusAndKeyword()
    {
        var code = $"FLT-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var id = await CreateAsync(code: code);
        var cancelled = await CreateAsync();
        await SetStatusAsync(cancelled, BusinessObjectiveStatus.Cancelled);
        var byKeyword = await api.SendAsync("UC37-02", HttpMethod.Get, $"/business-objectives?keyword={code.ToLowerInvariant()}", "admin");
        var byStatus = await api.SendAsync("UC37-02", HttpMethod.Get, "/business-objectives?status=CANCELLED", "admin");
        Assert.Equal(id, Assert.Single(byKeyword.Data.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Contains(byStatus.Data.EnumerateArray(), i => i.GetProperty("id").GetGuid() == cancelled);
        Assert.All(byStatus.Data.EnumerateArray(), i => Assert.Equal("CANCELLED", i.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task UC37_03_ListObjectives_InvalidStatus_Returns400()
    {
        var word = await api.SendAsync("UC37-03", HttpMethod.Get, "/business-objectives?status=BOGUS", "admin");
        var ordinal = await api.SendAsync("UC37-03", HttpMethod.Get, "/business-objectives?status=1", "admin");
        Assert.Equal(400, word.Code);
        Assert.Equal("VALIDATION_ERROR", word.ErrorCode);
        Assert.Equal(400, ordinal.Code);
    }

    [Fact]
    public async Task UC37_04_ObjectiveDetail_ReturnsUsageCounts()
    {
        var root = await CreateAsync();
        await CreateAsync(root);
        var result = await api.SendAsync("UC37-04", HttpMethod.Get, $"/business-objectives/{root}", "admin");
        Assert.Equal(200, result.Code);
        Assert.Equal(root, result.Data.GetProperty("objective").GetProperty("id").GetGuid());
        Assert.Equal(1, result.Data.GetProperty("usage").GetProperty("childObjectiveCount").GetInt32());
        Assert.Equal(JsonValueKind.Array, result.Data.GetProperty("kpis").ValueKind);
    }

    [Fact]
    public async Task UC37_05_ObjectiveDetail_UnknownId_Returns404()
    {
        var result = await api.SendAsync("UC37-05", HttpMethod.Get, $"/business-objectives/{Guid.NewGuid()}", "admin");
        Assert.Equal(404, result.Code);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task UC37_06_ViewObjectives_Permissions()
    {
        var id = await CreateAsync();
        var anonymous = await api.SendAsync("UC37-06", HttpMethod.Get, "/business-objectives", "none");
        var outsider = await api.SendAsync("UC37-06", HttpMethod.Get, "/business-objectives", "outsider");
        var manager = await api.SendAsync("UC37-06", HttpMethod.Get, $"/business-objectives/{id}", "bm");
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(403, outsider.Code);
        Assert.Equal(200, manager.Code);
    }

    // ---- UC38 Delete business objective ----
    [Fact]
    public async Task UC38_01_DeleteObjective_Returns200ThenNotFound()
    {
        var id = await CreateAsync();
        var deleted = await api.SendAsync("UC38-01", HttpMethod.Delete, $"/business-objectives/{id}", "admin");
        var again = await api.SendAsync("UC38-01", HttpMethod.Get, $"/business-objectives/{id}", "admin");
        Assert.Equal(200, deleted.Code);
        Assert.Equal(404, again.Code);
    }

    [Fact]
    public async Task UC38_02_DeleteObjective_WithChild_Returns409InUse()
    {
        var root = await CreateAsync();
        await CreateAsync(root);
        var result = await api.SendAsync("UC38-02", HttpMethod.Delete, $"/business-objectives/{root}", "admin");
        Assert.Equal(409, result.Code);
        Assert.Equal("BUSINESS_OBJECTIVE_IN_USE", result.ErrorCode);
    }

    [Fact]
    public async Task UC38_03_DeleteObjective_UnknownId_Returns404()
    {
        var result = await api.SendAsync("UC38-03", HttpMethod.Delete, $"/business-objectives/{Guid.NewGuid()}", "admin");
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task UC38_04_DeleteObjective_WithoutManagePermission_Returns401Or403()
    {
        var id = await CreateAsync();
        var anonymous = await api.SendAsync("UC38-04", HttpMethod.Delete, $"/business-objectives/{id}", "none");
        var outsider = await api.SendAsync("UC38-04", HttpMethod.Delete, $"/business-objectives/{id}", "outsider");
        Assert.Equal(401, anonymous.Code);
        Assert.Equal(HttpStatusCode.Forbidden, outsider.Status);
    }
}
