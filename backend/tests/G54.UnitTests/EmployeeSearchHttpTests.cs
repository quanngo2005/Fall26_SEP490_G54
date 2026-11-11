namespace G54.UnitTests;

/// <summary>The employee keyword search uses PostgreSQL ILike, so it is checked against the real API and database.</summary>
[Collection(ObjectiveKpiTargetDefinition.Name)]
public sealed class EmployeeSearchHttpTests(ObjectiveKpiTargetFixture api)
{
    [Fact]
    public async Task Keyword_MatchesCodeAndNameWithoutRegardToCase()
    {
        var code = $"UEMP1-{api.Suffix}";
        var byCode = await api.SendAsync(null, HttpMethod.Get, $"/employees?keyword={code.ToLowerInvariant()}", "admin");
        var byName = await api.SendAsync(null, HttpMethod.Get, $"/employees?keyword={code.ToLowerInvariant()[..8]}", "admin");
        var none = await api.SendAsync(null, HttpMethod.Get, "/employees?keyword=zzz-khong-co", "admin");

        Assert.Equal(200, byCode.Code);
        Assert.Equal(code, Assert.Single(byCode.Data.GetProperty("items").EnumerateArray()).GetProperty("employeeCode").GetString());
        Assert.Contains(byName.Data.GetProperty("items").EnumerateArray(), i => i.GetProperty("employeeCode").GetString() == code);
        Assert.Empty(none.Data.GetProperty("items").EnumerateArray());
    }
}
