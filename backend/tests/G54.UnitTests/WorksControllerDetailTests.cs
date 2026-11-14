using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace G54.UnitTests;

/// <summary>The three actions behind the work detail screen hand the caller and the body to the service and shape the answer.</summary>
public sealed class WorksControllerDetailTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private sealed class Service : IWorkService
    {
        public IReadOnlyList<WorkActivityDto>? Activities { get; init; }

        public (Guid Id, Guid Item, Guid User, WorkChecklistItemStateDto Dto)? Ticked { get; private set; }

        public (Guid Id, Guid User, UpdateWorkPlanDto Dto)? Planned { get; private set; }

        public Task<IReadOnlyList<WorkActivityDto>?> GetActivitiesAsync(Guid id, Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Activities);

        public Task<WorkDetailDto> SetChecklistItemAsync(Guid id, Guid itemId, Guid userId, WorkChecklistItemStateDto dto, CancellationToken cancellationToken = default)
        {
            Ticked = (id, itemId, userId, dto);
            return Task.FromResult(Detail(id));
        }

        public Task<WorkDetailDto> UpdatePlanAsync(Guid id, Guid userId, UpdateWorkPlanDto dto, CancellationToken cancellationToken = default)
        {
            Planned = (id, userId, dto);
            return Task.FromResult(Detail(id));
        }

        public Task<WorkDetailDto> CreateWorkAsync(CreateWorkRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<WorkListItemDto>> GetWorksAsync(Guid accountId, WorkFilterDto filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkDetailDto?> GetByIdAsync(Guid id, Guid accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkDetailDto> ChangeStatusAsync(Guid id, Guid userId, WorkStatusDto dto, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> MarkOverdueAsync(DateOnly today, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static WorkDetailDto Detail(Guid id) => new(
        id, "WK-1", "Title", null, null, null, null, null, Guid.NewGuid(), null, null, "Medium", "InProgress", null, null, null, DateTimeOffset.UtcNow);

    private static WorksController ControllerFor(IWorkService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AccountId.ToString())], "TestAuth")),
            },
        },
    };

    [Fact]
    public async Task GetActivities_ReturnsTheLogOfTheWork()
    {
        var log = new List<WorkActivityDto> { new(Guid.NewGuid(), "ASSIGNED", "Việc được giao", null, null, DateTimeOffset.UtcNow) };

        var result = await ControllerFor(new Service { Activities = log }).GetActivities(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<IReadOnlyList<WorkActivityDto>>>(ok.Value);
        Assert.Equal(log, body.Data);
    }

    [Fact]
    public async Task GetActivities_ForAWorkTheCallerCannotSee_Is404()
    {
        var result = await ControllerFor(new Service { Activities = null }).GetActivities(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<NotFoundObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task SetChecklistItem_PassesTheCallerAndTheStateOn()
    {
        var service = new Service();
        var workId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dto = new WorkChecklistItemStateDto { IsDone = true };

        var result = await ControllerFor(service).SetChecklistItem(workId, itemId, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((workId, itemId, AccountId, dto), service.Ticked);
    }

    [Fact]
    public async Task UpdatePlan_PassesTheCallerAndThePlanOn()
    {
        var service = new Service();
        var workId = Guid.NewGuid();
        var dto = new UpdateWorkPlanDto { PlannedEndDate = new DateOnly(2026, 10, 27), Note = "Khách hẹn lại" };

        var result = await ControllerFor(service).UpdatePlan(workId, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((workId, AccountId, dto), service.Planned);
    }
}
