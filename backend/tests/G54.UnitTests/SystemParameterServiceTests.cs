using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameter;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class SystemParameterServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly SystemParameterStore _store = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public void Dispose() => _db.Dispose();

    private SystemParameterService Service() => new(_db, _store, _cache);

    private static UpdateSystemParametersDto Update(params (string Key, object? Value)[] items)
        => new(items.Select(i => new SystemParameterChangeDto(i.Key, i.Value)).ToList());

    [Fact]
    public async Task GB910_01_GetAll_ReturnsFullTypedCatalogWithCanonicalDefaults()
    {
        var result = await Service().GetAllAsync();

        Assert.Equal(SystemParameterCatalog.All.Select(d => d.Key), result.Select(i => i.Key));
        var maxFile = result.Single(i => i.Key == "importMaxFileMb");
        Assert.Equal(5, maxFile.Value);
        Assert.Equal(1m, maxFile.Min);
        Assert.Equal(50m, maxFile.Max);
        Assert.Equal(SystemParameterValueTypes.Integer, maxFile.ValueType);
        Assert.Equal(SystemParameterGroups.ImportHandover, maxFile.Group);
        Assert.Null(maxFile.UpdatedAt);

        Assert.True(result.Single(i => i.Key == "importFormats").Locked);
        Assert.Equal(".xlsx", result.Single(i => i.Key == "importFormats").Value);

        // kpiLockTime and appealRejectedHours are now editable by Admin (GB-910)
        Assert.False(result.Single(i => i.Key == "kpiLockTime").Locked);
        Assert.Equal("23:59:59", result.Single(i => i.Key == "kpiLockTime").Value);
        Assert.False(result.Single(i => i.Key == "appealRejectedHours").Locked);
        Assert.Null(result.Single(i => i.Key == "handoverThresholdVnd").Max);
    }

    [Fact]
    public async Task GB910_02_Update_SupportsIntegerDecimalPercentTimeAndBooleanAndSyncsDistributedCache()
    {
        var live = new LiveSystemParametersOptions(_store);

        var result = await Service().UpdateAsync(Update(
            ("importMaxFileMb", 12),
            ("handoverThresholdVnd", 750_000_000m),
            ("perfAttentionDropPoints", 2.75m),
            ("tierAQuotaPercent", 25m),
            ("kpiLockTime", "22:30:00"),
            ("workOverdueEnabled", false)));

        Assert.Equal(12, result.Single(i => i.Key == "importMaxFileMb").Value);
        Assert.Equal(2.75m, result.Single(i => i.Key == "perfAttentionDropPoints").Value);
        Assert.Equal(25m, result.Single(i => i.Key == "tierAQuotaPercent").Value);
        Assert.Equal("22:30:00", result.Single(i => i.Key == "kpiLockTime").Value);
        Assert.Equal(false, result.Single(i => i.Key == "workOverdueEnabled").Value);

        Assert.Equal(12, live.Value.ImportMaxFileMb);
        Assert.Equal(750_000_000m, live.Value.HandoverAmountThreshold);
        Assert.Equal(2.75m, live.Value.PerfAttentionDropPoints);
        Assert.Equal(25m, live.Value.TierAQuotaPercent);
        Assert.Equal(new TimeOnly(22, 30, 0), live.Value.ResolveKpiLockTimeOnly());
        Assert.False(live.Value.WorkOverdueEnabled);

        // Verify a second SystemParameterStore instance refreshes immediately from Redis/IDistributedCache
        var secondNodeStore = new SystemParameterStore();
        var secondNodeService = new SystemParameterService(_db, secondNodeStore, _cache);
        await secondNodeService.RefreshAsync();
        Assert.Equal(25m, secondNodeStore.Current.TierAQuotaPercent);
        Assert.Equal("22:30:00", secondNodeStore.Current.KpiLockTime);
    }

    [Fact]
    public async Task GB910_03_Update_ReportsTheEditorUsername()
    {
        var editor = new Account { Id = Guid.NewGuid(), Username = "sa.admin", PasswordHash = "x" };
        _db.Accounts.Add(editor);
        _db.SystemParameters.Add(new SystemParameter { Key = "appealWindowDays", Value = "10", CreatedBy = editor.Id });
        await _db.SaveChangesAsync();

        var result = await Service().GetAllAsync();

        Assert.Equal("sa.admin", result.Single(i => i.Key == "appealWindowDays").UpdatedBy);
        Assert.Equal(10, _store.Current.AppealWindowDays);
    }

    [Fact]
    public async Task GB910_04_Update_TwiceUpdatesTheSameRow()
    {
        await Service().UpdateAsync(Update(("appealWindowDays", 10)));
        await Service().UpdateAsync(Update(("appealWindowDays", 20)));

        Assert.Equal("20", (await _db.SystemParameters.SingleAsync()).Value);
        Assert.Equal(20, _store.Current.AppealWindowDays);
    }

    [Theory]
    [InlineData("importMaxFileMb", 51)]
    [InlineData("importMaxFileMb", 0)]
    [InlineData("importMaxFileMb", 2.5)]
    [InlineData("handoverThresholdVnd", 0)]
    [InlineData("handoverThresholdVnd", -5)]
    [InlineData("importFormats", ".csv")]
    [InlineData("kpiLockTime", "25:99:00")]
    [InlineData("workOverdueEnabled", "not-a-bool")]
    [InlineData("unknown", 1)]
    public async Task GB910_05_Update_RejectsInvalidLockedOrUnknownWithoutSaving(string key, object value)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateAsync(Update((key, value))));

        Assert.NotEmpty(ex.Errors!);
        Assert.Empty(await _db.SystemParameters.ToListAsync());
        Assert.Equal(5, _store.Current.ImportMaxFileMb);
    }

    [Fact]
    public async Task GB910_06_Update_IsAtomicWhenOneChangeIsInvalid()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().UpdateAsync(Update(("manualApprovalHours", 24), ("appealWindowDays", 99))));

        Assert.Empty(await _db.SystemParameters.ToListAsync());
    }

    [Fact]
    public async Task GB910_07_Update_RejectsEmptyAndDuplicateRequests()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateAsync(new UpdateSystemParametersDto([])));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().UpdateAsync(Update(("preCloseCheckDays", 2), ("preCloseCheckDays", 3))));
    }

    [Fact]
    public async Task GB910_08_Refresh_IgnoresStoredValuesThatAreOutOfRangeOrLocked()
    {
        _db.SystemParameters.Add(new SystemParameter { Key = "importMaxFileMb", Value = "999" });
        _db.SystemParameters.Add(new SystemParameter { Key = "importFormats", Value = ".csv" });
        _db.SystemParameters.Add(new SystemParameter { Key = "preCloseCheckDays", Value = "2" });
        await _db.SaveChangesAsync();

        var freshService = new SystemParameterService(_db, _store);
        await freshService.RefreshAsync();

        Assert.Equal(5, _store.Current.ImportMaxFileMb);
        Assert.Equal(2, _store.Current.PreCloseCheckDays);
    }

    [Fact]
    public void GB910_09_LiveOptions_ReflectTheLatestStoreSnapshot()
    {
        var live = new LiveSystemParametersOptions(_store);
        Assert.Equal(7, live.Value.AppealWindowDays);

        _store.Apply(new Dictionary<string, decimal> { ["appealWindowDays"] = 12 });

        Assert.Equal(12, live.Value.AppealWindowDays);
    }
}
