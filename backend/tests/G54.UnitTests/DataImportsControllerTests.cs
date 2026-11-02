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

    private sealed class RecordingService : IDataImportService
    {
        public Guid? PreviewUserId { get; private set; }
        public Guid? CommitUserId { get; private set; }
        public Guid? CancelUserId { get; private set; }

        public byte[] GetTemplate(string type) => [1, 2, 3];

        public Task<ImportPreviewDto> PreviewAsync(string type, string fileName, Stream file, Guid userId, CancellationToken cancellationToken)
        {
            PreviewUserId = userId;
            return Task.FromResult(new ImportPreviewDto(
                Guid.NewGuid(), type, fileName, DateTimeOffset.UtcNow.AddMinutes(30),
                new ImportSummaryDto(0, 0, 0, 0, 0, 0), [],
                new PagedResult<ImportPreviewRowDto>([], 0, 1, 20)));
        }

        public Task<ImportPreviewDto> GetPreviewAsync(Guid importId, ImportPreviewQueryDto query, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(new ImportPreviewDto(
                importId, "employee-organization", "file.xlsx", DateTimeOffset.UtcNow.AddMinutes(30),
                new ImportSummaryDto(0, 0, 0, 0, 0, 0), [],
                new PagedResult<ImportPreviewRowDto>([], 0, 1, 20)));

        public Task<ImportCommitResultDto> CommitAsync(Guid importId, Guid userId, CancellationToken cancellationToken)
        {
            CommitUserId = userId;
            return Task.FromResult(new ImportCommitResultDto(1, 0, 0, [new ImportNewAccountDto("NV001", "nv001@bank.local", "Temp#1")]));
        }

        public Task CancelAsync(Guid importId, Guid userId, CancellationToken cancellationToken)
        {
            CancelUserId = userId;
            return Task.CompletedTask;
        }
    }

    private static DataImportsController ControllerFor(IDataImportService service) => new(
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
    public async Task Preview_ValidXlsx_Returns201WithUserId()
    {
        var service = new RecordingService();
        var controller = ControllerFor(service);

        var result = await controller.Preview("employee-organization", MakeXlsxFile(), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(AccountId, service.PreviewUserId);
    }

    [Fact]
    public async Task Preview_NonXlsxExtension_Returns400()
    {
        var controller = ControllerFor(new RecordingService());
        var file = MakeXlsxFile("import.txt");

        var result = await controller.Preview("employee-organization", file, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ApiResponse>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task Preview_WrongMagicBytes_Returns400()
    {
        var controller = ControllerFor(new RecordingService());
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a real xlsx"));
        var file = new FormFile(stream, 0, stream.Length, "file", "import.xlsx");

        var result = await controller.Preview("employee-organization", file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Preview_NullFile_Returns400()
    {
        var controller = ControllerFor(new RecordingService());

        var result = await controller.Preview("employee-organization", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Commit_SetsNoStoreCacheControlHeader()
    {
        var controller = ControllerFor(new RecordingService());

        var result = await controller.Commit(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("no-store", controller.ControllerContext.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Cancel_ReturnsNoContent()
    {
        var controller = ControllerFor(new RecordingService());

        var result = await controller.Cancel(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
