using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.SystemParameters;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class SystemParametersControllerTests
{
    private sealed class FakeSystemParameterService(IReadOnlyList<SystemParameterDto>? getAllResult = null) : ISystemParameterService
    {
        public Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(Guid accountId, CancellationToken ct)
            => Task.FromResult(getAllResult ?? new List<SystemParameterDto>());

        public Task<IReadOnlyList<SystemParameterDto>> SaveAsync(Guid accountId, SaveSystemParametersDto request, CancellationToken ct)
            => Task.FromResult(getAllResult ?? new List<SystemParameterDto>());
    }

    [Fact]
    public async Task GetAll_WithoutClaims_ReturnsUnauthorized()
    {
        var service = new FakeSystemParameterService();
        var controller = new SystemParametersController(service);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            controller.GetAll(CancellationToken.None));
    }

    [Fact]
    public async Task GetAll_WithValidUser_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var dtos = new List<SystemParameterDto>
        {
            new("appealWindowDays", 5L, 1, 30, false, null, null)
        }.AsReadOnly();

        var service = new FakeSystemParameterService(dtos);
        var controller = new SystemParametersController(service);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetAll(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<SystemParameterDto>>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(1, response.Data?.Count);
    }

    [Fact]
    public async Task Save_WithValidRequest_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var request = new SaveSystemParametersDto
        {
            Changes = new List<SystemParameterChangeDto>
            {
                new() { Key = "appealWindowDays", Value = 7 }
            }
        };
        var dtos = new List<SystemParameterDto>
        {
            new("appealWindowDays", 7L, 1, 30, false, DateTimeOffset.UtcNow, "admin")
        }.AsReadOnly();

        var service = new FakeSystemParameterService(dtos);
        var controller = new SystemParametersController(service);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Save(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<SystemParameterDto>>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(1, response.Data?.Count);
    }
}
