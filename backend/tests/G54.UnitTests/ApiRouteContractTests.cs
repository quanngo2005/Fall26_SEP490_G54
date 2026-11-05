using System.Reflection;
using G54.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace G54.UnitTests;

public sealed class ApiRouteContractTests
{
    [Theory]
    [InlineData(typeof(AuthController), "auth")]
    [InlineData(typeof(EmployeeController), "employees")]
    [InlineData(typeof(DepartmentsController), "departments")]
    [InlineData(typeof(WorksController), "works")]
    [InlineData(typeof(WorkTypeController), "work-types")]
    [InlineData(typeof(MeController), "me")]
    [InlineData(typeof(KpiDefinitionController), "kpi-definitions")]
    [InlineData(typeof(KpiMappingController), "business-objectives/{objectiveId:guid}/kpis")]
    [InlineData(typeof(BusinessObjectivesController), "business-objectives")]
    [InlineData(typeof(VerificationController), "verifications")]
    [InlineData(typeof(EvaluationController), "evaluations")]
    [InlineData(typeof(CampaignController), "campaigns")]
    [InlineData(typeof(EvaluationProfilesController), "evaluation-profiles")]
    [InlineData(typeof(PerformanceDashboardController), "performance-dashboard")]
    [InlineData(typeof(TargetAssignmentsController), "target-assignments")]
    [InlineData(typeof(MyTargetAssignmentsController), "target-assignments")]
    [InlineData(typeof(ResultSubmissionsController), "")]
    [InlineData(typeof(AppealsController), "")]
    [InlineData(typeof(HandoversController), "")]
    [InlineData(typeof(HelloController), "hello")]
    [InlineData(typeof(AuditLogsController), "audit-logs")]
    [InlineData(typeof(DataImportsController), "data-imports")]
    public void ControllersExposeRootResourceRoutes(Type controller, string expectedRoute)
    {
        var route = controller.GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal(expectedRoute, route.Template);
    }

    [Fact]
    public void WorksController_ExposesPatchStatusSubresource()
    {
        var action = typeof(WorksController).GetMethod(nameof(WorksController.ChangeWorkStatus));
        Assert.NotNull(action);

        var patch = action.GetCustomAttribute<HttpPatchAttribute>();
        Assert.NotNull(patch);
        Assert.Equal("{id:guid}/status", patch.Template);
    }

    [Fact]
    public void AuditLogsController_ExposesListAndDetailGetRoutes()
    {
        Assert.Null(
            typeof(AuditLogsController).GetMethod(nameof(AuditLogsController.GetAuditLogs))?.GetCustomAttribute<HttpGetAttribute>()?.Template);
        Assert.Equal(
            "{id:guid}",
            typeof(AuditLogsController).GetMethod(nameof(AuditLogsController.GetAuditLog))?.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    /// <summary>
    /// Two actions with the same verb and route shape (parameter names ignored) throw AmbiguousMatchException
    /// at request time, which controller unit tests never see.
    /// </summary>
    [Fact]
    public void NoTwoActionsShareTheSameVerbAndRoute()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);

        var routes = controllers.SelectMany(controller =>
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                    .SelectMany(attribute => attribute.HttpMethods.Select(verb =>
                        $"{verb} {NormalizeRoute(prefix, attribute.Template)}  ({controller.Name}.{method.Name})")));
        }).ToList();

        var duplicates = routes
            .GroupBy(route => route[..route.IndexOf("  (", StringComparison.Ordinal)])
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(" | ", group))
            .ToList();

        Assert.True(duplicates.Count == 0, "Ambiguous routes:\n" + string.Join("\n", duplicates));
    }

    private static string NormalizeRoute(string prefix, string? template)
    {
        var full = template is not null && (template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal))
            ? template.TrimStart('~', '/')
            : string.Join('/', new[] { prefix, template ?? string.Empty }.Where(part => part.Length > 0));

        // "{deptYearId:guid}" and "{id:guid}" match the same URLs; keep only the constraint.
        return System.Text.RegularExpressions.Regex.Replace(full.ToLowerInvariant(), @"\{[^}:]+(:[^}]*)?\}", "{$1}");
    }

    [Fact]
    public void ControllerActionsDoNotReintroduceApiOrVersionPrefixes()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type));

        foreach (var controller in controllers)
        {
            var routes = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>());
            foreach (var route in routes)
            {
                var segments = (route.Template ?? string.Empty).TrimStart('~', '/').Split('/');
                Assert.DoesNotContain("api", segments);
                Assert.DoesNotContain("v1", segments);
            }
        }
    }
}
