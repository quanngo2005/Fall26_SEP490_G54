using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameter;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G54.UnitTests;

public sealed class SystemParameterServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly SystemParameterStore _store = new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["SystemParameters:ImportMaxFileMb"] = "5" }).Build());

    public void Dispose() => _db.Dispose();

    private SystemParameterService Service() => new(_db, _store);

    private static UpdateSystemParametersDto Update(params (string Key, decimal Value)[] items)
        => new(items.Select(i => new SystemParameterChangeDto(i.Key, i.Value)).ToList());

    [Fact]
    public async Task GB878_01_GetAll_ReturnsCatalogWithDefaultsWhenNothingSaved()
    {
        var result = await Service().GetAllAsync();

        Assert.Equal(SystemParameterCatalog.All.Select(d => d.Key), result.Select(i => i.Key));
        var maxFile = result.Single(i => i.Key == "importMaxFileMb");
        Assert.Equal(5, maxFile.Value);
        Assert.Equal(1, maxFile.Min);
        Assert.Equal(10, maxFile.Max);
        Assert.Null(maxFile.UpdatedAt);
        Assert.True(result.Single(i => i.Key == "importFormats").Locked);
        Assert.Equal(".xlsx", result.Single(i => i.Key == "importFormats").Value);
        Assert.Null(result.Single(i => i.Key == "handoverThresholdVnd").Max);
    }

    [Fact]
    public async Task GB878_02_Update_PersistsValueAndPublishesToLiveOptions()
    {
        var live = new LiveSystemParametersOptions(_store);

        var result = await Service().UpdateAsync(Update(("importMaxFileMb", 8), ("handoverThresholdVnd", 750_000_000m)));

        Assert.Equal(8, result.Single(i => i.Key == "importMaxFileMb").Value);
        Assert.NotNull(result.Single(i => i.Key == "importMaxFileMb").UpdatedAt);
        Assert.Equal(8, live.Value.ImportMaxFileMb);
        Assert.Equal(750_000_000m, live.Value.HandoverAmountThreshold);
        Assert.Equal(2, await _db.SystemParameters.CountAsync());
    }

    [Fact]
    public async Task GB878_03_Update_ReportsTheEditorUsername()
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
    public async Task GB878_04_Update_TwiceUpdatesTheSameRow()
    {
        await Service().UpdateAsync(Update(("appealWindowDays", 10)));
        await Service().UpdateAsync(Update(("appealWindowDays", 20)));

        Assert.Equal("20", (await _db.SystemParameters.SingleAsync()).Value);
        Assert.Equal(20, _store.Current.AppealWindowDays);
    }

    [Theory]
    [InlineData("importMaxFileMb", 11)]
    [InlineData("importMaxFileMb", 0)]
    [InlineData("importMaxFileMb", 2.5)]
    [InlineData("handoverThresholdVnd", 0)]
    [InlineData("handoverThresholdVnd", -5)]
    [InlineData("appealRejectedHours", 24)]
    [InlineData("kpiLockTime", 1)]
    [InlineData("unknown", 1)]
    public async Task GB878_05_Update_RejectsInvalidLockedOrUnknownWithoutSaving(string key, double value)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateAsync(Update((key, (decimal)value))));

        Assert.NotEmpty(ex.Errors!);
        Assert.Empty(await _db.SystemParameters.ToListAsync());
        Assert.Equal(5, _store.Current.ImportMaxFileMb);
    }

    [Fact]
    public async Task GB878_06_Update_IsAtomicWhenOneChangeIsInvalid()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().UpdateAsync(Update(("manualApprovalHours", 24), ("appealWindowDays", 99))));

        Assert.Empty(await _db.SystemParameters.ToListAsync());
    }

    [Fact]
    public async Task GB878_07_Update_RejectsEmptyAndDuplicateRequests()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateAsync(new UpdateSystemParametersDto([])));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service().UpdateAsync(Update(("preCloseCheckDays", 2), ("preCloseCheckDays", 3))));
    }

    [Fact]
    public async Task GB878_08_Refresh_IgnoresStoredValuesThatAreOutOfRangeOrLocked()
    {
        _db.SystemParameters.Add(new SystemParameter { Key = "importMaxFileMb", Value = "999" });
        _db.SystemParameters.Add(new SystemParameter { Key = "appealRejectedHours", Value = "1" });
        _db.SystemParameters.Add(new SystemParameter { Key = "preCloseCheckDays", Value = "2" });
        await _db.SaveChangesAsync();

        await Service().RefreshAsync();

        Assert.Equal(5, _store.Current.ImportMaxFileMb);
        Assert.Equal(48, _store.Current.AppealRejectedHours);
        Assert.Equal(2, _store.Current.PreCloseCheckDays);
    }

    [Fact]
    public void GB878_09_LiveOptions_ReflectTheLatestStoreSnapshot()
    {
        var live = new LiveSystemParametersOptions(_store);
        Assert.Equal(7, live.Value.AppealWindowDays);

        _store.Apply(new Dictionary<string, decimal> { ["appealWindowDays"] = 12 });

        Assert.Equal(12, live.Value.AppealWindowDays);
    }
}
