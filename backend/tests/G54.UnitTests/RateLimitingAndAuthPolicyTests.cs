using System.Reflection;
using System.Threading.RateLimiting;
using G54.Api.Controllers;
using G54.Api.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace G54.UnitTests;

public sealed class RateLimitingAndAuthPolicyTests
{
    [Fact]
    public void LoginEndpoint_HasAllowAnonymous_And_EnableRateLimiting()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Login));
        Assert.NotNull(method);

        var allowAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(allowAnonymous);

        var rateLimiting = method.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(rateLimiting);
        Assert.Equal(AuthController.LoginRateLimitPolicy, rateLimiting.PolicyName);
    }

    [Fact]
    public void RefreshEndpoint_HasAllowAnonymous()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Refresh));
        Assert.NotNull(method);

        var allowAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(allowAnonymous);
    }

    [Fact]
    public void LogoutEndpoint_DoesNotHaveAllowAnonymous()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Logout));
        Assert.NotNull(method);

        var allowAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.Null(allowAnonymous);
    }

    [Fact]
    public async Task FixedWindowRateLimiter_EnforcesLimitPerPartitionKey()
    {
        var options = new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
        };

        using var limiter = new FixedWindowRateLimiter(options);

        // First 3 requests should be accepted
        for (var i = 0; i < 3; i++)
        {
            using var lease = await limiter.AcquireAsync(1);
            Assert.True(lease.IsAcquired, $"Request {i + 1} should be acquired.");
        }

        // 4th request must be rejected
        using var rejectedLease = await limiter.AcquireAsync(1);
        Assert.False(rejectedLease.IsAcquired, "Request beyond permit limit should be rejected.");
    }

    [Fact]
    public void AuthorizeCheckOperationFilter_RemovesSecurity_ForAllowAnonymous()
    {
        var filter = new AuthorizeCheckOperationFilter();
        var operation = new OpenApiOperation
        {
            Security = new List<OpenApiSecurityRequirement>
            {
                new()
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer",
                        },
                    }] = Array.Empty<string>(),
                },
            },
        };

        var loginMethod = typeof(AuthController).GetMethod(nameof(AuthController.Login))!;
        var context = new OperationFilterContext(
            new ApiDescriptionGroup("auth", [new ApiDescription()]).Items[0],
            new SchemaGenerator(new SchemaGeneratorOptions(), new JsonSerializerDataContractResolver(new())),
            new SchemaRepository(),
            loginMethod);

        filter.Apply(operation, context);

        Assert.True(operation.Security == null || operation.Security.Count == 0);
    }

    [Fact]
    public void AuthorizeCheckOperationFilter_AddsBearerSecurity_WhenNoAllowAnonymous()
    {
        var filter = new AuthorizeCheckOperationFilter();
        var operation = new OpenApiOperation();

        var logoutMethod = typeof(AuthController).GetMethod(nameof(AuthController.Logout))!;
        var context = new OperationFilterContext(
            new ApiDescriptionGroup("auth", [new ApiDescription()]).Items[0],
            new SchemaGenerator(new SchemaGeneratorOptions(), new JsonSerializerDataContractResolver(new())),
            new SchemaRepository(),
            logoutMethod);

        filter.Apply(operation, context);

        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security);
        var requirement = operation.Security[0];
        Assert.Contains(requirement.Keys, k => k.Reference.Id == "Bearer");
    }
}
