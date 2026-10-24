using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace G54.UnitTests;

public sealed class TargetAssignmentsControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid AccountId = Guid.NewGuid();

    private static TargetAssignmentsController CreateController(RecordingService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AccountId.ToString())], "test")),
            },
        },
    };

    private static TargetAssignmentDto Sample(Guid? id = null) => new(
        id ?? Guid.NewGuid(), Guid.NewGuid(), "KPI-REV", "Doanh thu", "VND", "SUM",
        Guid.NewGuid(), "2026-Q4", "Q4", "DEPARTMENT", Guid.NewGuid(), "D", "Phòng D", "DEPARTMENT",
        null, null, null, Guid.NewGuid(), "ORG", 1, 400m, 0m, 400m, "ACTIVE",
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Controller_UsesTheCanonicalRootAndTheTargetManagePermission()
    {
        Assert.Equal("target-assignments", typeof(TargetAssignmentsController).GetCustomAttribute<RouteAttribute>()?.Template);

        var permission = Assert.Single(typeof(TargetAssignmentsController).GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal("target", permission.Resource);
        Assert.Equal("manage", permission.Action);
        Assert.Equal("Dept", permission.Scope);
    }

    [Fact]
    public void Actions_HaveTheDocumentedVerbsAndRoutes()
    {
        var root = typeof(TargetAssignmentsController).GetMethod(nameof(TargetAssignmentsController.CreateRoot));
        var allocate = typeof(TargetAssignmentsController).GetMethod(nameof(TargetAssignmentsController.Allocate));
        var get = typeof(TargetAssignmentsController).GetMethod(nameof(TargetAssignmentsController.GetById));

        Assert.NotNull(root?.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(root?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal("{id:guid}/allocations", allocate?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal("{id:guid}", get?.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    [Fact]
    public async Task CreateRoot_Returns201WithLocationAndAnEnvelopeStatusOf201()
    {
        var service = new RecordingService { Result = Sample() };
        var controller = CreateController(service);
        var dto = new CreateTargetAssignmentDto { KpiDefinitionId = Guid.NewGuid(), TargetValue = 5 };

        var actionResult = await controller.CreateRoot(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        Assert.Equal(nameof(TargetAssignmentsController.GetById), created.ActionName);
        Assert.Equal(service.Result.Id, created.RouteValues!["id"]);
        var envelope = Assert.IsType<ApiResponse<TargetAssignmentDto>>(created.Value);
        Assert.True(envelope.Success);
        Assert.Equal(201, envelope.Status);
        Assert.Equal("trace-1", envelope.TraceId);
        Assert.Same(service.Result, envelope.Data);
        Assert.Equal(AccountId, service.CallerId);
        Assert.Same(dto, service.RootRequest);
    }

    [Fact]
    public async Task Allocate_Returns201WithLocationOfTheParent()
    {
        var parent = Sample();
        var service = new RecordingService { Result = parent, AllocationResult = new TargetAllocationResultDto(parent, [Sample(), Sample()]) };
        var dto = new AllocateTargetDto { Allocations = [new TargetAllocationItemDto { DepartmentId = Guid.NewGuid(), TargetValue = 1 }] };

        var actionResult = await CreateController(service).Allocate(parent.Id, dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        Assert.Equal(nameof(TargetAssignmentsController.GetById), created.ActionName);
        Assert.Equal(parent.Id, created.RouteValues!["id"]);
        var envelope = Assert.IsType<ApiResponse<TargetAllocationResultDto>>(created.Value);
        Assert.Equal(201, envelope.Status);
        Assert.Equal(2, envelope.Data!.Children.Count);
        Assert.Equal(parent.Id, service.RequestedId);
        Assert.Same(dto, service.AllocateRequest);
        Assert.Equal(AccountId, service.CallerId);
    }

    [Fact]
    public async Task GetById_Returns200WithTheEnvelope()
    {
        var service = new RecordingService { Result = Sample() };

        var actionResult = await CreateController(service).GetById(service.Result.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<TargetAssignmentDto>>(ok.Value);
        Assert.Equal(200, envelope.Status);
        Assert.Equal(service.Result.Id, service.RequestedId);
    }
    [Fact]
    public void Response_SerialisesCamelCaseWithUpperSnakeEnumsAndNullsForTheOtherOwner()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Sample(), JsonOptions));
        var root = json.RootElement;

        Assert.Equal("DEPARTMENT", root.GetProperty("ownerType").GetString());
        Assert.Equal("ORG", root.GetProperty("splitDimension").GetString());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.Equal("SUM", root.GetProperty("kpiAggregationType").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("employeeId").ValueKind);
        Assert.Equal(400m, root.GetProperty("targetValue").GetDecimal());
    }

    [Fact]
    public void RequestBodies_BindFromCamelCaseJson()
    {
        const string root = """
            {"kpiDefinitionId":"5eed0000-0000-0000-0005-000000000001","evaluationPeriodId":"5eed0000-0000-0000-0004-000000000001",
             "departmentId":"5eed0000-0000-0000-0001-000000000001","targetValue":400000.25,"changeReason":"Q1"}
            """;
        const string allocate = """
            {"allocations":[{"departmentId":"5eed0000-0000-0000-0001-000000000002","targetValue":300},
                            {"employeeId":"5eed0000-0000-0000-0007-000000000004","targetValue":100.5}],"changeReason":"split"}
            """;

        var rootDto = JsonSerializer.Deserialize<CreateTargetAssignmentDto>(root, JsonOptions)!;
        var allocateDto = JsonSerializer.Deserialize<AllocateTargetDto>(allocate, JsonOptions)!;

        Assert.Equal(400000.25m, rootDto.TargetValue);
        Assert.Equal(Guid.Parse("5eed0000-0000-0000-0001-000000000001"), rootDto.DepartmentId);
        Assert.Equal(2, allocateDto.Allocations!.Count);
        Assert.Null(allocateDto.Allocations[0].EmployeeId);
        Assert.Equal(100.5m, allocateDto.Allocations[1].TargetValue);
        Assert.Equal("split", allocateDto.ChangeReason);
    }

    private sealed class RecordingService : ITargetAssignmentService
    {
        public TargetAssignmentDto Result { get; set; } = null!;

        public TargetAllocationResultDto AllocationResult { get; set; } = null!;

        public Guid CallerId { get; private set; }

        public CreateTargetAssignmentDto? RootRequest { get; private set; }

        public AllocateTargetDto? AllocateRequest { get; private set; }

        public Guid RequestedId { get; private set; }

        public Task<TargetAssignmentDto> CreateRootAsync(Guid accountId, CreateTargetAssignmentDto dto, CancellationToken cancellationToken = default)
        {
            CallerId = accountId;
            RootRequest = dto;
            return Task.FromResult(Result);
        }

        public Task<TargetAllocationResultDto> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto, CancellationToken cancellationToken = default)
        {
            CallerId = accountId;
            RequestedId = parentAssignmentId;
            AllocateRequest = dto;
            return Task.FromResult(AllocationResult);
        }

        public Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId, CancellationToken cancellationToken = default)
        {
            CallerId = accountId;
            RequestedId = assignmentId;
            return Task.FromResult(Result);
        }
    }
}
