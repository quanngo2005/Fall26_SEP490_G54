using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Employee;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace G54.UnitTests;

/// <summary>Endpoints of <see cref="EmployeeController"/> not covered by <c>EmployeeLookupTests</c>.</summary>
public sealed class EmployeeControllerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    [Fact]
    public async Task GetEmployees_ForwardsTheFilterAndWrapsThePage()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);
        var filter = new EmployeeFilterDto();

        var result = await controller.GetEmployees(filter, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(filter, service.LastFilter);
        Assert.Equal(AccountId, service.LastCallerAccountId);
        Assert.True(Assert.IsType<ApiResponse<PagedResult<EmployeeDetailDto>>>(ok.Value).Success);
    }

    [Fact]
    public async Task GetCurrentEmployee_ResolvesTheEmployeeFromTheCallerAccount()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);

        var result = await controller.GetCurrentEmployee(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(AccountId, service.LastCurrentAccountId);
        Assert.Equal(service.Detail.Id, Assert.IsType<ApiResponse<EmployeeDetailDto>>(ok.Value).Data!.Id);
    }

    [Fact]
    public async Task GetEmployeeById_Returns200WhenFound()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);

        var result = await controller.GetEmployeeById(service.Detail.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(AccountId, service.LastCallerAccountId);
        Assert.Equal("NV020", Assert.IsType<ApiResponse<EmployeeDetailDto>>(ok.Value).Data!.EmployeeCode);
    }

    [Fact]
    public async Task GetEmployeeById_Returns404Envelope_WhenMissing()
    {
        var controller = ControllerFor(new RecordingEmployeeService { ReturnMissing = true });

        var result = await controller.GetEmployeeById(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse>(notFound.Value);
        Assert.False(body.Success);
        Assert.Equal(ErrorCodes.NotFound, body.ErrorCode);
    }

    [Fact]
    public async Task Options_EndpointsReturnTheServiceLists()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);

        var departments = await controller.GetDepartmentOptions(CancellationToken.None);
        var positions = await controller.GetJobPositionOptions(CancellationToken.None);

        Assert.Single(Assert.IsType<ApiResponse<List<DepartmentOptionDto>>>(Assert.IsType<OkObjectResult>(departments.Result).Value).Data!);
        Assert.Single(Assert.IsType<ApiResponse<List<JobPositionOptionDto>>>(Assert.IsType<OkObjectResult>(positions.Result).Value).Data!);
    }

    [Fact]
    public async Task CreateEmployee_Returns201PointingAtTheDetailRoute()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);
        var dto = new CreateEmployeeDto();

        var result = await controller.CreateEmployee(dto, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Same(dto, service.LastCreate);
        Assert.Equal(nameof(EmployeeController.GetEmployeeById), created.ActionName);
        Assert.Equal(service.Detail.Id, created.RouteValues!["id"]);
        var body = Assert.IsType<ApiResponse<EmployeeDetailDto>>(created.Value);
        Assert.Equal("Tạo nhân viên thành công.", body.Message);
    }

    [Fact]
    public async Task UpdateEmployee_ForwardsTheIdAndBody()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);
        var id = Guid.NewGuid();
        var dto = new UpdateEmployeeDto();

        var result = await controller.UpdateEmployee(id, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((id, dto), (service.LastUpdateId, service.LastUpdate));
    }

    [Fact]
    public async Task ChangeEmployeeStatus_ForwardsTheStatusAndReturnsOk()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);
        var id = Guid.NewGuid();
        var dto = new EmployeeStatusDto();

        var result = await controller.ChangeEmployeeStatus(id, dto, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((id, dto), (service.LastStatusId, service.LastStatus));
    }

    [Fact]
    public async Task DeleteEmployee_ForwardsTheIdAndReturnsOk()
    {
        var service = new RecordingEmployeeService();
        var controller = ControllerFor(service);
        var id = Guid.NewGuid();

        var result = await controller.DeleteEmployee(id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.LastDeletedId);
    }

    private static EmployeeController ControllerFor(IEmployeeService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AccountId.ToString())], "TestAuth")),
            },
        },
    };

    private sealed class RecordingEmployeeService : IEmployeeService
    {
        public EmployeeDetailDto Detail { get; } = new(
            Guid.NewGuid(),
            "NV020",
            "Le Van Nam",
            "nam.le@progest.local",
            null,
            Guid.NewGuid(),
            "DEP-01",
            "Retail",
            Guid.NewGuid(),
            "JP-01",
            "Officer",
            null,
            null,
            null,
            "ACTIVE",
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        public bool ReturnMissing { get; init; }

        public EmployeeFilterDto? LastFilter { get; private set; }

        public Guid LastCurrentAccountId { get; private set; }

        public Guid LastCallerAccountId { get; private set; }

        public CreateEmployeeDto? LastCreate { get; private set; }

        public Guid LastUpdateId { get; private set; }

        public UpdateEmployeeDto? LastUpdate { get; private set; }

        public Guid LastStatusId { get; private set; }

        public EmployeeStatusDto? LastStatus { get; private set; }

        public Guid LastDeletedId { get; private set; }

        public Task<PagedResult<EmployeeDetailDto>> GetEmployeesAsync(Guid callerAccountId, EmployeeFilterDto filter, CancellationToken cancellationToken = default)
        {
            LastCallerAccountId = callerAccountId;
            LastFilter = filter;
            return Task.FromResult(new PagedResult<EmployeeDetailDto>([Detail], 1, 1, 20));
        }

        public Task<EmployeeDetailDto?> GetEmployeeByIdAsync(Guid callerAccountId, Guid id, CancellationToken cancellationToken = default)
        {
            LastCallerAccountId = callerAccountId;
            return Task.FromResult(ReturnMissing ? null : (EmployeeDetailDto?)Detail);
        }

        public Task<EmployeeDetailDto?> GetEmployeeByCodeAsync(Guid callerAccountId, string code, CancellationToken cancellationToken = default)
        {
            LastCallerAccountId = callerAccountId;
            return Task.FromResult<EmployeeDetailDto?>(null);
        }

        public Task<EmployeeDetailDto> GetCurrentEmployeeAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            LastCurrentAccountId = accountId;
            return Task.FromResult(Detail);
        }

        public Task<List<DepartmentOptionDto>> GetDepartmentOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<List<DepartmentOptionDto>>([new DepartmentOptionDto(Guid.NewGuid(), "DEP-01", "Retail", G54.DAL.Entities.DepartmentLevelType.Department)]);

        public Task<List<JobPositionOptionDto>> GetJobPositionOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<List<JobPositionOptionDto>>([new JobPositionOptionDto(Guid.NewGuid(), "JP-01", "Officer")]);

        public Task<EmployeeDetailDto> CreateEmployeeAsync(CreateEmployeeDto dto, CancellationToken cancellationToken = default)
        {
            LastCreate = dto;
            return Task.FromResult(Detail);
        }

        public Task<EmployeeDetailDto> UpdateEmployeeAsync(Guid id, UpdateEmployeeDto dto, CancellationToken cancellationToken = default)
        {
            LastUpdateId = id;
            LastUpdate = dto;
            return Task.FromResult(Detail);
        }

        public Task ChangeStatusAsync(Guid id, EmployeeStatusDto dto, CancellationToken cancellationToken = default)
        {
            LastStatusId = id;
            LastStatus = dto;
            return Task.CompletedTask;
        }

        public Task DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LastDeletedId = id;
            return Task.CompletedTask;
        }
    }
}
