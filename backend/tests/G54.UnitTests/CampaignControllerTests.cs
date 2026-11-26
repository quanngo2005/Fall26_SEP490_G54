using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Campaign;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace G54.UnitTests;

public sealed class CampaignControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Decide_ReturnsOkWithCanonicalApiResponse_WhenDecisionSucceeds()
    {
        var branchManagerId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var mockService = new MockCampaignService();

        var controller = new CampaignController(mockService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", branchManagerId.ToString())], "test")),
                },
            },
        };

        var request = new CampaignDecisionRequestDto("APPROVE", "Duyệt kế hoạch", 150000000m);
        var result = await controller.Decide(campaignId, request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(campaignId, mockService.LastDecidedCampaignId);
        Assert.Equal(branchManagerId, mockService.LastDecidedUserId);
        Assert.Equal("APPROVE", mockService.LastDecisionRequest?.Decision);

        var json = JsonSerializer.Serialize(okResult.Value, JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(200, document.RootElement.GetProperty("status").GetInt32());

        var data = document.RootElement.GetProperty("data");
        Assert.Equal(campaignId.ToString(), data.GetProperty("campaignId").GetString());
        Assert.Equal("RUNNING", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Decide_ThrowsUnauthorizedException_WhenUserIsNotAuthenticated()
    {
        var mockService = new MockCampaignService();
        var controller = new CampaignController(mockService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var request = new CampaignDecisionRequestDto("APPROVE");
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.Decide(Guid.NewGuid(), request, CancellationToken.None));
    }

    private static CampaignController CreateController(Guid? userId = null) => new(new MockCampaignService())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = userId is null
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString()!)], "test")),
            },
        },
    };

    [Fact]
    public async Task GetCampaigns_ReturnsOkWithTheRequestedPage()
    {
        var result = await CreateController(Guid.NewGuid()).GetCampaigns("q", "RUNNING", null, 3, 10);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<CampaignListOverviewDto>>(ok.Value);
        Assert.Equal(3, response.Data!.Campaigns.Page);
    }

    [Fact]
    public async Task GetById_ReturnsTheCampaign()
    {
        var id = Guid.NewGuid();

        var result = await CreateController(Guid.NewGuid()).GetById(id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, Assert.IsType<ApiResponse<CampaignDetailDto>>(ok.Value).Data!.Id);
    }

    [Fact]
    public async Task Create_ReturnsCreatedPointingAtGetById()
    {
        var request = new CreateCampaignRequestDto(
            Guid.NewGuid(), "CMP-1", "Name", null, DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));

        var result = await CreateController(Guid.NewGuid()).Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(CampaignController.GetById), created.ActionName);
    }

    [Fact]
    public async Task Update_ReturnsOk()
    {
        var request = new UpdateCampaignRequestDto(
            "Name", null, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)));

        var result = await CreateController(Guid.NewGuid()).Update(Guid.NewGuid(), request);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var result = await CreateController(Guid.NewGuid()).Delete(Guid.NewGuid());

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task BatchGenerateWork_ReturnsOk()
    {
        var result = await CreateController(Guid.NewGuid()).BatchGenerateWork(
            Guid.NewGuid(), new BatchWorkGeneratorRequestDto(Guid.NewGuid(), "POOL", "EVEN", 1));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Complete_ReturnsCompleted()
    {
        var id = Guid.NewGuid();

        var result = await CreateController(Guid.NewGuid()).Complete(id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("COMPLETED", Assert.IsType<ApiResponse<CampaignDecisionResultDto>>(ok.Value).Data!.Status);
    }

    [Fact]
    public async Task Cancel_ReturnsCancelledWithTheReason()
    {
        var result = await CreateController(Guid.NewGuid()).Cancel(Guid.NewGuid(), new CancelCampaignRequestDto("Hết ngân sách"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<CampaignDecisionResultDto>>(ok.Value);
        Assert.Equal("CANCELLED", response.Data!.Status);
        Assert.Contains("Hết ngân sách", response.Message);
    }

    [Fact]
    public async Task WriteActions_ThrowUnauthorized_WhenUserIsNotAuthenticated()
    {
        var controller = CreateController();

        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.Complete(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.Delete(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.Cancel(Guid.NewGuid(), new CancelCampaignRequestDto("x")));
    }

    [Fact]
    public void DecideRequiresAllScopeAndTheOtherWritesRequireDeptScope()
    {
        static string Scope(string method) =>
            typeof(CampaignController).GetMethod(method)!.GetCustomAttribute<G54.Api.Authorization.HasPermissionAttribute>()!.Scope;

        Assert.Equal("All", Scope(nameof(CampaignController.Decide)));
        Assert.Equal("Dept", Scope(nameof(CampaignController.Create)));
        Assert.Equal("Dept", Scope(nameof(CampaignController.Complete)));
        Assert.Equal("Dept", Scope(nameof(CampaignController.Cancel)));
    }

    [Fact]
    public void ControllerHasExpectedRouteAndAuthorizeAttributes()
    {
        var routeAttr = typeof(CampaignController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttr);
        Assert.Equal("campaigns", routeAttr.Template);

        var authorizeAttr = typeof(CampaignController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);

        var apiControllerAttr = typeof(CampaignController).GetCustomAttribute<ApiControllerAttribute>();
        Assert.NotNull(apiControllerAttr);
    }

    private sealed class MockCampaignService : ICampaignService
    {
        public Guid LastDecidedCampaignId { get; private set; }
        public Guid LastDecidedUserId { get; private set; }
        public CampaignDecisionRequestDto? LastDecisionRequest { get; private set; }

        public Task<CampaignDecisionResultDto> DecideCampaignAsync(
            Guid id,
            CampaignDecisionRequestDto request,
            Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            LastDecidedCampaignId = id;
            LastDecidedUserId = currentUserId;
            LastDecisionRequest = request;

            return Task.FromResult(new CampaignDecisionResultDto(
                id,
                "RUNNING",
                DateTimeOffset.UtcNow,
                "Đã phê duyệt kế hoạch chiến dịch kinh doanh thành công."));
        }

        public Task<CampaignListOverviewDto> GetCampaignsAsync(
            string? search,
            string? status,
            Guid? objectiveId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var overview = new CampaignOverviewMetricsDto(1, 1, 0, 0, 10, 5);
            var paged = new PagedResult<CampaignSummaryDto>([], 0, page, pageSize);
            return Task.FromResult(new CampaignListOverviewDto(overview, paged));
        }

        public Task<CampaignDetailDto> GetCampaignByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var obj = new CampaignObjectiveDto(Guid.NewGuid(), "OBJ-1", "Objective 1", null);
            var metrics = new CampaignMetricsDto(0, 0, 0, 0, 0);
            return Task.FromResult(new CampaignDetailDto(
                id, "CMP-1", "Campaign 1", null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                "PLANNED", obj, [], [], metrics, true, true, DateTimeOffset.UtcNow));
        }

        public Task<CampaignDetailDto> CreateCampaignAsync(CreateCampaignRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
            => GetCampaignByIdAsync(Guid.NewGuid(), cancellationToken);

        public Task<CampaignDetailDto> UpdateCampaignAsync(Guid id, UpdateCampaignRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
            => GetCampaignByIdAsync(id, cancellationToken);

        public Task DeleteCampaignAsync(Guid id, Guid currentUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<BatchWorkGenerateResultDto> BatchGenerateWorkAsync(Guid id, BatchWorkGeneratorRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new BatchWorkGenerateResultDto(id, "CMP-1", 1, 1, 1m, ["TSK-1"], "pool", DateTimeOffset.UtcNow, "Done"));

        public Task<CampaignDecisionResultDto> CompleteAsync(Guid id, Guid currentUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CampaignDecisionResultDto(id, "COMPLETED", DateTimeOffset.UtcNow, "Đã hoàn tất chiến dịch thành công."));

        public Task<CampaignDecisionResultDto> CancelAsync(Guid id, CancelCampaignRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CampaignDecisionResultDto(id, "CANCELLED", DateTimeOffset.UtcNow, $"Đã hủy chiến dịch. Lý do: {request.Reason}"));

        public Task<int> SendEndReminderAsync(DateOnly today, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
