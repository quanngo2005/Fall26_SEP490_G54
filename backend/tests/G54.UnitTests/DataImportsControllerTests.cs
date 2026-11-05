using System.Security.Claims;
using System.Text;
using G54.Api.Controllers;
using G54.BLL.Common;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.DataImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class DataImportsControllerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private sealed class RecordingService : IEmployeeImportService
    {
        public byte[] GetTemplate() => [1, 2, 3];

        public Task<ImportPreviewDto> PreviewAsync(string fileName, Stream file, CancellationToken cancellationToken) =>
            Task.FromResult(new ImportPreviewDto(fileName, new ImportSummaryDto(0, 0, 0, 0, 0), []));

        public Task<ImportCommitResultDto> CommitAsync(Stream file, CancellationToken cancellationToken) =>
            Task.FromResult(new ImportCommitResultDto(1, 0, [new ImportNewAccountDto("NV001", "nv001@bank.local", "Temp#1")]));
    }

    private static DataImportsController ControllerFor(IEmployeeImportService service) => new(
        service, Options.Create(new SystemParametersOptions { ImportMaxFileMb = 5 }))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AccountId.ToString())], "test")),
            },
        },
    };

    private static FormFile MakeXlsxFile(string fileName = "import.xlsx")
    {
        // Minimal valid zip local-file-header signature, enough to pass the magic-byte check.
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 };
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName);
    }

    [Fact]
    public async Task Preview_ValidXlsx_Returns200()
    {
        var controller = ControllerFor(new RecordingService());

        var result = await controller.Preview(MakeXlsxFile(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Preview_NonXlsxExtension_Returns400()
    {
        var controller = ControllerFor(new RecordingService());
        var file = MakeXlsxFile("import.txt");

        var result = await controller.Preview(file, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.IsType<ApiResponse>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task Preview_WrongMagicBytes_Returns400()
    {
        var controller = ControllerFor(new RecordingService());
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a real xlsx"));
        var file = new FormFile(stream, 0, stream.Length, "file", "import.xlsx");

        var result = await controller.Preview(file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Preview_NullFile_Returns400()
    {
        var controller = ControllerFor(new RecordingService());

        var result = await controller.Preview(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Commit_SetsNoStoreCacheControlHeader()
    {
        var controller = ControllerFor(new RecordingService());

        var result = await controller.Commit(MakeXlsxFile(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("no-store", controller.ControllerContext.HttpContext.Response.Headers.CacheControl.ToString());
    }
}
