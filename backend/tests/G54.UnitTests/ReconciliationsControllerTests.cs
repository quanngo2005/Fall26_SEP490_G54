using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Reconciliation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class ReconciliationsControllerTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static ReconciliationsController ControllerFor(RecordingService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", CallerId.ToString())], "TestAuth")),
            },
        },
    };

    [Theory]
    [InlineData(nameof(ReconciliationsController.List), "")]
    [InlineData(nameof(ReconciliationsController.GetDetail), "{id:guid}")]
    [InlineData(nameof(ReconciliationsController.Export), "export")]
    public void EveryAction_IsAGetRoute_ThatNeedsReconciliationRead(string action, string route)
    {
        var type = typeof(ReconciliationsController);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("reconciliations", type.GetCustomAttribute<RouteAttribute>()?.Template);

        var method = type.GetMethod(action)!;
        Assert.Equal(route, method.GetCustomAttribute<HttpGetAttribute>()!.Template ?? string.Empty);
        var required = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(required);
        Assert.Equal("reconciliation", required.Resource);
        Assert.Equal("read", required.Action);
        Assert.Equal("Dept", required.Scope);
    }

    [Fact]
    public async Task List_PassesTheCallerAndTheFilterAndWrapsTheResult()
    {
        var service = new RecordingService();
        var filter = new ReconciliationFilterDto(Status: "NOT_MATCHED", Page: 2);

        var result = await ControllerFor(service).List(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<ReconciliationListDto>>(ok.Value);
        Assert.True(body.Success);
        Assert.Equal("trace-1", body.TraceId);
        Assert.Equal(CallerId, service.UserId);
        Assert.Same(filter, service.Filter);
    }

    [Fact]
    public async Task Export_ReturnsTheWorkbookAsAnExcelFile()
    {
        var service = new RecordingService();

        var result = await ControllerFor(service).Export(new ReconciliationFilterDto(), CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Equal("doi-soat.xlsx", file.FileDownloadName);
        Assert.Equal([1, 2, 3], file.FileContents);
    }

    private sealed class RecordingService : IReconciliationService
    {
        public Guid UserId { get; private set; }

        public ReconciliationFilterDto? Filter { get; private set; }

        public Task<ReconciliationListDto> ListAsync(Guid userId, ReconciliationFilterDto filter, CancellationToken cancellationToken = default)
        {
            UserId = userId;
            Filter = filter;
            return Task.FromResult(new ReconciliationListDto(
                [],
                0,
                1,
                20,
                new ReconciliationCountsDto(0, 0, 0, 0),
                new ReconciliationFilterOptionsDto([], [], [], [])));
        }

        public Task<ReconciliationDetailDto> GetDetailAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReconciliationExportDto> ExportAsync(Guid userId, ReconciliationFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReconciliationExportDto([1, 2, 3], "doi-soat.xlsx"));
    }
}
