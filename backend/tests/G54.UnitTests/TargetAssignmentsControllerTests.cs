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

    private static TargetAssignmentDto Sample() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "ORG", 1, 400m, "ACTIVE",
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Controller_UsesTheCanonicalRootAndTheTargetManagePermission()
    {
        Assert.Equal("target-assignments", typeof(TargetAssignmentsController).GetCustomAttribute<RouteAttribute>()?.Template);

        var permission = Assert.Single(typeof(TargetAssignmentsController).GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal(("target", "manage", "Dept"), (permission.Resource, permission.Action, permission.Scope));
    }

    [Fact]
    public void Actions_HaveTheDocumentedVerbsAndRoutes()
    {
        Assert.Null(typeof(TargetAssignmentsController).GetMethod(nameof(TargetAssignmentsController.CreateRoot))
            ?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal("{id:guid}/allocations", typeof(TargetAssignmentsController).GetMethod(nameof(TargetAssignmentsController.Allocate))
            ?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal("{id:guid}", typeof(TargetAssignmentsController).GetMethod(nameof(TargetAssignmentsController.GetById))
            ?.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    [Fact]
    public async Task CreateRoot_Returns201WithLocationAndAnEnvelopeStatusOf201()
    {
        var service = new RecordingService { Result = Sample() };
        var dto = new CreateTargetAssignmentDto { KpiDefinitionId = Guid.NewGuid(), TargetValue = 5 };

        var actionResult = await CreateController(service).CreateRoot(dto);

        var created = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        Assert.Equal(nameof(TargetAssignmentsController.GetById), created.ActionName);
        Assert.Equal(service.Result.Id, created.RouteValues!["id"]);
        var envelope = Assert.IsType<ApiResponse<TargetAssignmentDto>>(created.Value);
        Assert.Equal(201, envelope.Status);
        Assert.Equal("trace-1", envelope.TraceId);
        Assert.Same(service.Result, envelope.Data);
        Assert.Equal(AccountId, service.CallerId);
        Assert.Same(dto, service.RootRequest);
    }

    [Fact]
    public async Task Allocate_Returns201WithLocationOfTheParentAndTheChildren()
    {
        var parentId = Guid.NewGuid();
        var service = new RecordingService { Children = [Sample(), Sample()] };
        var dto = new AllocateTargetDto { Allocations = [new TargetAllocationItemDto { DepartmentId = Guid.NewGuid(), TargetValue = 1 }] };

        var actionResult = await CreateController(service).Allocate(parentId, dto);

        var created = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        Assert.Equal(parentId, created.RouteValues!["id"]);
        var envelope = Assert.IsType<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>(created.Value);
        Assert.Equal(201, envelope.Status);
        Assert.Equal(2, envelope.Data!.Count);
        Assert.Equal(parentId, service.RequestedId);
        Assert.Same(dto, service.AllocateRequest);
        Assert.Equal(AccountId, service.CallerId);
    }

    [Fact]
    public async Task GetById_Returns200WithTheEnvelope()
    {
        var service = new RecordingService { Result = Sample() };

        var actionResult = await CreateController(service).GetById(service.Result.Id);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Equal(200, Assert.IsType<ApiResponse<TargetAssignmentDto>>(ok.Value).Status);
        Assert.Equal(service.Result.Id, service.RequestedId);
    }

    [Fact]
    public void Response_SerialisesCamelCaseWithUpperSnakeEnums()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Sample(), JsonOptions));
        var root = json.RootElement;

        Assert.Equal("ORG", root.GetProperty("splitDimension").GetString());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
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
    }

    private sealed class RecordingService : ITargetAssignmentService
    {
        public TargetAssignmentDto Result { get; set; } = null!;

        public IReadOnlyList<TargetAssignmentDto> Children { get; set; } = [];

        public Guid CallerId { get; private set; }

        public CreateTargetAssignmentDto? RootRequest { get; private set; }

        public AllocateTargetDto? AllocateRequest { get; private set; }

        public Guid RequestedId { get; private set; }

        public Task<TargetAssignmentDto> CreateRootAsync(Guid accountId, CreateTargetAssignmentDto dto)
        {
            CallerId = accountId;
            RootRequest = dto;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<TargetAssignmentDto>> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto)
        {
            CallerId = accountId;
            RequestedId = parentAssignmentId;
            AllocateRequest = dto;
            return Task.FromResult(Children);
        }

        public Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId)
        {
            CallerId = accountId;
            RequestedId = assignmentId;
            return Task.FromResult(Result);
        }
    }
}
