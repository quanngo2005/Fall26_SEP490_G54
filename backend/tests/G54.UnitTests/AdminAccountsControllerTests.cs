using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Admin;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class AdminAccountsControllerTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();

    private static AdminAccountsController CreateController(StubAdminAccountService service)
    {
        var http = new DefaultHttpContext
        {
            TraceIdentifier = "trace-admin-acct",
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", ActorId.ToString())], "test")),
        };
        return new AdminAccountsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    [Fact]
    public async Task AllEndpointsDelegateToServiceAndReturnExpectedEnvelopes()
    {
        var stub = new StubAdminAccountService();
        var controller = CreateController(stub);

        var roles = Assert.IsType<OkObjectResult>((await controller.GetRoles(CancellationToken.None)).Result);
        Assert.True(Assert.IsType<ApiResponse<IReadOnlyList<RoleSummaryDto>>>(roles.Value).Success);

        var list = Assert.IsType<OkObjectResult>((await controller.GetAccounts(new UserAccountFilterDto(), CancellationToken.None)).Result);
        Assert.Equal(1, Assert.IsType<ApiResponse<PagedResult<UserAccountSummaryDto>>>(list.Value).Data!.TotalCount);

        var summary = Assert.IsType<OkObjectResult>((await controller.GetSummary(CancellationToken.None)).Result);
        Assert.Equal(2, Assert.IsType<ApiResponse<UserAccountSummaryCountsDto>>(summary.Value).Data!.ActiveCount);

        var eligible = Assert.IsType<OkObjectResult>((await controller.GetEligibleEmployees(CancellationToken.None)).Result);
        Assert.Single(Assert.IsType<ApiResponse<IReadOnlyList<EligibleEmployeeDto>>>(eligible.Value).Data!);

        var detail = Assert.IsType<OkObjectResult>((await controller.GetAccountDetail(TargetId, CancellationToken.None)).Result);
        Assert.Equal(TargetId, Assert.IsType<ApiResponse<UserAccountDetailDto>>(detail.Value).Data!.Id);

        var created = Assert.IsType<CreatedAtActionResult>((await controller.CreateAccount(
            new CreateUserAccountRequestDto(Guid.NewGuid(), "new.user", Guid.NewGuid()),
            CancellationToken.None)).Result);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ApiResponse<CreateUserAccountResultDto>>(created.Value).Status);

        var updated = Assert.IsType<OkObjectResult>((await controller.UpdateAccount(
            TargetId,
            new UpdateUserAccountRequestDto(Guid.NewGuid()),
            CancellationToken.None)).Result);
        Assert.Equal(TargetId, Assert.IsType<ApiResponse<UserAccountDetailDto>>(updated.Value).Data!.Id);

        var reset = Assert.IsType<OkObjectResult>((await controller.ResetPassword(TargetId, CancellationToken.None)).Result);
        Assert.Equal("Tmp#Pass12345678", Assert.IsType<ApiResponse<ResetUserPasswordResultDto>>(reset.Value).Data!.TemporaryPassword);

        var locked = Assert.IsType<OkObjectResult>((await controller.LockAccount(TargetId, CancellationToken.None)).Result);
        Assert.Equal(ActorId, stub.LastLockActorId);
        Assert.Equal("LOCKED", Assert.IsType<ApiResponse<UserAccountDetailDto>>(locked.Value).Data!.Status);

        var unlocked = Assert.IsType<OkObjectResult>((await controller.UnlockAccount(TargetId, CancellationToken.None)).Result);
        Assert.Equal("ACTIVE", Assert.IsType<ApiResponse<UserAccountDetailDto>>(unlocked.Value).Data!.Status);
    }

    private sealed class StubAdminAccountService : IAdminAccountService
    {
        public Guid LastLockActorId { get; private set; }

        private static UserAccountDetailDto SampleDetail(string status = "ACTIVE") => new(
            TargetId,
            "sample.user",
            "NV01",
            "Sample User",
            "D01",
            "Teller",
            Guid.NewGuid(),
            "Employee",
            status,
            Guid.NewGuid(),
            "Department 1",
            null,
            DateTimeOffset.UtcNow,
            "System",
            null,
            []);

        public Task<IReadOnlyList<RoleSummaryDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleSummaryDto>>([new RoleSummaryDto(Guid.NewGuid(), "ADMIN", "Admin", true, null, 1, "Active")]);

        public Task<PagedResult<UserAccountSummaryDto>> GetAccountsAsync(UserAccountFilterDto filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<UserAccountSummaryDto>(
                [new UserAccountSummaryDto(TargetId, "sample.user", "NV01", "Sample User", "D01", "Department 1", "Teller", Guid.NewGuid(), "Employee", "ACTIVE")],
                1,
                1,
                20));

        public Task<UserAccountSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserAccountSummaryCountsDto(2, 1, 1));

        public Task<IReadOnlyList<EligibleEmployeeDto>> GetEligibleEmployeesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EligibleEmployeeDto>>(
                [new EligibleEmployeeDto(Guid.NewGuid(), "NV02", "Eligible", "D01", "Department 1", "Teller", "e@x.local", "HRM Sync", null, "eligible")]);

        public Task<UserAccountDetailDto> GetAccountDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleDetail());

        public Task<CreateUserAccountResultDto> CreateAccountAsync(CreateUserAccountRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreateUserAccountResultDto(SampleDetail(), "Tmp#Pass12345678"));

        public Task<UserAccountDetailDto> UpdateAccountAsync(Guid id, UpdateUserAccountRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleDetail());

        public Task<ResetUserPasswordResultDto> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResetUserPasswordResultDto("Tmp#Pass12345678"));

        public Task<UserAccountDetailDto> LockAccountAsync(Guid actorAccountId, Guid id, CancellationToken cancellationToken = default)
        {
            LastLockActorId = actorAccountId;
            return Task.FromResult(SampleDetail("LOCKED"));
        }

        public Task<UserAccountDetailDto> UnlockAccountAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(SampleDetail("ACTIVE"));
    }
}
