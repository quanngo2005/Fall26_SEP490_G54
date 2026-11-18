using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Department;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class DepartmentsControllerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static DepartmentsController CreateController(StubDepartmentService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-dept-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AccountId.ToString())], "test")),
            },
        },
    };

    private static DepartmentDetailDto SampleDetail(Guid? id = null) => new(
        id ?? Guid.NewGuid(), "D01", "Phòng ban 01", "Mô tả", "DEPARTMENT", "ACTIVE",
        null, [], null, 10, 10, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetDepartments_ReturnsOkWithPagedResult()
    {
        var service = new StubDepartmentService
        {
            PagedResult = new PagedResult<DepartmentSummaryDto>([], 0, 1, 20)
        };
        var controller = CreateController(service);

        var result = await controller.GetDepartments(new DepartmentFilterDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<DepartmentSummaryDto>>>(ok.Value);
        Assert.True(envelope.Success);
    }

    [Fact]
    public async Task GetTree_ReturnsOkWithTree()
    {
        var service = new StubDepartmentService { Tree = [] };
        var controller = CreateController(service);

        var result = await controller.GetTree(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<IReadOnlyList<DepartmentTreeNodeDto>>>(ok.Value);
        Assert.True(envelope.Success);
    }

    [Fact]
    public async Task CreateDepartment_ReturnsCreatedAtAction()
    {
        var detail = SampleDetail();
        var service = new StubDepartmentService { Detail = detail };
        var controller = CreateController(service);

        var result = await controller.CreateDepartment(new CreateDepartmentDto(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(DepartmentsController.GetDepartment), created.ActionName);
        var envelope = Assert.IsType<ApiResponse<DepartmentDetailDto>>(created.Value);
        Assert.True(envelope.Success);
        Assert.Equal(detail.Id, envelope.Data!.Id);
    }

    [Fact]
    public async Task UpdateDepartment_ReturnsOk()
    {
        var detail = SampleDetail();
        var service = new StubDepartmentService { Detail = detail };
        var controller = CreateController(service);

        var result = await controller.UpdateDepartment(detail.Id, new UpdateDepartmentDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<DepartmentDetailDto>>(ok.Value);
        Assert.True(envelope.Success);
    }

    [Fact]
    public async Task ChangeStatus_ReturnsOk()
    {
        var detail = SampleDetail();
        var service = new StubDepartmentService { Detail = detail };
        var controller = CreateController(service);

        var result = await controller.ChangeStatus(detail.Id, new DepartmentStatusDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<DepartmentDetailDto>>(ok.Value);
        Assert.True(envelope.Success);
    }

    [Fact]
    public async Task DeleteDepartment_ReturnsOk()
    {
        var service = new StubDepartmentService();
        var controller = CreateController(service);

        var result = await controller.DeleteDepartment(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(ok.Value);
        Assert.True(envelope.Success);
    }

    [Fact]
    public async Task GetDepartment_ReturnsOk()
    {
        var detail = SampleDetail();
        var service = new StubDepartmentService { Detail = detail };
        var controller = CreateController(service);

        var result = await controller.GetDepartment(detail.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<DepartmentDetailDto>>(ok.Value);
        Assert.True(envelope.Success);
    }

    [Fact]
    public async Task GetOverview_ReturnsOkWithOverview()
    {
        var overview = new DepartmentOverviewDto(
            SampleDetail(), null, null, [],
            new DepartmentMetricsDto(0, 0, 0, 0, 0, 0),
            new DepartmentPerformanceDto(0, 0, null, 0, 0, [], [], [], []),
            [],
            new DepartmentOperationalDto(0, 0, 0, null, null, 0),
            [], [], DateTimeOffset.UtcNow);
        var service = new StubDepartmentService { Overview = overview };
        var controller = CreateController(service);

        var result = await controller.GetOverview(Guid.NewGuid(), null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<DepartmentOverviewDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(overview, envelope.Data);
    }

    [Fact]
    public async Task ExportOverview_ReturnsFileContentResult()
    {
        var export = new DepartmentOverviewExportDto([1, 2, 3], "overview.xlsx");
        var service = new StubDepartmentService { Export = export };
        var controller = CreateController(service);

        var result = await controller.ExportOverview(Guid.NewGuid(), null, null, CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
        Assert.Equal("overview.xlsx", fileResult.FileDownloadName);
        Assert.Equal([1, 2, 3], fileResult.FileContents);
    }

    [Fact]
    public async Task GetMembers_ReturnsOkWithPagedMembers()
    {
        var paged = new PagedResult<DepartmentMemberDto>([], 0, 1, 20);
        var service = new StubDepartmentService { Members = paged };
        var controller = CreateController(service);

        var result = await controller.GetMembers(Guid.NewGuid(), new DepartmentMemberFilterDto(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<DepartmentMemberDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(paged, envelope.Data);
    }

    private sealed class StubDepartmentService : IDepartmentService
    {
        public PagedResult<DepartmentSummaryDto> PagedResult { get; set; } = null!;
        public IReadOnlyList<DepartmentTreeNodeDto> Tree { get; set; } = [];
        public DepartmentDetailDto Detail { get; set; } = null!;
        public DepartmentOverviewDto Overview { get; set; } = null!;
        public DepartmentOverviewExportDto Export { get; set; } = null!;
        public PagedResult<DepartmentMemberDto> Members { get; set; } = null!;

        public Task<PagedResult<DepartmentSummaryDto>> GetDepartmentsAsync(Guid accountId, DepartmentFilterDto filter, CancellationToken ct = default) =>
            Task.FromResult(PagedResult);

        public Task<DepartmentDetailDto> GetDepartmentAsync(Guid accountId, Guid departmentId, CancellationToken ct = default) =>
            Task.FromResult(Detail);

        public Task<DepartmentOverviewDto> GetOverviewAsync(Guid accountId, Guid departmentId, Guid? periodId, string? compare = null, CancellationToken ct = default) =>
            Task.FromResult(Overview);

        public Task<DepartmentOverviewExportDto> ExportOverviewAsync(Guid accountId, Guid departmentId, Guid? periodId, string? compare = null, CancellationToken ct = default) =>
            Task.FromResult(Export);

        public Task<PagedResult<DepartmentMemberDto>> GetMembersAsync(Guid accountId, Guid departmentId, DepartmentMemberFilterDto filter, CancellationToken ct = default) =>
            Task.FromResult(Members);

        public Task<IReadOnlyList<DepartmentTreeNodeDto>> GetTreeAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(Tree);

        public Task<DepartmentDetailDto> CreateDepartmentAsync(CreateDepartmentDto dto, CancellationToken ct = default) =>
            Task.FromResult(Detail);

        public Task<DepartmentDetailDto> UpdateDepartmentAsync(Guid departmentId, UpdateDepartmentDto dto, CancellationToken ct = default) =>
            Task.FromResult(Detail);

        public Task<DepartmentDetailDto> ChangeStatusAsync(Guid departmentId, DepartmentStatusDto dto, CancellationToken ct = default) =>
            Task.FromResult(Detail);

        public Task DeleteDepartmentAsync(Guid departmentId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

