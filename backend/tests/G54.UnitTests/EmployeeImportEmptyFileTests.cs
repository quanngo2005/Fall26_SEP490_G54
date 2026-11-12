using G54.BLL.Common;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Services;
using G54.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace G54.UnitTests;

/// <summary>An employee import file that has a header row but no data rows is rejected, not "previewed" as 0 rows.</summary>
public sealed class EmployeeImportEmptyFileTests
{
    private static EmployeeImportService CreateService(AppDbContext db) =>
        new(null!, db, new ClosedXmlSheetReader(), new PlainPasswordEncoder(), Options.Create(new SystemParametersOptions()));

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task PreviewAsync_HeaderOnlyFile_ThrowsValidation()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        using var file = new MemoryStream(service.GetTemplate());

        await Assert.ThrowsAsync<ValidationException>(() => service.PreviewAsync("empty.xlsx", file, CancellationToken.None));
    }

    [Fact]
    public async Task CommitAsync_HeaderOnlyFile_ThrowsValidation_BeforeAnyWrite()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        using var file = new MemoryStream(service.GetTemplate());

        await Assert.ThrowsAsync<ValidationException>(() => service.CommitAsync(file, CancellationToken.None));
        Assert.Empty(db.Employees);
    }
}
