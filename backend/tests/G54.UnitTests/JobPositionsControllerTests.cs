using System.Reflection;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.JobPosition;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class JobPositionsControllerTests
{
    private static readonly string[] SampleKpis = ["Giới thiệu"];

    private sealed class StubJobPositionService : IJobPositionService
    {
        public JobPositionDetailDto? DetailToReturn { get; set; }
        public List<JobPositionSummaryDto> SummaryItems { get; } = [];
        public JobPositionFilterDto? LastFilter { get; private set; }
        public Guid? LastId { get; private set; }

        public Task<PagedResult<JobPositionSummaryDto>> GetJobPositionsAsync(
            JobPositionFilterDto filter,
            CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult(new PagedResult<JobPositionSummaryDto>(SummaryItems, SummaryItems.Count, 1, 50));
        }

        public Task<JobPositionDetailDto?> GetJobPositionByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            LastId = id;
            return Task.FromResult(DetailToReturn);
        }

        public AssignJobPositionWorkTypeDto? LastAssign { get; private set; }

        public Task<JobPositionDetailDto> AssignWorkTypeAsync(
            Guid id,
            AssignJobPositionWorkTypeDto request,
            CancellationToken cancellationToken = default)
        {
            LastId = id;
            LastAssign = request;
            return Task.FromResult(DetailToReturn!);
        }

        public Guid? LastRemovedWorkTypeId { get; private set; }

        public Task<JobPositionDetailDto> RemoveWorkTypeAsync(
            Guid id,
            Guid workTypeId,
            CancellationToken cancellationToken = default)
        {
            LastId = id;
            LastRemovedWorkTypeId = workTypeId;
            return Task.FromResult(DetailToReturn!);
        }
    }

    private static JobPositionsController CreateController(StubJobPositionService stub) =>
        new(stub)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-1" },
            },
        };

    [Fact]
    public void ControllerRequiresOrganizationReadAndExposesTheDocumentedRoutes()
    {
        var type = typeof(JobPositionsController);

        var permission = Assert.Single(type.GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal(("organization", "read", "Dept"), (permission.Resource, permission.Action, permission.Scope));
        Assert.Equal(
            (OrganizationAccessCodes.Resource, OrganizationAccessCodes.Read),
            (permission.Resource, permission.Action));
        Assert.Equal("job-positions", type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Null(type.GetMethod(nameof(JobPositionsController.GetJobPositions))?.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal("{id:guid}", type.GetMethod(nameof(JobPositionsController.GetJobPosition))?.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    [Fact]
    public void DepartmentsControllerReadsRequireTheSameOrganizationReadPermission()
    {
        var type = typeof(DepartmentsController);

        var permission = type.GetCustomAttributes<HasPermissionAttribute>().First();
        Assert.Equal(("organization", "read", "Dept"), (permission.Resource, permission.Action, permission.Scope));
        Assert.DoesNotContain(
            type.GetCustomAttributes<HasPermissionAttribute>(),
            a => a.Resource == "evaluation");
    }

    [Fact]
    public async Task GetJobPositionsReturnsTheEnvelopeAndForwardsTheFilter()
    {
        var stub = new StubJobPositionService();
        stub.SummaryItems.Add(
            new(Guid.NewGuid(), "TELLER", "Giao dịch viên", "Mô tả", 14, null, "Teller", 12, SampleKpis, DateTimeOffset.UtcNow, "ACTIVE"));
        var filter = new JobPositionFilterDto(Keyword: "tel", Status: "ACTIVE", Page: 2, PageSize: 10);

        var result = await CreateController(stub).GetJobPositions(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<JobPositionSummaryDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal("trace-1", envelope.TraceId);
        Assert.Equal(1, envelope.Data?.TotalCount);
        Assert.Equal("TELLER", envelope.Data?.Items[0].Code);
        Assert.Same(filter, stub.LastFilter);
    }

    [Fact]
    public async Task GetJobPositionReturnsTheDetailEnvelope()
    {
        var id = Guid.NewGuid();
        var stub = new StubJobPositionService
        {
            DetailToReturn = new(id, "TELLER", "Giao dịch viên", null, 1, DateTimeOffset.UtcNow, "ACTIVE", null, []),
        };

        var result = await CreateController(stub).GetJobPosition(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<JobPositionDetailDto>>(ok.Value);
        Assert.Equal(id, envelope.Data?.Id);
        Assert.Equal(id, stub.LastId);
    }

    [Fact]
    public async Task AssignWorkTypeReturns201WithTheDetailEnvelopeAndRequiresJobPositionManage()
    {
        var id = Guid.NewGuid();
        var workTypeId = Guid.NewGuid();
        var stub = new StubJobPositionService
        {
            DetailToReturn = new(id, "TELLER", "Teller", null, 1, DateTimeOffset.UtcNow, "ACTIVE", null, []),
        };

        var result = await CreateController(stub).AssignWorkType(id, new AssignJobPositionWorkTypeDto(workTypeId), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(JobPositionsController.GetJobPosition), created.ActionName);
        var envelope = Assert.IsType<ApiResponse<JobPositionDetailDto>>(created.Value);
        Assert.Equal(id, envelope.Data?.Id);
        Assert.Equal(workTypeId, stub.LastAssign?.WorkTypeId);

        var method = typeof(JobPositionsController).GetMethod(nameof(JobPositionsController.AssignWorkType));
        Assert.Equal("{id:guid}/work-types", method?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        var permission = Assert.Single(method!.GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal(("job-position", "manage", "Dept"), (permission.Resource, permission.Action, permission.Scope));
    }

    [Fact]
    public async Task RemoveWorkTypeReturns200WithTheDetailEnvelopeAndRequiresJobPositionManage()
    {
        var id = Guid.NewGuid();
        var workTypeId = Guid.NewGuid();
        var stub = new StubJobPositionService
        {
            DetailToReturn = new(id, "TELLER", "Teller", null, 1, DateTimeOffset.UtcNow, "ACTIVE", null, []),
        };

        var result = await CreateController(stub).RemoveWorkType(id, workTypeId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<JobPositionDetailDto>>(ok.Value);
        Assert.Equal(id, envelope.Data?.Id);
        Assert.Equal(id, stub.LastId);
        Assert.Equal(workTypeId, stub.LastRemovedWorkTypeId);

        var method = typeof(JobPositionsController).GetMethod(nameof(JobPositionsController.RemoveWorkType));
        Assert.Equal("{id:guid}/work-types/{workTypeId:guid}", method?.GetCustomAttribute<HttpDeleteAttribute>()?.Template);
        var permission = Assert.Single(method!.GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal(("job-position", "manage", "Dept"), (permission.Resource, permission.Action, permission.Scope));
    }

    [Fact]
    public async Task GetJobPositionRaisesNotFoundForTheGlobalHandlerWhenMissing()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateController(new StubJobPositionService()).GetJobPosition(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
    }
}
