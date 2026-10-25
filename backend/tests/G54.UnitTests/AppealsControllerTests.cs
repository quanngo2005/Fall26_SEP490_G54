using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Appeal;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class AppealsControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Controller_RequiresAuthenticationAndExposesTheDocumentedRoutes()
    {
        var type = typeof(AppealsController);

        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(string.Empty, type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal(
            "submissions/{submissionId:guid}/appeals",
            type.GetMethod(nameof(AppealsController.CreateAppeal))?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal(
            "appeals/{id:guid}/withdraw",
            type.GetMethod(nameof(AppealsController.WithdrawAppeal))?.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal(
            "appeals",
            type.GetMethod(nameof(AppealsController.GetAppeals))?.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal(
            "appeals/{id:guid}",
            type.GetMethod(nameof(AppealsController.GetAppealById))?.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    [Fact]
    public void CreateAppeal_AcceptsMultipartForm()
    {
        var consumes = typeof(AppealsController).GetMethod(nameof(AppealsController.CreateAppeal))!
            .GetCustomAttribute<ConsumesAttribute>();

        Assert.NotNull(consumes);
        Assert.Contains("multipart/form-data", consumes.ContentTypes);
    }

    [Fact]
    public async Task CreateAppeal_Returns201WithLocationAndTheCanonicalEnvelope()
    {
        var userId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);

        var result = await controller.CreateAppeal(
            submissionId,
            new CreateAppealRequestDto { Reason = "lý do" },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(AppealsController.GetAppealById), created.ActionName);
        Assert.Equal(service.Detail.Id, created.RouteValues!["id"]);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(submissionId, service.SubmissionId);
        Assert.Equal("lý do", service.Request!.Reason);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(created.Value, JsonOptions));
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("trace-1", root.GetProperty("traceId").GetString());
        Assert.Equal("Tạo mới thành công.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal("OPEN", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("decidedAt").ValueKind);
        Assert.Equal(JsonValueKind.Array, data.GetProperty("evidences").ValueKind);
    }

    [Fact]
    public async Task WithdrawAppeal_UsesTheAuthenticatedUserAndReturns200()
    {
        var userId = Guid.NewGuid();
        var appealId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);

        var result = await controller.WithdrawAppeal(appealId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(appealId, service.AppealId);
    }

    [Fact]
    public async Task GetAppeals_PassesTheFilterAndReturnsPagedData()
    {
        var userId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);

        var result = await controller.GetAppeals(new AppealFilterDto { Status = "OPEN", Page = 2, PageSize = 5 }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(userId, service.UserId);
        Assert.Equal("OPEN", service.Filter!.Status);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, JsonOptions));
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(5, data.GetProperty("pageSize").GetInt32());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task GetAppealById_UsesTheAuthenticatedUser()
    {
        var userId = Guid.NewGuid();
        var appealId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);

        var result = await controller.GetAppealById(appealId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(appealId, service.AppealId);
    }

    [Fact]
    public async Task AnyAction_WithoutIdentity_NeverReachesTheService()
    {
        var service = new RecordingService();
        var controller = new AppealsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.CreateAppeal(Guid.NewGuid(), new CreateAppealRequestDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.WithdrawAppeal(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetAppeals(new AppealFilterDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.GetAppealById(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(0, service.Calls);
    }

    private static AppealsController ControllerFor(IAppealService service, Guid userId) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth")),
            },
        },
    };

    private sealed class RecordingService : IAppealService
    {
        public int Calls { get; private set; }
        public Guid UserId { get; private set; }
        public Guid SubmissionId { get; private set; }
        public Guid AppealId { get; private set; }
        public CreateAppealRequestDto? Request { get; private set; }
        public AppealFilterDto? Filter { get; private set; }

        public AppealDetailDto Detail { get; } = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "W-1",
            "Work",
            1,
            Guid.NewGuid(),
            "NV001",
            "Employee",
            "OPEN",
            DateTimeOffset.UtcNow,
            null,
            DateTimeOffset.UtcNow,
            "lý do",
            null,
            null,
            null,
            []);

        public Task<AppealDetailDto> CreateAppealAsync(
            Guid submissionId,
            Guid userId,
            CreateAppealRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            SubmissionId = submissionId;
            UserId = userId;
            Request = request;
            return Task.FromResult(Detail);
        }

        public Task<AppealDetailDto> WithdrawAppealAsync(Guid appealId, Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            AppealId = appealId;
            UserId = userId;
            return Task.FromResult(Detail);
        }

        public Task<PagedResult<AppealListItemDto>> GetAppealsAsync(
            Guid userId,
            AppealFilterDto filter,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            UserId = userId;
            Filter = filter;
            return Task.FromResult(new PagedResult<AppealListItemDto>([], 0, filter.Page, filter.PageSize));
        }

        public Task<AppealDetailDto> GetAppealByIdAsync(Guid appealId, Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            AppealId = appealId;
            UserId = userId;
            return Task.FromResult(Detail);
        }
    }
}
