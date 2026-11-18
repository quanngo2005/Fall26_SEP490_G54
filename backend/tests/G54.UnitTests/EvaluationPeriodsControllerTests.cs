using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.EvaluationPeriod;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace G54.UnitTests;

public sealed class EvaluationPeriodsControllerTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static ControllerContext Context() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-ep-1",
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", CallerId.ToString())], "test")),
        },
    };

    [Theory]
    [InlineData(nameof(EvaluationPeriodsController.GetPeriods), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(EvaluationPeriodsController.GetPeriodById), typeof(HttpGetAttribute), "{id:guid}")]
    [InlineData(nameof(EvaluationPeriodsController.CreatePeriod), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(EvaluationPeriodsController.ClosePeriod), typeof(HttpPostAttribute), "{id:guid}/close")]
    public void EveryAction_UsesDocumentedVerbAndRoute(string action, Type verb, string? template)
    {
        var method = typeof(EvaluationPeriodsController).GetMethod(action)!;
        var attribute = (HttpMethodAttribute)method.GetCustomAttribute(verb)!;

        Assert.NotNull(attribute);
        Assert.Equal(template, attribute.Template);
    }

    [Theory]
    [InlineData(nameof(EvaluationPeriodsController.GetPeriods), "evaluation", "read", PermissionScopes.Branch)]
    [InlineData(nameof(EvaluationPeriodsController.GetPeriodById), "evaluation", "read", PermissionScopes.Branch)]
    [InlineData(nameof(EvaluationPeriodsController.CreatePeriod), "evaluation", "finalize", PermissionScopes.Branch)]
    [InlineData(nameof(EvaluationPeriodsController.ClosePeriod), "evaluation", "finalize", PermissionScopes.Branch)]
    public void EveryAction_DeclaresDocumentedPermission(string action, string resource, string actionName, string scope)
    {
        var method = typeof(EvaluationPeriodsController).GetMethod(action)!;
        var perm = method.GetCustomAttribute<HasPermissionAttribute>()!;

        Assert.NotNull(perm);
        Assert.Equal(resource, perm.Resource);
        Assert.Equal(actionName, perm.Action);
        Assert.Equal(scope, perm.Scope);
    }

    [Fact]
    public async Task GetPeriods_ForwardsFilter_AndReturnsOkWithPagedResult()
    {
        var stub = new StubEvaluationPeriodService();
        var controller = new EvaluationPeriodsController(stub) { ControllerContext = Context() };
        var filter = new EvaluationPeriodFilterDto { Keyword = "test", Page = 1, PageSize = 10 };

        var actionResult = await controller.GetPeriods(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<EvaluationPeriodItemDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal(StatusCodes.Status200OK, envelope.Status);
        Assert.Same(filter, stub.PassedFilter);
        Assert.Equal(1, envelope.Data!.TotalCount);
        Assert.Single(envelope.Data.Items);
    }

    [Fact]
    public async Task GetPeriodById_WhenFound_ReturnsOkWithItemDto()
    {
        var periodId = Guid.NewGuid();
        var stub = new StubEvaluationPeriodService
        {
            PeriodToReturn = new EvaluationPeriodItemDto(
                periodId, "2026-M01", "Tháng 1/2026", "MONTH", null, null, null,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "OPEN", DateTimeOffset.UtcNow),
        };
        var controller = new EvaluationPeriodsController(stub) { ControllerContext = Context() };

        var actionResult = await controller.GetPeriodById(periodId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<EvaluationPeriodItemDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal(periodId, envelope.Data!.Id);
        Assert.Equal(periodId, stub.PassedId);
    }

    [Fact]
    public async Task GetPeriodById_WhenNotFound_ReturnsNotFoundEnvelope()
    {
        var stub = new StubEvaluationPeriodService { PeriodToReturn = null };
        var controller = new EvaluationPeriodsController(stub) { ControllerContext = Context() };

        var actionResult = await controller.GetPeriodById(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse>(notFound.Value);
        Assert.False(envelope.Success);
        Assert.Equal(StatusCodes.Status404NotFound, envelope.Status);
        Assert.Equal(ErrorCodes.NotFound, envelope.ErrorCode);
    }

    [Fact]
    public async Task CreatePeriod_ReturnsCreatedStatus()
    {
        var stub = new StubEvaluationPeriodService();
        var controller = new EvaluationPeriodsController(stub) { ControllerContext = Context() };
        var request = new CreateEvaluationPeriodRequestDto("2026-M01", "Tháng 1/2026", "MONTH", null, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var actionResult = await controller.CreatePeriod(request, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var envelope = Assert.IsType<ApiResponse<EvaluationPeriodStatusDto>>(created.Value);
        Assert.True(envelope.Success);
        Assert.Equal(StatusCodes.Status201Created, envelope.Status);
        Assert.Same(request, stub.PassedCreateRequest);
    }

    [Fact]
    public async Task ClosePeriod_ReturnsOkWithCloseResult()
    {
        var periodId = Guid.NewGuid();
        var stub = new StubEvaluationPeriodService();
        var controller = new EvaluationPeriodsController(stub) { ControllerContext = Context() };

        var actionResult = await controller.ClosePeriod(periodId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<ClosePeriodResultDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal(periodId, stub.PassedId);
        Assert.Equal("IN_REVIEW", envelope.Data!.Status);
    }

    private sealed class StubEvaluationPeriodService : IEvaluationPeriodService
    {
        public EvaluationPeriodFilterDto? PassedFilter { get; private set; }
        public Guid? PassedId { get; private set; }
        public CreateEvaluationPeriodRequestDto? PassedCreateRequest { get; private set; }
        public EvaluationPeriodItemDto? PeriodToReturn { get; set; }

        public Task<PagedResult<EvaluationPeriodItemDto>> GetPeriodsAsync(EvaluationPeriodFilterDto filter, CancellationToken cancellationToken = default)
        {
            PassedFilter = filter;
            var sample = new EvaluationPeriodItemDto(
                Guid.NewGuid(), "2026-Y", "Năm 2026", "YEAR", null, null, null,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "OPEN", DateTimeOffset.UtcNow);
            return Task.FromResult(new PagedResult<EvaluationPeriodItemDto>([sample], 1, filter.Page, filter.PageSize));
        }

        public Task<EvaluationPeriodItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            PassedId = id;
            return Task.FromResult(PeriodToReturn);
        }

        public Task<EvaluationPeriodStatusDto> CreateAsync(CreateEvaluationPeriodRequestDto request, CancellationToken cancellationToken = default)
        {
            PassedCreateRequest = request;
            return Task.FromResult(new EvaluationPeriodStatusDto(
                Guid.NewGuid(), request.Code ?? "P", request.Name ?? "Period", request.PeriodType ?? "YEAR",
                request.ParentPeriodId, request.StartDate ?? new DateOnly(2026, 1, 1), request.EndDate ?? new DateOnly(2026, 12, 31), "OPEN"));
        }

        public Task<ClosePeriodResultDto> CloseAsync(Guid periodId, CancellationToken cancellationToken = default)
        {
            PassedId = periodId;
            return Task.FromResult(new ClosePeriodResultDto(periodId, "IN_REVIEW", 0, 0));
        }
    }
}

