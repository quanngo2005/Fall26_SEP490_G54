using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Quality;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class QualityAssuranceControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static QualityAssuranceController CreateController(
        IQualityAssuranceService? service = null,
        IQaQueueService? queue = null,
        IQaDashboardService? dashboard = null)
    {
        return new QualityAssuranceController(
            service ?? new StubQualityAssuranceService(),
            queue ?? new StubQaQueueService(),
            dashboard ?? new StubQaDashboardService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-qa-1",
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };
    }

    private static QualityFlagDto SampleFlag(Guid? id = null) => new(
        id ?? Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "W-01", "Work Title",
        Guid.NewGuid(), "EMP01", "Nguyen Van A", UserId, "QA User", null,
        "MEDIUM", "Reason for flag", "OPEN", null, null, DateTimeOffset.UtcNow);

    private static QaSampleDto SampleSample(Guid? id = null) => new(
        id ?? Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "W-01", "Work Title",
        Guid.NewGuid(), "EMP01", "Nguyen Van A", "PENDING", DateTimeOffset.UtcNow,
        null, null, null, null, null);

    private static QaQueueItemDto SampleQueueItem(Guid? id = null, string kind = "FLAG") => new(
        kind, "SAMPLE", id ?? Guid.NewGuid(), "QA-01", "MANUAL", Guid.NewGuid(), 1,
        Guid.NewGuid(), "W-01", "Work Title", Guid.NewGuid(), "EMP01", "Nguyen Van A",
        "IT Dept", "PENDING", "OPEN", "MEDIUM", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));

    [Fact]
    public async Task GetQueue_ReturnsOkWithQaQueueDto()
    {
        var queueDto = new QaQueueDto([], 0, 1, 20, new QaQueueCountsDto(0, 0, 0), []);
        var queueService = new StubQaQueueService { QueueResult = queueDto };
        var controller = CreateController(queue: queueService);

        var result = await controller.GetQueue(new QaQueueFilterDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QaQueueDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(queueDto, envelope.Data);
    }

    [Fact]
    public async Task GetQueueItem_ReturnsOkWithQaQueueDetailDto()
    {
        var itemId = Guid.NewGuid();
        var detailDto = new QaQueueDetailDto(
            SampleQueueItem(itemId), "Reason", null, null, null, null,
            DateTimeOffset.UtcNow, "Developer", [], []);
        var queueService = new StubQaQueueService { DetailResult = detailDto };
        var controller = CreateController(queue: queueService);

        var result = await controller.GetQueueItem("FLAG", itemId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QaQueueDetailDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(detailDto, envelope.Data);
    }

    [Fact]
    public async Task CreateFlag_Returns201CreatedAtActionWithQualityFlagDto()
    {
        var flagId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var flag = SampleFlag(flagId);
        var qaService = new StubQualityAssuranceService { FlagResult = flag };
        var controller = CreateController(service: qaService);

        var dto = new CreateQualityFlagDto("Sai số liệu", "HIGH");
        var result = await controller.CreateFlag(submissionId, dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(QualityAssuranceController.GetFlag), created.ActionName);
        Assert.Equal(flagId, created.RouteValues?["id"]);
        var envelope = Assert.IsType<ApiResponse<QualityFlagDto>>(created.Value);
        Assert.True(envelope.Success);
        Assert.Same(flag, envelope.Data);
    }

    [Fact]
    public async Task GetFlags_ReturnsOkWithPagedFlags()
    {
        var flag = SampleFlag();
        var paged = new PagedResult<QualityFlagDto>([flag], 1, 1, 20);
        var qaService = new StubQualityAssuranceService { PagedFlagsResult = paged };
        var controller = CreateController(service: qaService);

        var result = await controller.GetFlags(new QualityFlagFilterDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<QualityFlagDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(paged, envelope.Data);
    }

    [Fact]
    public async Task GetFlag_ReturnsOkWithFlag()
    {
        var flagId = Guid.NewGuid();
        var flag = SampleFlag(flagId);
        var qaService = new StubQualityAssuranceService { FlagResult = flag };
        var controller = CreateController(service: qaService);

        var result = await controller.GetFlag(flagId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QualityFlagDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal(flagId, envelope.Data!.Id);
    }

    [Fact]
    public async Task RequestRework_ReturnsOkWithQualityFlagDto()
    {
        var flagId = Guid.NewGuid();
        var flag = SampleFlag(flagId);
        var qaService = new StubQualityAssuranceService { FlagResult = flag };
        var controller = CreateController(service: qaService);

        var dto = new RequestReworkDto("Cần bổ sung chứng từ");
        var result = await controller.RequestRework(flagId, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QualityFlagDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(flag, envelope.Data);
    }

    [Fact]
    public async Task DismissFlag_ReturnsOkWithQualityFlagDto()
    {
        var flagId = Guid.NewGuid();
        var flag = SampleFlag(flagId);
        var qaService = new StubQualityAssuranceService { FlagResult = flag };
        var controller = CreateController(service: qaService);

        var result = await controller.DismissFlag(flagId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QualityFlagDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(flag, envelope.Data);
    }

    [Fact]
    public async Task GetSamples_ReturnsOkWithPagedSamples()
    {
        var sample = SampleSample();
        var paged = new PagedResult<QaSampleDto>([sample], 1, 1, 20);
        var qaService = new StubQualityAssuranceService { PagedSamplesResult = paged };
        var controller = CreateController(service: qaService);

        var result = await controller.GetSamples(new QaSampleFilterDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<QaSampleDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(paged, envelope.Data);
    }

    [Fact]
    public async Task DecideSample_ReturnsOkWithQaSampleDecisionDto()
    {
        var sampleId = Guid.NewGuid();
        var decision = new QaSampleDecisionDto(SampleSample(sampleId), null);
        var qaService = new StubQualityAssuranceService { DecisionResult = decision };
        var controller = CreateController(service: qaService);

        var dto = new DecideQaSampleDto("PASSED", "Đạt yêu cầu", null, false);
        var result = await controller.DecideSample(sampleId, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QaSampleDecisionDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(decision, envelope.Data);
    }

    private sealed class StubQualityAssuranceService : IQualityAssuranceService
    {
        public QualityFlagDto FlagResult { get; set; } = null!;
        public PagedResult<QualityFlagDto> PagedFlagsResult { get; set; } = null!;
        public PagedResult<QaSampleDto> PagedSamplesResult { get; set; } = null!;
        public QaSampleDecisionDto DecisionResult { get; set; } = null!;

        public Task<QualityFlagDto> CreateFlagAsync(
            Guid submissionId, Guid userId, CreateQualityFlagDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(FlagResult);

        public Task<PagedResult<QualityFlagDto>> GetFlagsAsync(
            QualityFlagFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(PagedFlagsResult);

        public Task<QualityFlagDto> GetFlagAsync(Guid flagId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FlagResult);

        public Task<QualityFlagDto> RequestReworkAsync(
            Guid flagId, Guid userId, RequestReworkDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(FlagResult);

        public Task<QualityFlagDto> DismissFlagAsync(Guid flagId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FlagResult);

        public Task<PagedResult<QaSampleDto>> GetSamplesAsync(
            QaSampleFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(PagedSamplesResult);

        public Task<QaSampleDecisionDto> DecideSampleAsync(
            Guid sampleId, Guid userId, DecideQaSampleDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(DecisionResult);
    }

    private sealed class StubQaQueueService : IQaQueueService
    {
        public QaQueueDto QueueResult { get; set; } = null!;
        public QaQueueDetailDto DetailResult { get; set; } = null!;

        public Task<QaQueueDto> GetQueueAsync(QaQueueFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(QueueResult);

        public Task<QaQueueDetailDto> GetItemAsync(string kind, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(DetailResult);
    }

    private sealed class StubQaDashboardService : IQaDashboardService
    {
        public Task<QaDashboardDto> GetDashboardAsync(QaDashboardFilterDto filter, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PagedResult<QaReconciliationItemDto>> GetReconciliationsAsync(QaReconciliationFilterDto filter, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<QaDashboardFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}

