using System.Security.Claims;
using G54.Api.Controllers;
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

    private sealed class RecordingService : IKpiDefinitionService
    {
        public KpiDefinitionDto Created { get; init; } = Sample();

        public Guid AccountId { get; private set; }

        public Guid Id { get; private set; }

        public string? Called { get; private set; }

        public KpiRequestDto? RequestDto { get; private set; }

        public RejectKpiDto? RejectDto { get; private set; }

        public Task<PagedResult<KpiDefinitionDto>> GetKpisAsync(KpiFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<KpiDefinitionDto>([], 0, 1, 20));

        public Task<KpiDefinitionDto?> GetKpiByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<KpiDefinitionDto?>(Created);

        public Task<List<KpiGroupOptionDto>> GetGroupOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<KpiGroupOptionDto>());

        public Task<KpiDefinitionDto> CreateKpiAsync(KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(Created);

        public Task<KpiDefinitionDto> RequestKpiAsync(Guid accountId, KpiRequestDto dto, CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            RequestDto = dto;
            return Task.FromResult(Created);
        }

        public Task<KpiDefinitionDto> UpdateKpiAsync(Guid id, KpiDefinitionSaveDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(Created);

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
    }
}
