using System.Net;
using System.Security.Claims;
using G54.Api.Logging;
using G54.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace G54.UnitTests;

public sealed class SerilogLoggingTests
{
    private sealed class DelegatingSink(Action<LogEvent> write) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => write(logEvent);
    }

    private sealed class TestDiagnosticContext : IDiagnosticContext
    {
        public Dictionary<string, object> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string propertyName, object value, bool destructureObjects = false)
        {
            Properties[propertyName] = value;
        }

        public void SetException(Exception exception)
        {
        }
    }

    [Fact]
    public async Task TraceIdMiddleware_PreservesIncomingTraceId_AndPushesToLogContext()
    {
        var logs = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(logs.Add))
            .CreateLogger();

        var middleware = new TraceIdMiddleware(innerContext =>
        {
            Assert.Equal("custom-trace-123", innerContext.TraceIdentifier);
            logger.Information("Testing inside pipeline");
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers[TraceIdMiddleware.TraceIdHeader] = "custom-trace-123";

        await middleware.InvokeAsync(context);

        Assert.Single(logs);
        var traceIdProperty = Assert.Contains("TraceId", logs[0].Properties);
        var scalarValue = Assert.IsType<ScalarValue>(traceIdProperty);
        Assert.Equal("custom-trace-123", scalarValue.Value);
    }

    [Fact]
    public async Task TraceIdMiddleware_GeneratesTraceId_WhenHeaderMissing()
    {
        var logs = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(logs.Add))
            .CreateLogger();

        string? capturedTraceId = null;
        var middleware = new TraceIdMiddleware(innerContext =>
        {
            capturedTraceId = innerContext.TraceIdentifier;
            logger.Information("Testing generated trace id");
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.False(string.IsNullOrWhiteSpace(capturedTraceId));
        Assert.Single(logs);
        var traceIdProperty = Assert.Contains("TraceId", logs[0].Properties);
        var scalarValue = Assert.IsType<ScalarValue>(traceIdProperty);
        Assert.Equal(capturedTraceId, scalarValue.Value);
    }

    [Fact]
    public async Task UserLogContextMiddleware_PushesUserIdToLogContext_WhenUserAuthenticatedWithSub()
    {
        var logs = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(logs.Add))
            .CreateLogger();

        var middleware = new UserLogContextMiddleware(innerContext =>
        {
            logger.Information("Inside authenticated handler");
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        var claims = new[] { new Claim("sub", "user-uuid-111") };
        var identity = new ClaimsIdentity(claims, "Bearer");
        context.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(context);

        Assert.Single(logs);
        var userIdProperty = Assert.Contains("UserId", logs[0].Properties);
        var scalarValue = Assert.IsType<ScalarValue>(userIdProperty);
        Assert.Equal("user-uuid-111", scalarValue.Value);
    }

    [Fact]
    public async Task UserLogContextMiddleware_PushesUserIdFromClaimTypesNameIdentifier_WhenSubMissing()
    {
        var logs = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(logs.Add))
            .CreateLogger();

        var middleware = new UserLogContextMiddleware(innerContext =>
        {
            logger.Information("Inside authenticated handler with name identifier");
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "user-uuid-222") };
        var identity = new ClaimsIdentity(claims, "Bearer");
        context.User = new ClaimsPrincipal(identity);

        await middleware.InvokeAsync(context);

        Assert.Single(logs);
        var userIdProperty = Assert.Contains("UserId", logs[0].Properties);
        var scalarValue = Assert.IsType<ScalarValue>(userIdProperty);
        Assert.Equal("user-uuid-222", scalarValue.Value);
    }

    [Fact]
    public async Task UserLogContextMiddleware_PassesThrough_WhenAnonymous()
    {
        var logs = new List<LogEvent>();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(logs.Add))
            .CreateLogger();

        var middleware = new UserLogContextMiddleware(innerContext =>
        {
            logger.Information("Inside anonymous handler");
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Single(logs);
        Assert.DoesNotContain("UserId", logs[0].Properties);
    }

    [Fact]
    public void SerilogLoggingHelper_EnrichDiagnosticContext_SetsAllExpectedProperties()
    {
        var diagnosticContext = new TestDiagnosticContext();
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-diag-999",
        };
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        httpContext.Request.Headers.UserAgent = "Mozilla/5.0 UnitTests";
        httpContext.Request.Host = new HostString("api.g54.local:5001");
        httpContext.Request.Scheme = "https";

        var claims = new[] { new Claim("sub", "user-uuid-333") };
        var identity = new ClaimsIdentity(claims, "Bearer");
        httpContext.User = new ClaimsPrincipal(identity);

        SerilogLoggingHelper.EnrichDiagnosticContext(diagnosticContext, httpContext);

        Assert.Equal("trace-diag-999", diagnosticContext.Properties["TraceId"]);
        Assert.Equal("user-uuid-333", diagnosticContext.Properties["UserId"]);
        Assert.Equal("192.168.1.100", diagnosticContext.Properties["ClientIp"]);
        Assert.Equal("Mozilla/5.0 UnitTests", diagnosticContext.Properties["UserAgent"]);
        Assert.Equal("api.g54.local:5001", diagnosticContext.Properties["Host"]);
        Assert.Equal("https", diagnosticContext.Properties["Scheme"]);
    }

    [Fact]
    public void SerilogLoggingHelper_EnrichDiagnosticContext_UsesXForwardedFor_WhenRemoteIpNull()
    {
        var diagnosticContext = new TestDiagnosticContext();
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-forwarded-1",
        };
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.195";

        SerilogLoggingHelper.EnrichDiagnosticContext(diagnosticContext, httpContext);

        Assert.Equal("203.0.113.195", diagnosticContext.Properties["ClientIp"]);
        Assert.DoesNotContain("UserId", diagnosticContext.Properties.Keys);
    }

    [Theory]
    [InlineData(500, "/api/users", false, LogEventLevel.Error)]
    [InlineData(503, "/api/users", false, LogEventLevel.Error)]
    [InlineData(200, "/api/users", true, LogEventLevel.Error)]
    [InlineData(400, "/api/users", false, LogEventLevel.Warning)]
    [InlineData(401, "/api/users", false, LogEventLevel.Warning)]
    [InlineData(404, "/api/users", false, LogEventLevel.Warning)]
    [InlineData(200, "/health", false, LogEventLevel.Verbose)]
    [InlineData(200, "/health/ready", false, LogEventLevel.Verbose)]
    [InlineData(200, "/api/work-types", false, LogEventLevel.Information)]
    [InlineData(201, "/api/employees", false, LogEventLevel.Information)]
    public void SerilogLoggingHelper_GetRequestLogLevel_ReturnsExpectedLevel(
        int statusCode,
        string path,
        bool hasException,
        LogEventLevel expectedLevel)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = statusCode;
        httpContext.Request.Path = path;
        var exception = hasException ? new InvalidOperationException("Test Error") : null;

        var level = SerilogLoggingHelper.GetRequestLogLevel(httpContext, 15.5, exception);

        Assert.Equal(expectedLevel, level);
    }
}
