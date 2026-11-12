using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAllocation;
using G54.BLL.Dtos.TargetImport;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class TargetAssignmentControllerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static FormFile MakeXlsxFile(string fileName = "import.xlsx", byte[]? content = null)
    {
        var bytes = content ?? [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0];
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName);
    }

    private static TargetAssignmentController CreateController(
        ITargetAllocationService? allocService = null,
        ITargetImportService? importService = null,
        ITargetSetupService? setupService = null)
    {
        return new TargetAssignmentController(
            allocService ?? new StubTargetAllocationService(),
            importService ?? new StubTargetImportService(),
            setupService ?? new StubTargetSetupService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-ta-1",
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AccountId.ToString())], "test")),
                },
            },
        };
    }

    [Fact]
    public void GetImportTemplate_ReturnsFileContentResultWithTemplateBytes()
    {
        var importService = new StubTargetImportService { TemplateBytes = [1, 2, 3] };
        var controller = CreateController(importService: importService);

        var result = controller.GetImportTemplate(2026);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
        Assert.Equal("KPI-Nam-Quy-2026-mau.xlsx", fileResult.FileDownloadName);
        Assert.Equal([1, 2, 3], fileResult.FileContents);
    }

    [Fact]
    public async Task PreviewImport_WhenFileIsNull_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.PreviewImport(Guid.NewGuid(), 2026, null, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ValidationError, envelope.ErrorCode);
    }

    [Fact]
    public async Task PreviewImport_WhenFileHasInvalidExtension_ReturnsBadRequest()
    {
        var controller = CreateController();
        var file = MakeXlsxFile("test.txt");

        var result = await controller.PreviewImport(Guid.NewGuid(), 2026, file, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ValidationError, envelope.ErrorCode);
    }

    [Fact]
    public async Task PreviewImport_WhenFileHasInvalidSignature_ReturnsBadRequest()
    {
        var controller = CreateController();
        var file = MakeXlsxFile("test.xlsx", [0x00, 0x00, 0x00, 0x00]);

        var result = await controller.PreviewImport(Guid.NewGuid(), 2026, file, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ValidationError, envelope.ErrorCode);
    }

    [Fact]
    public async Task PreviewImport_WhenFileIsValid_ReturnsOkWithPreviewDto()
    {
        var branchId = Guid.NewGuid();
        var preview = new TargetImportPreviewDto(
            "import.xlsx", branchId, 2026,
            new TargetImportSummaryDto(10, 10, 0, 10, 0, 0),
            new TargetImportQuarterRatiosDto(25, 25, 25, 25),
            [],
            []);
        var importService = new StubTargetImportService { PreviewResult = preview };
        var controller = CreateController(importService: importService);

        var result = await controller.PreviewImport(branchId, 2026, MakeXlsxFile(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<TargetImportPreviewDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(preview, envelope.Data);
    }

    [Fact]
    public async Task CommitImport_WhenFileIsInvalid_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.CommitImport(Guid.NewGuid(), 2026, null, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ValidationError, envelope.ErrorCode);
    }

    [Fact]
    public async Task CommitImport_WhenFileIsValid_Returns201CreatedWithCommitResult()
    {
        var branchId = Guid.NewGuid();
        var commitResult = new TargetImportCommitResultDto(branchId, 2026, [], 10, 0, false);
        var importService = new StubTargetImportService { CommitResult = commitResult };
        var controller = CreateController(importService: importService);

        var result = await controller.CommitImport(branchId, 2026, MakeXlsxFile(), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, status.StatusCode);
        var envelope = Assert.IsType<ApiResponse<TargetImportCommitResultDto>>(status.Value);
        Assert.True(envelope.Success);
        Assert.Same(commitResult, envelope.Data);
    }

    [Fact]
    public async Task GetSetup_ReturnsOkWithSetupDto()
    {
        var branchId = Guid.NewGuid();
        var setup = new TargetSetupDto(
            Guid.NewGuid(), true, DateTimeOffset.UtcNow, 5, "QUARTERS", "IN_PROGRESS",
            new TargetSetupQuartersMetadataDto(4, 4),
            new TargetSetupAllocationMetadataDto(2, 5),
            false, []);
        var setupService = new StubTargetSetupService { SetupResult = setup };
        var controller = CreateController(setupService: setupService);

        var result = await controller.GetSetup(branchId, 2026, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<TargetSetupDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(setup, envelope.Data);
    }

    [Fact]
    public async Task GetQuarters_ReturnsOkWithQuarterSplitDto()
    {
        var branchYearId = Guid.NewGuid();
        var quarters = new QuarterSplitDto(
            branchYearId, 2026, "DRAFT", null, null, [], []);
        var setupService = new StubTargetSetupService { QuartersResult = quarters };
        var controller = CreateController(setupService: setupService);

        var result = await controller.GetQuarters(branchYearId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QuarterSplitDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(quarters, envelope.Data);
    }

    [Fact]
    public async Task SaveQuarters_ReturnsOkWithUpdatedQuarterSplit()
    {
        var branchYearId = Guid.NewGuid();
        var quarters = new QuarterSplitDto(
            branchYearId, 2026, "DRAFT", null, null, [], []);
        var setupService = new StubTargetSetupService { QuartersResult = quarters };
        var controller = CreateController(setupService: setupService);
        var request = new SaveQuarterSplitRequest([]);

        var result = await controller.SaveQuarters(branchYearId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<QuarterSplitDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(quarters, envelope.Data);
    }

    [Fact]
    public async Task GetAllocation_ReturnsOkWithKpiAllocationDto()
    {
        var branchYearId = Guid.NewGuid();
        var kpiId = Guid.NewGuid();
        var alloc = new KpiAllocationDto(
            kpiId, "KPI 1", "VND", "WORK",
            new BranchTargetValuesDto(100, 25, 25, 25, 25),
            [],
            new AllocationTotalsDto(100, 25, 25, 25, 25),
            new AllocationRemainingDto(0, 0, 0, 0, 0),
            []);
        var setupService = new StubTargetSetupService { AllocationResult = alloc };
        var controller = CreateController(setupService: setupService);

        var result = await controller.GetAllocation(branchYearId, kpiId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<KpiAllocationDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(alloc, envelope.Data);
    }

    [Fact]
    public async Task SaveAllocation_ReturnsOkWithSavedKpiAllocation()
    {
        var branchYearId = Guid.NewGuid();
        var kpiId = Guid.NewGuid();
        var alloc = new KpiAllocationDto(
            kpiId, "KPI 1", "VND", "WORK",
            new BranchTargetValuesDto(100, 25, 25, 25, 25),
            [],
            new AllocationTotalsDto(100, 25, 25, 25, 25),
            new AllocationRemainingDto(0, 0, 0, 0, 0),
            []);
        var setupService = new StubTargetSetupService { AllocationResult = alloc };
        var controller = CreateController(setupService: setupService);
        var request = new SaveKpiAllocationRequest([]);

        var result = await controller.SaveAllocation(branchYearId, kpiId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<KpiAllocationDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(alloc, envelope.Data);
    }

    [Fact]
    public async Task Publish_ReturnsOkWithPublishResult()
    {
        var branchYearId = Guid.NewGuid();
        var publishResult = new PublishResultDto(DateTimeOffset.UtcNow, 1, 4, 3, 12);
        var setupService = new StubTargetSetupService { PublishResult = publishResult };
        var controller = CreateController(setupService: setupService);

        var result = await controller.Publish(branchYearId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PublishResultDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(publishResult, envelope.Data);
    }

    [Fact]
    public async Task AllocateDepartments_ReturnsOkWithTargetNodes()
    {
        var branchYearId = Guid.NewGuid();
        var nodes = new List<TargetNodeDto>();
        var allocService = new StubTargetAllocationService { DepartmentNodes = nodes };
        var controller = CreateController(allocService: allocService);
        var dto = new AllocateDepartmentsDto([], null);

        var result = await controller.AllocateDepartments(branchYearId, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<List<TargetNodeDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(nodes, envelope.Data);
    }

    [Fact]
    public async Task AllocateQuarters_ReturnsOkWithTargetNodes()
    {
        var deptYearId = Guid.NewGuid();
        var nodes = new List<TargetNodeDto>();
        var allocService = new StubTargetAllocationService { QuarterNodes = nodes };
        var controller = CreateController(allocService: allocService);
        var dto = new AllocateQuartersDto([], null);

        var result = await controller.AllocateQuarters(deptYearId, dto, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<List<TargetNodeDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(nodes, envelope.Data);
    }

    [Fact]
    public async Task Confirm_ReturnsOkWithTargetNode()
    {
        var deptYearId = Guid.NewGuid();
        var node = new TargetNodeDto(
            Guid.NewGuid(), null, Guid.NewGuid(), "KPI", Guid.NewGuid(), "DEPT",
            Guid.NewGuid(), "P-2026", "YEAR", 100, 100, null, 1, "CONFIRMED", null, null, []);
        var allocService = new StubTargetAllocationService { ConfirmedNode = node };
        var controller = CreateController(allocService: allocService);

        var result = await controller.Confirm(deptYearId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<TargetNodeDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(node, envelope.Data);
    }

    [Fact]
    public async Task GetTree_ReturnsOkWithTargetNodes()
    {
        var branchId = Guid.NewGuid();
        var tree = new List<TargetNodeDto>();
        var allocService = new StubTargetAllocationService { TreeNodes = tree };
        var controller = CreateController(allocService: allocService);

        var result = await controller.GetTree(branchId, 2026, 2025, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<List<TargetNodeDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Same(tree, envelope.Data);
    }

    private sealed class StubTargetImportService : ITargetImportService
    {
        public byte[] TemplateBytes { get; set; } = [0x50, 0x4B, 0x03, 0x04];
        public TargetImportPreviewDto PreviewResult { get; set; } = null!;
        public TargetImportCommitResultDto CommitResult { get; set; } = null!;

        public byte[] BuildTemplate(int year) => TemplateBytes;

        public Task<TargetImportPreviewDto> PreviewAsync(
            Guid branchId, int year, string fileName, Stream file, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(PreviewResult);

        public Task<TargetImportCommitResultDto> CommitAsync(
            Guid branchId, int year, string fileName, Stream file, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(CommitResult);
    }

    private sealed class StubTargetSetupService : ITargetSetupService
    {
        public QuarterSplitDto QuartersResult { get; set; } = null!;
        public KpiAllocationDto AllocationResult { get; set; } = null!;
        public TargetSetupDto SetupResult { get; set; } = null!;
        public PublishResultDto PublishResult { get; set; } = null!;

        public Task<QuarterSplitDto> GetQuartersAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(QuartersResult);

        public Task<QuarterSplitDto> SaveQuartersAsync(
            Guid branchYearId, SaveQuarterSplitRequest request, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(QuartersResult);

        public Task<KpiAllocationDto> GetAllocationAsync(Guid branchYearId, Guid kpiId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(AllocationResult);

        public Task<KpiAllocationDto> SaveAllocationAsync(
            Guid branchYearId, Guid kpiId, SaveKpiAllocationRequest request, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(AllocationResult);

        public Task<TargetSetupDto> GetSetupAsync(Guid branchId, int year, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(SetupResult);

        public Task<PublishResultDto> PublishAsync(Guid branchYearId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(PublishResult);
    }

    private sealed class StubTargetAllocationService : ITargetAllocationService
    {
        public List<TargetNodeDto> DepartmentNodes { get; set; } = [];
        public List<TargetNodeDto> QuarterNodes { get; set; } = [];
        public TargetNodeDto ConfirmedNode { get; set; } = null!;
        public List<TargetNodeDto> TreeNodes { get; set; } = [];

        public Task<List<TargetNodeDto>> AllocateDepartmentsAsync(
            Guid branchYearId, AllocateDepartmentsDto dto, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(DepartmentNodes);

        public Task<List<TargetNodeDto>> AllocateQuartersAsync(
            Guid deptYearId, AllocateQuartersDto dto, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(QuarterNodes);

        public Task<TargetNodeDto> ConfirmAsync(Guid deptYearId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(ConfirmedNode);

        public Task<List<TargetNodeDto>> GetTreeAsync(
            Guid branchId, int year, int? compareYear, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(TreeNodes);
    }
}

