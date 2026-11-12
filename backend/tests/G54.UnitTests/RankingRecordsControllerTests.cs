using System.Reflection;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace G54.UnitTests;

public sealed class RankingRecordsControllerTests
{
    [Fact]
    public async Task GetRecords_ForwardsTheFilterAndWrapsThePageInTheEnvelope()
    {
        var service = new RecordingService();
        var filter = new RankingRecordFilterDto { PeriodId = Guid.NewGuid(), Tier = "A1", Page = 2 };

        var result = await ControllerFor(service).GetRecords(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(filter, service.LastFilter);
        var data = Assert.IsType<ApiResponse<PagedResult<RankingRecordDto>>>(ok.Value).Data;
        Assert.Equal(2, data!.Page);
        Assert.Empty(data.Items);
    }

    [Fact]
    public async Task GetRecord_ForwardsTheEvaluationId()
    {
        var service = new RecordingService();
        var id = Guid.NewGuid();

        var result = await ControllerFor(service).GetRecord(id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.LastId);
    }

    [Fact]
    public void Controller_UsesTheRankingRecordsRouteAndRequiresRankingRecordReadAtAll()
    {
        var type = typeof(RankingRecordsController);

        Assert.Equal("ranking-records", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        foreach (var name in new[] { nameof(RankingRecordsController.GetRecords), nameof(RankingRecordsController.GetRecord) })
        {
            var permission = type.GetMethod(name)!.GetCustomAttribute<HasPermissionAttribute>();
            Assert.Equal(("ranking_record", "read", "All"), (permission!.Resource, permission.Action, permission.Scope));
        }
    }

    private static RankingRecordsController ControllerFor(IRankingRecordService service) => new(service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private sealed class RecordingService : IRankingRecordService
    {
        public RankingRecordFilterDto? LastFilter { get; private set; }

        public Guid LastId { get; private set; }

        public Task<PagedResult<RankingRecordDto>> GetRecordsAsync(RankingRecordFilterDto filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult(new PagedResult<RankingRecordDto>([], 0, filter.Page, filter.PageSize));
        }

        public Task<RankingRecordDetailDto> GetRecordAsync(Guid evaluationId, CancellationToken cancellationToken = default)
        {
            LastId = evaluationId;
            return Task.FromResult<RankingRecordDetailDto>(null!);
        }
    }
}
