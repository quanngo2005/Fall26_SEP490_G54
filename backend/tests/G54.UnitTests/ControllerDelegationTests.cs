using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

/// <summary>
/// Calls every action of thin controllers against recording stub services: each action must reach its service and answer
/// with a 2xx result. Covers the delegation code that the out-of-process HTTP tests cannot report on.
/// </summary>
public sealed class ControllerDelegationTests
{
    private static readonly byte[] XlsxBytes = [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public async Task TargetAssignmentController_EveryActionDelegatesAndReturns2xx()
    {
        var controller = new TargetAssignmentController(
            Stub<ITargetAllocationService>(out var allocation),
            Stub<ITargetImportService>(out var import),
            Stub<ITargetSetupService>(out var setup));

        var actions = await RunAllAsync(controller);

        Assert.Equal(13, actions.Count);
        Assert.All(actions, a => AssertSuccess(a.Result, a.Name));
        Assert.NotEmpty(allocation.Calls);
        Assert.NotEmpty(import.Calls);
        Assert.NotEmpty(setup.Calls);
    }

    [Fact]
    public async Task TargetAssignmentController_RejectsMissingOrNonXlsxFiles()
    {
        var controller = Prepare(new TargetAssignmentController(
            Stub<ITargetAllocationService>(out _),
            Stub<ITargetImportService>(out var import),
            Stub<ITargetSetupService>(out _)));
        var notXlsx = new FormFile(new MemoryStream(XlsxBytes), 0, XlsxBytes.Length, "file", "targets.csv");
        var fakeXlsx = new FormFile(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]), 0, 8, "file", "targets.xlsx");

        var results = new[]
        {
            (await controller.PreviewImport(Guid.NewGuid(), 2026, null, default)).Result,
            (await controller.PreviewImport(Guid.NewGuid(), 2026, notXlsx, default)).Result,
            (await controller.CommitImport(Guid.NewGuid(), 2026, fakeXlsx, default)).Result,
        };

        Assert.All(results, r => Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(r).StatusCode));
        Assert.Empty(import.Calls);
    }

    [Fact]
    public async Task TransactionImportsController_EveryActionDelegatesAndReturns2xx()
    {
        var controller = new TransactionImportsController(
            Stub<ITransactionImportService>(out var service),
            Stub<IWorkTransactionService>(out var transactions),
            Options.Create(new SystemParametersOptions()),
            Stub<ITransactionImportTemplateProvider>(out var template));

        var actions = await RunAllAsync(controller);

        Assert.Equal(6, actions.Count);
        Assert.All(actions, a => AssertSuccess(a.Result, a.Name));
        Assert.Contains("ImportAsync", service.Calls);
        Assert.Contains("ReconcileBatchAsync", transactions.Calls);
        Assert.NotEmpty(template.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("data.csv")]
    public async Task TransactionImportsController_RejectsMissingOrNonXlsxFile(string? fileName)
    {
        var controller = Prepare(new TransactionImportsController(
            Stub<ITransactionImportService>(out var service),
            Stub<IWorkTransactionService>(out _),
            Options.Create(new SystemParametersOptions()),
            Stub<ITransactionImportTemplateProvider>(out _)));
        IFormFile? file = fileName is null ? null : new FormFile(new MemoryStream(XlsxBytes), 0, XlsxBytes.Length, "file", fileName);

        var result = await controller.Import(file, default);

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result.Result).StatusCode);
        Assert.DoesNotContain("ImportAsync", service.Calls);
    }

    [Fact]
    public async Task QualityAssuranceController_EveryActionDelegatesAndReturns2xx()
    {
        var controller = new QualityAssuranceController(
            Stub<IQualityAssuranceService>(out var quality),
            Stub<IQaQueueService>(out var queue),
            Stub<IQaDashboardService>(out var dashboard));

        var actions = await RunAllAsync(controller);

        Assert.True(actions.Count >= 10);
        Assert.All(actions, a => AssertSuccess(a.Result, a.Name));
        Assert.NotEmpty(quality.Calls);
        Assert.NotEmpty(queue.Calls);
        Assert.NotEmpty(dashboard.Calls);
    }

    private static T Stub<T>(out StubService proxy)
        where T : class
    {
        var created = DispatchProxy.Create<T, StubService>();
        proxy = (StubService)(object)created;
        return created;
    }

    private static TController Prepare<TController>(TController controller)
        where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test")),
            },
        };
        return controller;
    }

    private static async Task<List<(string Name, IActionResult Result)>> RunAllAsync(ControllerBase controller)
    {
        Prepare(controller);
        var results = new List<(string, IActionResult)>();
        var actions = controller.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute));
        foreach (var method in actions)
        {
            var value = method.Invoke(controller, [.. method.GetParameters().Select(p => ArgFor(p.ParameterType))]);
            if (value is Task task)
            {
                await task;
                value = task.GetType().GetProperty("Result")!.GetValue(task);
            }

            results.Add((method.Name, value is IConvertToActionResult convertible ? convertible.Convert() : (IActionResult)value!));
        }

        return results;
    }

    private static void AssertSuccess(IActionResult result, string action)
    {
        var status = result switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            FileResult => 200,
            _ => throw new InvalidOperationException($"{action} returned {result.GetType().Name}."),
        };
        Assert.True(status is >= 200 and < 300, $"{action} answered {status}.");
    }

    private static object? ArgFor(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return null;
        }

        if (type == typeof(IFormFile))
        {
            return new FormFile(new MemoryStream(XlsxBytes), 0, XlsxBytes.Length, "file", "data.xlsx");
        }

        if (type == typeof(CancellationToken))
        {
            return CancellationToken.None;
        }

        return type == typeof(Guid) ? Guid.NewGuid() : type == typeof(int) ? 1 : type == typeof(string) ? "x" : DefaultFor(type);
    }

    private static object? DefaultFor(Type type)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type == typeof(byte[]))
        {
            return new byte[] { 1 };
        }

        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type.IsInterface || type.IsAbstract)
        {
            return null;
        }

        return type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : RuntimeHelpers.GetUninitializedObject(type);
    }

    /// <summary>Answers every service call with a default value and remembers the method names.</summary>
    public class StubService : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [DefaultFor(inner)]);
            }

            return returnType == typeof(void) ? null : DefaultFor(returnType);
        }
    }
}
