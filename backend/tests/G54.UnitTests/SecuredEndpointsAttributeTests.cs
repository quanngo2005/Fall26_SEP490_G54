using System.Reflection;
using G54.Api.Authorization;
using G54.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Pins the [HasPermission] attribute of every action on the verification, evaluation and campaign controllers
/// (SEC-BE-M08 / SEC-BE-C03 / SEC-BE-C04). Each action must declare exactly one permission with the expected scope.
/// </summary>
public sealed class SecuredEndpointsAttributeTests
{
    private static void AssertPermissions(Type controller, IReadOnlyDictionary<string, (string Resource, string Action, string Scope)> expected)
    {
        foreach (var (action, (resource, verb, scope)) in expected)
        {
            var method = controller.GetMethod(action);
            Assert.NotNull(method);

            var permission = Assert.Single(method!.GetCustomAttributes<HasPermissionAttribute>());
            Assert.True(
                permission.Resource == resource && permission.Action == verb && permission.Scope == scope,
                $"{controller.Name}.{action} expected {resource}:{verb}:{scope} but was {permission.Resource}:{permission.Action}:{permission.Scope}");
        }
    }

    [Fact]
    public void VerificationController_EveryActionRequiresVerificationDecideInDepartmentScope()
    {
        AssertPermissions(typeof(VerificationController), new Dictionary<string, (string, string, string)>
        {
            ["GetDossier"] = ("verification", "decide", "Dept"),
            ["GetDossierLegacy"] = ("verification", "decide", "Dept"),
            ["SubmitDecision"] = ("verification", "decide", "Dept"),
            ["SubmitDecisionLegacy"] = ("verification", "decide", "Dept"),
            ["GetHistory"] = ("verification", "decide", "Dept"),
            ["GetHistoryLegacy"] = ("verification", "decide", "Dept"),
        });
    }

    [Fact]
    public void EvaluationController_EveryActionHasScopedPermission()
    {
        AssertPermissions(typeof(EvaluationController), new Dictionary<string, (string, string, string)>
        {
            ["GetEvaluationForm"] = ("evaluation", "read", "Own"),
            ["GetPeriods"] = ("evaluation", "read", "Own"),
            ["SubmitEvaluation"] = ("evaluation", "submit", "Dept"),
            ["GetDepartmentEmployees"] = ("evaluation", "read", "Dept"),
            ["GetCalibration"] = ("evaluation", "read", "Dept"),
            ["SubmitCalibration"] = ("evaluation", "calibrate", "Dept"),
            ["GetApprovals"] = ("evaluation", "read", "Branch"),
            ["RequestRecalibration"] = ("evaluation", "finalize", "Branch"),
            ["FinalizePeriod"] = ("evaluation", "finalize", "Branch"),
        });
    }

    [Fact]
    public void CampaignController_EveryActionHasScopedPermission()
    {
        AssertPermissions(typeof(CampaignController), new Dictionary<string, (string, string, string)>
        {
            ["GetCampaigns"] = ("campaign", "read", "Dept"),
            ["GetById"] = ("campaign", "read", "Dept"),
            ["Create"] = ("campaign", "manage", "Dept"),
            ["Update"] = ("campaign", "manage", "Dept"),
            ["Delete"] = ("campaign", "manage", "Dept"),
            ["Decide"] = ("campaign", "manage", "All"),
            ["BatchGenerateWork"] = ("campaign", "manage", "Dept"),
        });
    }

    [Theory]
    [InlineData(typeof(VerificationController))]
    [InlineData(typeof(EvaluationController))]
    [InlineData(typeof(CampaignController))]
    public void ControllerRequiresAuthentication(Type controller)
    {
        Assert.NotEmpty(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: false));
    }
}
