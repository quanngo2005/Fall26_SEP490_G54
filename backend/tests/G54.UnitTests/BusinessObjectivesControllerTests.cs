using System.Reflection;
using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.BusinessObjective;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace G54.UnitTests;

public sealed class BusinessObjectivesControllerTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static ControllerContext Context() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-1",
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", CallerId.ToString())], "test")),
        },
    };

    private static BusinessObjectiveDto Sample() => new(
        Guid.NewGuid(), null, "OBJ-1", "Growth", null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, "ACTIVE", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(nameof(BusinessObjectivesController.GetObjectives), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(BusinessObjectivesController.GetObjective), typeof(HttpGetAttribute), "{id:guid}")]
    [InlineData(nameof(BusinessObjectivesController.CreateObjective), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(BusinessObjectivesController.UpdateObjective), typeof(HttpPutAttribute), "{id:guid}")]
    [InlineData(nameof(BusinessObjectivesController.DeleteObjective), typeof(HttpDeleteAttribute), "{id:guid}")]
    public void EveryAction_UsesTheDocumentedVerbAndRoute(string action, Type verb, string? template)
    {
        var attribute = (HttpMethodAttribute)typeof(BusinessObjectivesController).GetMethod(action)!.GetCustomAttribute(verb)!;

        Assert.Equal(template, attribute.Template);
    }

    [Theory]
    [InlineData(nameof(KpiDefinitionController.RequestKpi), typeof(HttpPostAttribute), "requests")]
    [InlineData(nameof(KpiDefinitionController.ApproveKpi), typeof(HttpPostAttribute), "{id:guid}/approve")]
    [InlineData(nameof(KpiDefinitionController.RejectKpi), typeof(HttpPostAttribute), "{id:guid}/reject")]
    public void KpiApprovalActions_UseTheDocumentedVerbAndRoute(string action, Type verb, string template)
    {
        var attribute = (HttpMethodAttribute)typeof(KpiDefinitionController).GetMethod(action)!.GetCustomAttribute(verb)!;

        Assert.Equal(template, attribute.Template);
    }

    [Fact]
    public async Task List_PassesTheCallerAndTheFilterToTheService()
    {
        var service = new RecordingService();
        var controller = new BusinessObjectivesController(service) { ControllerContext = Context() };
        var filter = new BusinessObjectiveFilterDto { Keyword = "x" };

        var result = await controller.GetObjectives(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<ApiResponse<IReadOnlyList<BusinessObjectiveDto>>>(ok.Value);
        Assert.Equal(CallerId, service.AccountId);
        Assert.Same(filter, service.Filter);
    }

    [Fact]
    public async Task Create_Returns201WithALocationToTheDetail()
    {
        var created = Sample();
        var service = new RecordingService { Created = created };
        var controller = new BusinessObjectivesController(service) { ControllerContext = Context() };

        var result = await controller.CreateObjective(new BusinessObjectiveSaveDto { Code = "OBJ-1" }, CancellationToken.None);

        var response = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Equal(nameof(BusinessObjectivesController.GetObjective), response.ActionName);
        Assert.Equal(created.Id, response.RouteValues!["id"]);
        Assert.True(Assert.IsType<ApiResponse<BusinessObjectiveDto>>(response.Value).Success);
    }

    [Fact]
    public async Task Delete_CallsTheServiceAndReturnsOk()
    {
        var service = new RecordingService();
        var controller = new BusinessObjectivesController(service) { ControllerContext = Context() };
        var id = Guid.NewGuid();

        var result = await controller.DeleteObjective(id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.DeletedId);
    }

    private sealed class RecordingService : IBusinessObjectiveService
    {
        public BusinessObjectiveDto Created { get; init; } = Sample();

        public Guid AccountId { get; private set; }

        public BusinessObjectiveFilterDto? Filter { get; private set; }

        public Guid? DeletedId { get; private set; }

        public Task<IReadOnlyList<BusinessObjectiveDto>> GetObjectivesAsync(
            Guid accountId, BusinessObjectiveFilterDto filter, CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            Filter = filter;
            return Task.FromResult<IReadOnlyList<BusinessObjectiveDto>>([]);
        }

        public Task<BusinessObjectiveDetailDto> GetObjectiveAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BusinessObjectiveDetailDto(Created, new BusinessObjectiveUsageDto(0, 0, 0, 0), []));

        public Task<BusinessObjectiveDto> CreateObjectiveAsync(BusinessObjectiveSaveDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(Created);

        public Task<BusinessObjectiveDto> UpdateObjectiveAsync(Guid id, BusinessObjectiveSaveDto dto, CancellationToken cancellationToken = default) =>
            Task.FromResult(Created);

        public Task DeleteObjectiveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            DeletedId = id;
            return Task.CompletedTask;
        }
    }
}
