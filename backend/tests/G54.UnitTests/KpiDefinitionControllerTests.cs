using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace G54.UnitTests;

public sealed class KpiDefinitionControllerTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static KpiDefinitionController CreateController(RecordingService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-1",
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", CallerId.ToString())], "test")),
                },
            },
        };

    private static KpiDefinitionDto Sample() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Group", "KPI-1", "Kpi", null, null, "VND", "SUM", "HIGHER_BETTER", null, "DRAFT", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Request_Returns201WithALocationToTheDetail_AndPassesTheCaller()
    {
        var created = Sample();
        var service = new RecordingService { Created = created };
        var dto = new KpiRequestDto { RequestNote = "why" };

        var result = await CreateController(service).RequestKpi(dto, CancellationToken.None);

        var response = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Equal(nameof(KpiDefinitionController.GetKpiById), response.ActionName);
        Assert.Equal(created.Id, response.RouteValues!["id"]);
        Assert.True(Assert.IsType<ApiResponse<KpiDefinitionDto>>(response.Value).Success);
        Assert.Equal(CallerId, service.AccountId);
        Assert.Same(dto, service.RequestDto);
    }

    [Fact]
    public async Task ChangeStatus_PassesTheCallerAsTheMaker()
    {
        var service = new RecordingService();
        var id = Guid.NewGuid();
        var dto = new KpiStatusDto { Status = "PENDING_APPROVAL" };

        var result = await CreateController(service).ChangeKpiStatus(id, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((CallerId, id, "status"), (service.AccountId, service.Id, service.Called));
    }

    [Fact]
    public async Task Approve_PassesTheCallerAndTheKpi()
    {
        var service = new RecordingService();
        var id = Guid.NewGuid();

        var result = await CreateController(service).ApproveKpi(id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((CallerId, id, "approve"), (service.AccountId, service.Id, service.Called));
    }

    [Fact]
    public async Task Reject_PassesTheCallerTheKpiAndTheReason()
    {
        var service = new RecordingService();
        var id = Guid.NewGuid();
        var dto = new RejectKpiDto { Reason = "Formula missing" };

        var result = await CreateController(service).RejectKpi(id, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((CallerId, id, "reject"), (service.AccountId, service.Id, service.Called));
        Assert.Same(dto, service.RejectDto);
    }

    [Fact]
    public async Task List_ReturnsThePageAndPassesTheFilter()
    {
        var service = new RecordingService();
        var filter = new KpiFilterDto();

        var result = await CreateController(service).GetKpis(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ApiResponse<PagedResult<KpiDefinitionDto>>>(ok.Value).Success);
        Assert.Same(filter, service.Filter);
    }

    [Fact]
    public async Task Groups_ReturnsTheGroupOptions()
    {
        var result = await CreateController(new RecordingService()).GetGroupOptions(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ApiResponse<List<KpiGroupOptionDto>>>(ok.Value).Success);
    }

    [Fact]
    public async Task Detail_ReturnsTheKpi()
    {
        var kpi = Sample();

        var result = await CreateController(new RecordingService { Created = kpi }).GetKpiById(kpi.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(kpi, Assert.IsType<ApiResponse<KpiDefinitionDto>>(ok.Value).Data);
    }

    [Fact]
    public async Task Detail_OfAnUnknownKpi_Is404WithTheTraceId()
    {
        var result = await CreateController(new RecordingService { Missing = true }).GetKpiById(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse>(notFound.Value);
        Assert.False(response.Success);
        Assert.Equal(StatusCodes.Status404NotFound, response.Status);
        Assert.Equal("trace-1", response.TraceId);
    }

    [Fact]
    public async Task Create_Returns201WithALocationToTheDetail()
    {
        var created = Sample();
        var service = new RecordingService { Created = created };
        var dto = new KpiDefinitionSaveDto();

        var result = await CreateController(service).CreateKpi(dto, CancellationToken.None);

        var response = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(KpiDefinitionController.GetKpiById), response.ActionName);
        Assert.Equal(created.Id, response.RouteValues!["id"]);
        Assert.Same(dto, service.SaveDto);
    }

    [Fact]
    public async Task Update_ReturnsTheUpdatedKpiAndPassesTheIdAndBody()
    {
        var service = new RecordingService();
        var id = Guid.NewGuid();
        var dto = new KpiDefinitionSaveDto();

        var result = await CreateController(service).UpdateKpi(id, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ApiResponse<KpiDefinitionDto>>(ok.Value).Success);
        Assert.Equal((id, "update"), (service.Id, service.Called));
        Assert.Same(dto, service.SaveDto);
    }

    [Fact]
    public async Task Summary_ReturnsTheCounts()
    {
        var result = await CreateController(new RecordingService()).GetSummary(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(24, Assert.IsType<ApiResponse<KpiSummaryDto>>(ok.Value).Data!.Total);
    }

    [Fact]
    public async Task UsageAndHistory_ReturnTheDataOfAnExistingKpi()
    {
        var controller = CreateController(new RecordingService());

        Assert.IsType<OkObjectResult>((await controller.GetUsage(Guid.NewGuid(), CancellationToken.None)).Result);
        var history = Assert.IsType<OkObjectResult>((await controller.GetHistory(Guid.NewGuid(), CancellationToken.None)).Result);
        Assert.Single(Assert.IsType<ApiResponse<IReadOnlyList<KpiHistoryEntryDto>>>(history.Value).Data!);
    }

    [Fact]
    public async Task UsageAndHistory_OfAnUnknownKpi_AreNotFound()
    {
        var controller = CreateController(new RecordingService { Missing = true });

        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetUsage(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetHistory(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CodeSuggestion_ReturnsTheCodeForTheName()
    {
        var service = new RecordingService();

        var result = await CreateController(service).SuggestCode("Tiền gửi", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("TIEN_GUI", Assert.IsType<ApiResponse<KpiCodeSuggestionDto>>(ok.Value).Data!.Code);
        Assert.Equal("Tiền gửi", service.SuggestedFor);
    }

    [Theory]
    [InlineData(nameof(KpiDefinitionController.SuggestCode))]
    [InlineData(nameof(KpiDefinitionController.GetSummary))]
    [InlineData(nameof(KpiDefinitionController.GetUsage))]
    [InlineData(nameof(KpiDefinitionController.GetHistory))]
    public void TheCatalogReads_NeedTheKpiReadPermission(string action)
    {
        var required = typeof(KpiDefinitionController).GetMethod(action)!.GetCustomAttribute<HasPermissionAttribute>();

        Assert.NotNull(required);
        Assert.Equal(("kpi", "read", "All"), (required.Resource, required.Action, required.Scope));
    }

    private sealed class RecordingService : IKpiDefinitionService
    {
        public KpiDefinitionDto Created { get; init; } = Sample();

        public Guid AccountId { get; private set; }

        public Guid Id { get; private set; }

        public string? Called { get; private set; }

        public KpiRequestDto? RequestDto { get; private set; }

        public RejectKpiDto? RejectDto { get; private set; }

        public KpiFilterDto? Filter { get; private set; }

        public KpiDefinitionSaveDto? SaveDto { get; private set; }

        /// <summary>When set, the detail lookup finds nothing.</summary>
        public bool Missing { get; init; }

        public Task<PagedResult<KpiDefinitionDto>> GetKpisAsync(KpiFilterDto filter, CancellationToken cancellationToken = default)
        {
            Filter = filter;
            return Task.FromResult(new PagedResult<KpiDefinitionDto>([], 0, 1, 20));
        }

        public Task<KpiDefinitionDto?> GetKpiByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<KpiDefinitionDto?>(Missing ? null : Created);

        public Task<List<KpiGroupOptionDto>> GetGroupOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<KpiGroupOptionDto>());

        public Task<KpiDefinitionDto> CreateKpiAsync(KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default)
        {
            SaveDto = dto;
            return Task.FromResult(Created);
        }

        public Task<KpiDefinitionDto> RequestKpiAsync(Guid accountId, KpiRequestDto dto, CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            RequestDto = dto;
            return Task.FromResult(Created);
        }

        public Task<KpiDefinitionDto> UpdateKpiAsync(Guid id, KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default)
        {
            (Id, Called, SaveDto) = (id, "update", dto);
            return Task.FromResult(Created);
        }

        public Task ChangeStatusAsync(Guid accountId, Guid id, KpiStatusDto dto, CancellationToken cancellationToken = default)
        {
            (AccountId, Id, Called) = (accountId, id, "status");
            return Task.CompletedTask;
        }

        public Task ApproveKpiAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default)
        {
            (AccountId, Id, Called) = (accountId, id, "approve");
            return Task.CompletedTask;
        }

        public Task RejectKpiAsync(Guid accountId, Guid id, RejectKpiDto dto, CancellationToken cancellationToken = default)
        {
            (AccountId, Id, Called, RejectDto) = (accountId, id, "reject", dto);
            return Task.CompletedTask;
        }

        public string? SuggestedFor { get; private set; }

        public Task<KpiCodeSuggestionDto> SuggestCodeAsync(string? name, CancellationToken cancellationToken = default)
        {
            SuggestedFor = name;
            return Task.FromResult(new KpiCodeSuggestionDto("TIEN_GUI"));
        }

        public Task<KpiSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new KpiSummaryDto(24, 1, 2, 0, 18, 3, 0));

        public Task<KpiUsageDto?> GetUsageAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<KpiUsageDto?>(Missing ? null : new KpiUsageDto([], 0, []));

        public Task<IReadOnlyList<KpiHistoryEntryDto>?> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KpiHistoryEntryDto>?>(Missing ? null : [new KpiHistoryEntryDto(DateTimeOffset.UtcNow, "An", "CREATE", "Tạo")]);
    }
}
