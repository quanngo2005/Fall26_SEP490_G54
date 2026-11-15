using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TransactionImport;
using G54.BLL.Dtos.WorkTransaction;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

public sealed class TransactionImportsControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static FormFile MakeXlsxFile(string fileName = "transactions.xlsx", int length = 8)
    {
        var bytes = new byte[length];
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, length, "file", fileName);
    }

    private static TransactionImportsController CreateController(
        StubService service,
        StubWorkTransactionService? workService = null,
        StubTemplateProvider? templateProvider = null,
        SystemParametersOptions? options = null) => new(
        service,
        workService ?? new StubWorkTransactionService(),
        Options.Create(options ?? new SystemParametersOptions()),
        templateProvider ?? new StubTemplateProvider())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-1",
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "test")),
                },
            },
        };

    [Fact]
    public void GetTemplate_ReturnsFileContentResultWithTemplate()
    {
        var templateProvider = new StubTemplateProvider { TemplateBytes = [1, 2, 3] };
        var controller = CreateController(new StubService(), templateProvider: templateProvider);

        var result = controller.GetTemplate();

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
        Assert.Equal("transaction-import-template.xlsx", fileResult.FileDownloadName);
        Assert.Equal([1, 2, 3], fileResult.FileContents);
    }

    [Fact]
    public async Task Import_WhenFileIsNull_ReturnsBadRequest()
    {
        var controller = CreateController(new StubService());

        var result = await controller.Import(null, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ImportInvalidFile, envelope.ErrorCode);
    }

    [Fact]
    public async Task Import_WhenFileIsEmpty_ReturnsBadRequest()
    {
        var controller = CreateController(new StubService());
        var emptyFile = MakeXlsxFile(length: 0);

        var result = await controller.Import(emptyFile, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ImportInvalidFile, envelope.ErrorCode);
    }

    [Fact]
    public async Task Import_WhenFileIsNotXlsx_ReturnsBadRequest()
    {
        var controller = CreateController(new StubService());
        var csvFile = MakeXlsxFile("test.csv", length: 10);

        var result = await controller.Import(csvFile, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ImportInvalidFile, envelope.ErrorCode);
    }

    [Fact]
    public async Task Import_WhenFileExceedsMaxMb_ReturnsBadRequest()
    {
        var options = new SystemParametersOptions { ImportMaxFileMb = 1 };
        var controller = CreateController(new StubService(), options: options);
        var largeFile = MakeXlsxFile("large.xlsx", length: 2 * 1024 * 1024);

        var result = await controller.Import(largeFile, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse>(bad.Value);
        Assert.False(envelope.Success);
        Assert.Equal(ErrorCodes.ImportInvalidFile, envelope.ErrorCode);
    }

    [Fact]
    public async Task Import_Returns201WithAnEnvelopeThatAgreesWithTheHttpStatus()
    {
        var batch = new ImportBatchDto(
            Guid.NewGuid(), "tx.xlsx", "COMPLETED", 10, 9, 1, 0, null, DateTimeOffset.UnixEpoch, Guid.NewGuid());
        var service = new StubService { Imported = batch };
        var file = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "tx.xlsx");

        var controller = CreateController(service);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test"));

        var result = await controller.Import(file, CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var envelope = Assert.IsType<ApiResponse<ImportBatchDto>>(created.Value);
        Assert.Equal(201, envelope.Status);
        Assert.True(envelope.Success);
        Assert.Equal(1, envelope.Data!.Reconcile!.Matched);
    }

    [Fact]
    public async Task Import_WhenFileIsValid_ReturnsCreated201WithReconcile()
    {
        var batchId = Guid.NewGuid();
        var batch = new ImportBatchDto(
            batchId, "transactions.xlsx", "COMPLETED", 10, 10, 0, 0, null, DateTimeOffset.UtcNow, UserId);
        var service = new StubService { BatchToImport = batch };
        var workService = new StubWorkTransactionService { ReconcileSummary = new ReconcileSummaryDto(8, 2, 0) };
        var controller = CreateController(service, workService: workService);

        var file = MakeXlsxFile("transactions.xlsx", length: 100);
        var result = await controller.Import(file, CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal($"/transaction-imports/{batchId}", created.Location);
        var envelope = Assert.IsType<ApiResponse<ImportBatchDto>>(created.Value);
        Assert.True(envelope.Success);
        Assert.Equal(batchId, envelope.Data!.Id);
        Assert.NotNull(envelope.Data.Reconcile);
        Assert.Equal(8, envelope.Data.Reconcile.Matched);
    }

    [Fact]
    public async Task List_Returns200WithPagedBatchesAndPassesPagingToTheService()
    {
        var batch = new ImportBatchDto(
            Guid.NewGuid(), "tx.xlsx", "COMPLETED", 10, 9, 1, 0, null, DateTimeOffset.UnixEpoch, Guid.NewGuid());
        var service = new StubService { Page = new PagedResult<ImportBatchDto>([batch], 1, 2, 50) };

        var result = await CreateController(service).List(2, 50, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<ImportBatchDto>>>(ok.Value);
        Assert.Equal(200, envelope.Status);
        Assert.Single(envelope.Data!.Items);
        Assert.Equal(2, service.RequestedPage);
        Assert.Equal(50, service.RequestedPageSize);
    }

    [Fact]
    public async Task Get_ReturnsOkWithBatch()
    {
        var batchId = Guid.NewGuid();
        var batch = new ImportBatchDto(
            batchId, "tx.xlsx", "COMPLETED", 10, 9, 1, 0, null, DateTimeOffset.UnixEpoch, Guid.NewGuid());
        var service = new StubService { BatchToGet = batch };

        var result = await CreateController(service).Get(batchId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<ImportBatchDto>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Equal(batchId, envelope.Data!.Id);
        Assert.Equal(batchId, service.RequestedBatchId);
    }

    [Fact]
    public async Task ListErrors_ReturnsOkWithPagedErrors()
    {
        var batchId = Guid.NewGuid();
        var error = new ImportRowErrorDto(2, "Amount", "INVALID_NUMBER", "Giá trị không hợp lệ");
        var service = new StubService { ErrorsPage = new PagedResult<ImportRowErrorDto>([error], 1, 1, 20) };

        var result = await CreateController(service).ListErrors(batchId, 1, 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<ImportRowErrorDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Single(envelope.Data!.Items);
        Assert.Equal(batchId, service.RequestedBatchId);
    }

    [Fact]
    public async Task ListTransactions_ReturnsOkWithPagedTransactions()
    {
        var batchId = Guid.NewGuid();
        var tx = new ImportedTransactionDto(
            Guid.NewGuid(), "TX-01", "EMP01", "PRD01",
            DateOnly.FromDateTime(DateTime.UtcNow), 1000m, null, null);
        var service = new StubService { TransactionsPage = new PagedResult<ImportedTransactionDto>([tx], 1, 1, 20) };

        var result = await CreateController(service).ListTransactions(batchId, "EMP01", "PRD01", 1, 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<ImportedTransactionDto>>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.Single(envelope.Data!.Items);
        Assert.Equal("EMP01", service.RequestedEmployeeCode);
        Assert.Equal("PRD01", service.RequestedProductCode);
    }

    private sealed class StubService : ITransactionImportService
    {
        public PagedResult<ImportBatchDto> Page { get; set; } = null!;
        public ImportBatchDto? BatchToImport { get; set; }
        public ImportBatchDto? Imported { get; set; }
        public ImportBatchDto BatchToGet { get; set; } = null!;
        public PagedResult<ImportRowErrorDto> ErrorsPage { get; set; } = null!;
        public PagedResult<ImportedTransactionDto> TransactionsPage { get; set; } = null!;

        public int RequestedPage { get; private set; }
        public int RequestedPageSize { get; private set; }
        public Guid RequestedBatchId { get; private set; }
        public string? RequestedEmployeeCode { get; private set; }
        public string? RequestedProductCode { get; private set; }

        public Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(BatchToImport ?? Imported ?? throw new NotImplementedException());

        public Task<PagedResult<ImportBatchDto>> ListBatchesAsync(int page, int pageSize, CancellationToken ct = default)
        {
            RequestedPage = page;
            RequestedPageSize = pageSize;
            return Task.FromResult(Page);
        }

        public Task<ImportBatchDto> GetBatchAsync(Guid id, CancellationToken ct = default)
        {
            RequestedBatchId = id;
            return Task.FromResult(BatchToGet);
        }

        public Task<PagedResult<ImportedTransactionDto>> ListTransactionsAsync(
            Guid batchId, string? employeeCode, string? productCode, int page, int pageSize, CancellationToken ct = default)
        {
            RequestedBatchId = batchId;
            RequestedEmployeeCode = employeeCode;
            RequestedProductCode = productCode;
            return Task.FromResult(TransactionsPage);
        }

        public Task<PagedResult<ImportRowErrorDto>> ListErrorsAsync(Guid batchId, int page, int pageSize, CancellationToken ct = default)
        {
            RequestedBatchId = batchId;
            return Task.FromResult(ErrorsPage);
        }
    }

    private sealed class StubWorkTransactionService : IWorkTransactionService
    {
        public ReconcileSummaryDto ReconcileSummary { get; set; } = new(1, 0, 0);

        public Task<WorkTransactionsDto> ListAsync(Guid workId, Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<WorkTransactionsDto> AddAsync(Guid workId, Guid userId, AddWorkTransactionsRequestDto request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<WorkTransactionsDto> RemoveAsync(Guid workId, Guid transactionId, Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ReconcileSummaryDto> ReconcileBatchAsync(Guid batchId, CancellationToken ct = default) =>
            Task.FromResult(ReconcileSummary);
    }

    private sealed class StubTemplateProvider : ITransactionImportTemplateProvider
    {
        public byte[] TemplateBytes { get; set; } = [1, 2, 3];
        public byte[] GetTemplate() => TemplateBytes;
    }
}
