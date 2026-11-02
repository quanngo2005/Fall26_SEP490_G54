using System.Text.Json;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.AuditLog;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class AuditLogsControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetAuditLogs_ReturnsPagedResultWithTraceId()
    {
        var service = new RecordingService();
        var controller = ControllerFor(service);
        var query = new AuditLogQueryDto { Page = 2, PageSize = 10 };

        var result = await controller.GetAuditLogs(query, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(query, service.Query);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(200, root.GetProperty("status").GetInt32());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(10, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task GetAuditLog_ReturnsDetailWithTraceId()
    {
        var service = new RecordingService();
        var controller = ControllerFor(service);
        var id = Guid.NewGuid();

        var result = await controller.GetAuditLog(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, service.RequestedId);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
        Assert.Equal(service.Detail.Id, root.GetProperty("data").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetAuditLog_WhenServiceThrowsNotFound_PropagatesTheException()
    {
        var service = new RecordingService { ThrowNotFound = true };
        var controller = ControllerFor(service);

        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetAuditLog(Guid.NewGuid(), CancellationToken.None));
    }

    private static AuditLogsController ControllerFor(IAuditLogService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { TraceIdentifier = "trace-1" },
        },
    };

    private sealed class RecordingService : IAuditLogService
    {
        public AuditLogQueryDto? Query { get; private set; }
        public Guid RequestedId { get; private set; }
        public bool ThrowNotFound { get; set; }

        public AuditLogDetailDto Detail { get; } = new(
            Guid.NewGuid(),
            "RiskEvent",
            Guid.NewGuid().ToString(),
            "Create",
            Guid.NewGuid(),
            "admin",
            "127.0.0.1",
            "curl/8.0",
            "POST",
            "/risk-events",
            "trace-x",
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        public Task<PagedResult<AuditLogListItemDto>> GetPagedAsync(AuditLogQueryDto query, CancellationToken ct = default)
        {
            Query = query;
            return Task.FromResult(new PagedResult<AuditLogListItemDto>([], 0, query.Page, query.PageSize));
        }

        public Task<AuditLogDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            RequestedId = id;
            if (ThrowNotFound)
            {
                throw new NotFoundException("Nhật ký audit", id);
            }

            return Task.FromResult(Detail);
        }
    }
}
