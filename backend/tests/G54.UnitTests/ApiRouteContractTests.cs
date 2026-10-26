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
    [InlineData(typeof(VerificationController), "verifications")]
    [InlineData(typeof(EvaluationController), "evaluations")]
    [InlineData(typeof(CampaignController), "campaigns")]
    [InlineData(typeof(EvaluationProfilesController), "evaluation-profiles")]
    [InlineData(typeof(PerformanceDashboardController), "performance-dashboard")]
    [InlineData(typeof(TargetAssignmentsController), "target-assignments")]
    [InlineData(typeof(ResultSubmissionsController), "")]
    [InlineData(typeof(AppealsController), "")]
    [InlineData(typeof(HelloController), "hello")]
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
