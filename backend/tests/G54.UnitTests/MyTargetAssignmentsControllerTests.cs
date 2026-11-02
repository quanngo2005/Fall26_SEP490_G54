using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.TargetAssignment;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class MyTargetAssignmentsControllerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static MyTargetAssignmentsController CreateController(StubService service) => new(service)
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

    [Fact]
    public void Controller_RequiresAuthenticationOnlyNotAResourcePermission()
    {
        Assert.NotEmpty(typeof(MyTargetAssignmentsController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.Empty(typeof(MyTargetAssignmentsController).GetCustomAttributes(typeof(G54.Api.Authorization.HasPermissionAttribute), true));
    }

    [Fact]
    public async Task GetMine_Returns200WithTheCallersTargets()
    {
        var sample = new TargetAssignmentDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null, null, 1, 100m, "ACTIVE",
            null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "NOT_VIEWED");
        var service = new StubService { Mine = [sample] };

        var result = await CreateController(service).GetMine(new MyTargetAssignmentQueryDto());

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<IReadOnlyList<TargetAssignmentDto>>>(ok.Value);
        Assert.Equal(200, envelope.Status);
        Assert.Single(envelope.Data!);
        Assert.Equal(AccountId, service.CallerId);
    }

    [Fact]
    public async Task Confirm_Returns201()
    {
        var feedback = new TargetAssignmentFeedbackDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "CONFIRM", null, DateTimeOffset.UnixEpoch);
        var service = new StubService { Feedback = feedback };

        var result = await CreateController(service).Confirm(Guid.NewGuid());

        // Both the HTTP status and the envelope status must be 201.
        var http = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, http.StatusCode);
        var created = Assert.IsType<ApiResponse<TargetAssignmentFeedbackDto>>(http.Value);
        Assert.Equal(201, created.Status);
    }

    private sealed class StubService : ITargetAssignmentService
    {
        public Guid CallerId { get; private set; }

        public IReadOnlyList<TargetAssignmentDto> Mine { get; set; } = [];

        public TargetAssignmentFeedbackDto Feedback { get; set; } = null!;

        public Task<TargetAssignmentDto> CreateRootAsync(Guid accountId, CreateTargetAssignmentDto dto) => throw new NotImplementedException();

        public Task<IReadOnlyList<TargetAssignmentDto>> AllocateAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetDto dto) => throw new NotImplementedException();

        public Task<IReadOnlyList<TargetAssignmentDto>> AllocateByPeriodAsync(Guid accountId, Guid parentAssignmentId, AllocateTargetByPeriodDto dto) => throw new NotImplementedException();

        public Task<IReadOnlyList<TargetAssignmentDto>> ListAsync(Guid accountId, TargetAssignmentQueryDto query) => throw new NotImplementedException();

        public Task<TargetAssignmentDto> GetAsync(Guid accountId, Guid assignmentId) => throw new NotImplementedException();

        public Task<TargetAssignmentBreakdownDto> GetBreakdownAsync(Guid accountId, Guid assignmentId, string? granularity) => throw new NotImplementedException();

        public Task<IReadOnlyList<TargetAssignmentDto>> GetMineAsync(Guid accountId, MyTargetAssignmentQueryDto query)
        {
            CallerId = accountId;
            return Task.FromResult(Mine);
        }

        public Task<TargetAssignmentFeedbackDto> ConfirmAsync(Guid accountId, Guid assignmentId)
        {
            CallerId = accountId;
            return Task.FromResult(Feedback);
        }

        public Task<TargetAssignmentFeedbackDto> CommentAsync(Guid accountId, Guid assignmentId, CreateTargetFeedbackCommentDto dto) => throw new NotImplementedException();

        public Task<IReadOnlyList<TargetAssignmentFeedbackDto>> GetFeedbackAsync(Guid accountId, Guid assignmentId) => throw new NotImplementedException();
    }
}
