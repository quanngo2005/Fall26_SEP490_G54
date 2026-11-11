using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common;
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
    private static TransactionImportsController CreateController(StubService service) => new(
        service, new StubWorkTransactionService(), Options.Create(new SystemParametersOptions()), new StubTemplateProvider())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-1" },
        },
    };

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

    private sealed class StubService : ITransactionImportService
    {
        public PagedResult<ImportBatchDto> Page { get; set; } = null!;

        public ImportBatchDto? Imported { get; set; }

        public int RequestedPage { get; private set; }

        public int RequestedPageSize { get; private set; }

        public Task<ImportBatchDto> ImportAsync(string fileName, Stream excel, Guid userId, CancellationToken ct = default) =>
            Imported is null ? throw new NotImplementedException() : Task.FromResult(Imported);

        public Task<PagedResult<ImportBatchDto>> ListBatchesAsync(int page, int pageSize, CancellationToken ct = default)
        {
            RequestedPage = page;
            RequestedPageSize = pageSize;
            return Task.FromResult(Page);
        }

        public Task<ImportBatchDto> GetBatchAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<PagedResult<ImportedTransactionDto>> ListTransactionsAsync(
            Guid batchId, string? employeeCode, string? productCode, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PagedResult<ImportRowErrorDto>> ListErrorsAsync(Guid batchId, int page, int pageSize, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubWorkTransactionService : IWorkTransactionService
    {
        public Task<WorkTransactionsDto> ListAsync(Guid workId, Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<WorkTransactionsDto> AddAsync(Guid workId, Guid userId, AddWorkTransactionsRequestDto request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<WorkTransactionsDto> RemoveAsync(Guid workId, Guid transactionId, Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ReconcileSummaryDto> ReconcileBatchAsync(Guid batchId, CancellationToken ct = default) =>
            Task.FromResult(new ReconcileSummaryDto(1, 0, 0));
    }

    private sealed class StubTemplateProvider : ITransactionImportTemplateProvider
    {
        public byte[] GetTemplate() => throw new NotImplementedException();
    }
}
