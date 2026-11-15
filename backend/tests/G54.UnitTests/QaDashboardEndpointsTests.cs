using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Quality;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class QaDashboardEndpointsTests
{
    private static QualityAssuranceController CreateController(IQaDashboardService service, Guid? accountId = null)
    {
        var claims = accountId.HasValue ? new[] { new Claim("sub", accountId.Value.ToString()) } : [];
        return new QualityAssuranceController(null!, null!, service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-test-qa",
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, accountId.HasValue ? "test" : null)),
                },
            },
        };
    }

    [Theory]
    [InlineData(nameof(QualityAssuranceController.GetDashboard), "qa-dashboard")]
    [InlineData(nameof(QualityAssuranceController.GetReconciliations), "qa-dashboard/reconciliations")]
    [InlineData(nameof(QualityAssuranceController.GetDashboardFilters), "qa-dashboard/filters")]
    public void Endpoint_UsesCanonicalRouteAndPermission(string method, string template)
    {
        var info = typeof(QualityAssuranceController).GetMethod(method);
        Assert.NotNull(info);

        var http = info.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(http);
        Assert.Equal(template, http.Template);

        var permission = info.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permission);
        Assert.Equal("quality_flag", permission.Resource);
        Assert.Equal("read", permission.Action);
        Assert.Equal(PermissionScopes.All, permission.Scope);

        Assert.NotEmpty(typeof(QualityAssuranceController).GetCustomAttributes<AuthorizeAttribute>());
    }

    [Fact]
    public async Task GetDashboard_ReturnsSuccessEnvelope()
    {
        var fakeDashboard = new QaDashboardDto(
            DateTimeOffset.UtcNow,
            new QaDashboardPeriodDto(Guid.NewGuid(), "PER-2026-10", "10/2026", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), DateTimeOffset.UtcNow, false),
            new QaDashboardBannerDto(12, "Msg", "Warn", true),
            new QaDashboardCardsDto(
                new QaDashboardNotMatchedCardDto(7, 4, "cũ nhất 4 ngày"),
                new QaDashboardInReviewCardDto(12, "chờ GD import"),
                new QaDashboardQaQueueCardDto(9, 5, 4, "5 mẫu · 4 cờ"),
                new QaDashboardRiskEventsCardDto(3, 8.0m, "tổng phạt -8 điểm")),
            new QaDashboardMatchedRateChartDto("Title", "Subtitle", 95.6m, "T43", []),
            [],
            []);

        var fakeService = new FakeQaDashboardService { DashboardToReturn = fakeDashboard };
        var controller = CreateController(fakeService, Guid.NewGuid());

        var response = await controller.GetDashboard(new QaDashboardFilterDto(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var envelope = Assert.IsType<ApiResponse<QaDashboardDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(fakeDashboard, envelope.Data);
        Assert.Equal("trace-test-qa", envelope.TraceId);
    }

    [Fact]
    public async Task GetReconciliations_ReturnsPagedEnvelope()
    {
        var fakePaged = new PagedResult<QaReconciliationItemDto>([], 0, 1, 20);
        var fakeService = new FakeQaDashboardService { ReconciliationsToReturn = fakePaged };
        var controller = CreateController(fakeService, Guid.NewGuid());

        var response = await controller.GetReconciliations(new QaReconciliationFilterDto(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<QaReconciliationItemDto>>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(fakePaged, envelope.Data);
    }

    [Fact]
    public async Task GetFilters_ReturnsOptionsEnvelope()
    {
        var fakeOptions = new QaDashboardFilterOptionsDto([], [], []);
        var fakeService = new FakeQaDashboardService { FilterOptionsToReturn = fakeOptions };
        var controller = CreateController(fakeService, Guid.NewGuid());

        var response = await controller.GetDashboardFilters(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var envelope = Assert.IsType<ApiResponse<QaDashboardFilterOptionsDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(fakeOptions, envelope.Data);
    }

    private sealed class FakeQaDashboardService : IQaDashboardService
    {
        public QaDashboardDto? DashboardToReturn { get; set; }
        public PagedResult<QaReconciliationItemDto>? ReconciliationsToReturn { get; set; }
        public QaDashboardFilterOptionsDto? FilterOptionsToReturn { get; set; }

        public Task<QaDashboardDto> GetDashboardAsync(QaDashboardFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardToReturn!);

        public Task<PagedResult<QaReconciliationItemDto>> GetReconciliationsAsync(QaReconciliationFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReconciliationsToReturn!);

        public Task<QaDashboardFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(FilterOptionsToReturn!);
    }
}
