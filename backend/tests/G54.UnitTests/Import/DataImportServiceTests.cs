using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Session;
using G54.BLL.Dtos.DataImport;
using G54.BLL.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace G54.UnitTests.Import;

public sealed class DataImportServiceTests
{
    private sealed class FakeDefinition(bool failCommit = false) : IImportDefinition
    {
        public string Type => "fake";

        public byte[] BuildTemplateFile() => [1, 2, 3];

        public Task<ImportPipelineResult> PreviewAsync(ExcelSheetResult sheet, CancellationToken cancellationToken) =>
            Task.FromResult(new ImportPipelineResult(
                new ImportSummary(1, 1, 0, 1, 0, 0),
                [new ImportRowResult(2, "CREATE", new Dictionary<string, string?> { ["Code"] = "X" }, [])],
                []));

        public Task<ImportCommitResult> CommitAsync(ExcelSheetResult sheet, CancellationToken cancellationToken) =>
            failCommit
                ? throw new BusinessRuleException(G54.BLL.Common.Constants.ErrorCodes.DataImportHasErrors, null, 422)
                : Task.FromResult(new ImportCommitResult(1, 0, 0, [new ImportNewAccount("NV001", "nv001@bank.local", "Temp#123")]));
    }

    private sealed class FakeExcelSheetReader : IExcelSheetReader
    {
        public ExcelSheetResult Read(Stream excel, int maxRows) => new(["Code"], [new RawRow(2, ["X"])]);
    }

    private static DataImportService CreateService(IImportDefinition? definition, out IImportSessionStore sessionStore)
    {
        sessionStore = new DistributedCacheImportSessionStore(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        var options = Options.Create(new SystemParametersOptions { ImportMaxRows = 5000, ImportPreviewTtlMinutes = 30 });
        return new DataImportService(definition is null ? [] : [definition], sessionStore, new FakeExcelSheetReader(), options, TimeProvider.System);
    }

    [Fact]
    public async Task PreviewAsync_UnknownType_ThrowsNotFound()
    {
        var service = CreateService(null, out _);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.PreviewAsync("unknown", "file.xlsx", new MemoryStream(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task PreviewAsync_ThenGetPreview_SameUser_Succeeds()
    {
        var service = CreateService(new FakeDefinition(), out _);
        var userId = Guid.NewGuid();

        var preview = await service.PreviewAsync("fake", "file.xlsx", new MemoryStream(), userId, CancellationToken.None);
        var reloaded = await service.GetPreviewAsync(preview.ImportId, new ImportPreviewQueryDto(), userId, CancellationToken.None);

        Assert.Equal(preview.ImportId, reloaded.ImportId);
        Assert.Equal(1, reloaded.Summary.TotalRows);
    }

    [Fact]
    public async Task GetPreviewAsync_DifferentOwner_ThrowsNotFound()
    {
        var service = CreateService(new FakeDefinition(), out _);
        var preview = await service.PreviewAsync("fake", "file.xlsx", new MemoryStream(), Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetPreviewAsync(preview.ImportId, new ImportPreviewQueryDto(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetPreviewAsync_ExpiredSession_ThrowsNotFound()
    {
        var service = CreateService(new FakeDefinition(), out var sessionStore);
        var userId = Guid.NewGuid();
        var preview = await service.PreviewAsync("fake", "file.xlsx", new MemoryStream(), userId, CancellationToken.None);
        await sessionStore.RemoveAsync(preview.ImportId, CancellationToken.None); // simulates TTL expiry / Redis restart

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetPreviewAsync(preview.ImportId, new ImportPreviewQueryDto(), userId, CancellationToken.None));
    }

    [Fact]
    public async Task CommitAsync_DifferentOwner_ThrowsNotFound()
    {
        var service = CreateService(new FakeDefinition(), out _);
        var preview = await service.PreviewAsync("fake", "file.xlsx", new MemoryStream(), Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.CommitAsync(preview.ImportId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CommitAsync_DefinitionHasErrors_PropagatesAndSessionSurvives()
    {
        var service = CreateService(new FakeDefinition(failCommit: true), out var sessionStore);
        var userId = Guid.NewGuid();
        var preview = await service.PreviewAsync("fake", "file.xlsx", new MemoryStream(), userId, CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CommitAsync(preview.ImportId, userId, CancellationToken.None));

        Assert.NotNull(await sessionStore.GetAsync(preview.ImportId, CancellationToken.None));
    }

    [Fact]
    public async Task CommitAsync_Success_ReturnsNewAccountsAndRemovesSession()
    {
        var service = CreateService(new FakeDefinition(), out var sessionStore);
        var userId = Guid.NewGuid();
        var preview = await service.PreviewAsync("fake", "file.xlsx", new MemoryStream(), userId, CancellationToken.None);

        var result = await service.CommitAsync(preview.ImportId, userId, CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal("Temp#123", result.NewAccounts[0].TemporaryPassword);
        Assert.Null(await sessionStore.GetAsync(preview.ImportId, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_RemovesSession()
    {
        var service = CreateService(new FakeDefinition(), out var sessionStore);
        var userId = Guid.NewGuid();
        var preview = await service.PreviewAsync("fake", "file.xlsx", new MemoryStream(), userId, CancellationToken.None);

        await service.CancelAsync(preview.ImportId, userId, CancellationToken.None);

        Assert.Null(await sessionStore.GetAsync(preview.ImportId, CancellationToken.None));
    }
}
