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

        public Task<IReadOnlyList<CampaignObjectiveDto>> GetObjectivesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
    }
}
