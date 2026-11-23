using System;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Dashboard;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace G54.UnitTests;

public sealed class MyDashboardControllerTests
{
    private static MyDashboardController CreateController(IMyDashboardService service, Guid? accountId = null)
    {
        var claims = accountId.HasValue ? new[] { new Claim("sub", accountId.Value.ToString()) } : [];
        return new MyDashboardController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-test-1",
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, accountId.HasValue ? "test" : null)),
                },
            },
        };
    }

    [Fact]
    public void Controller_UsesCanonicalRoute()
    {
        var route = typeof(MyDashboardController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("my-dashboard", route.Template);
    }

    [Fact]
    public void Controller_RequiresAuthorization()
    {
        var auth = typeof(MyDashboardController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(auth);
    }

    [Fact]
    public void GetProgressCard_ActionHasCorrectRoute()
    {
        var method = typeof(MyDashboardController).GetMethod(nameof(MyDashboardController.GetProgressCard));
        Assert.NotNull(method);
        var getAttr = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(getAttr);
        Assert.Equal("progress-card", getAttr.Template);
    }

    [Fact]
    public async Task GetProgressCard_ReturnsOkWithExpectedPayload()
    {
        var accountId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var expectedPeriod = new DashboardPeriodInfoDto(
            periodId, "2026-11", "Tháng 11/2026", "MONTH", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        var expectedDto = new MyProgressCardDto(
            Role: "TELLER",
            CardTitle: "Hiệu suất tháng 11",
            Period: expectedPeriod,
            CurrentRate: 82m,
            PacingStatus: "ON_PACE",
            PacingStatusLabel: "Đạt nhịp",
            ExpectedRateToday: 30m,
            ExpectedRateTodayText: "Kỳ vọng hôm nay 30%",
            PolicyNote: null,
            PreviousPeriodComparison: new PeriodComparisonDto("MoM", "UP", 6m, "T10", "▲ 6% so với T10"),
            WorkingDays: new ProgressWorkingDaysDto(21, 6),
            ActionUrl: "/my-targets");

        var service = new MockDashboardService
        {
            ExpectedResult = expectedDto
        };

        var controller = CreateController(service, accountId);
        var query = new MyProgressCardQueryDto("MONTH", periodId);

        var actionResult = await controller.GetProgressCard(query, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<G54.BLL.Common.Models.ApiResponse<MyProgressCardDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(200, response.Status);
        Assert.Equal(expectedDto, response.Data);
        Assert.Equal(accountId, service.LastAccountId);
        Assert.Equal(query, service.LastQuery);
    }

    [Fact]
    public async Task GetProgressCard_PropagatesValidationException()
    {
        var service = new MockDashboardService
        {
            ExceptionToThrow = new ValidationException("Kỳ theo ngày không hỗ trợ cho thẻ tiến độ tổng hợp.")
        };

        var controller = CreateController(service, Guid.NewGuid());
        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.GetProgressCard(new MyProgressCardQueryDto(), CancellationToken.None));
    }

    private sealed class MockDashboardService : IMyDashboardService
    {
        public MyProgressCardDto? ExpectedResult { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public Guid? LastAccountId { get; private set; }
        public MyProgressCardQueryDto? LastQuery { get; private set; }

        public Task<MyProgressCardDto> GetMyProgressCardAsync(
            Guid accountId,
            MyProgressCardQueryDto query,
            CancellationToken cancellationToken = default)
        {
            LastAccountId = accountId;
            LastQuery = query;

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ExpectedResult!);
        }
    }
}

