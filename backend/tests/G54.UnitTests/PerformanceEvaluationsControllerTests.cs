using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Performance;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class PerformanceEvaluationsControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Controller_UsesCanonicalRouteAndPermission()
    {
        var type = typeof(PerformanceEvaluationsController);

        Assert.Equal("performance-evaluations", type.GetCustomAttribute<RouteAttribute>()?.Template);
        var permission = Assert.Single(type.GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal("evaluation", permission.Resource);
        Assert.Equal("calculate", permission.Action);
        Assert.Equal("Dept", permission.Scope);

        var action = type.GetMethod(nameof(PerformanceEvaluationsController.Calculate));
        Assert.Equal("{id:guid}/calculate", action?.GetCustomAttribute<HttpPostAttribute>()?.Template);
    }

    [Fact]
    public async Task Calculate_UsesAuthenticatedSubjectAndCanonicalEnvelope()
    {
        var accountId = Guid.NewGuid();
        var evaluationId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = new PerformanceEvaluationsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-1",
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", accountId.ToString())], "test")),
                },
            },
        };

        var result = await controller.Calculate(evaluationId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(accountId, service.AccountId);
        Assert.Equal(evaluationId, service.EvaluationId);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(200, root.GetProperty("status").GetInt32());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(evaluationId, data.GetProperty("id").GetGuid());
        Assert.Equal("DRAFT", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("totalScore").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("groups").ValueKind);
    }

    [Fact]
    public async Task Calculate_WithoutIdentity_NeverReachesTheService()
    {
        var service = new RecordingService();
        var controller = new PerformanceEvaluationsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => controller.Calculate(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(Guid.Empty, service.AccountId);
    }

    private sealed class RecordingService : IPerformanceEvaluationService
    {
        public Guid AccountId { get; private set; }
        public Guid EvaluationId { get; private set; }

        public Task<PerformanceEvaluationDto> CalculateAsync(
            Guid accountId,
            Guid evaluationId,
            CancellationToken cancellationToken = default)
        {
            AccountId = accountId;
            EvaluationId = evaluationId;
            return Task.FromResult(new PerformanceEvaluationDto(
                evaluationId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                "DRAFT",
                null,
                DateTimeOffset.UtcNow,
                []));
        }
    }
}
