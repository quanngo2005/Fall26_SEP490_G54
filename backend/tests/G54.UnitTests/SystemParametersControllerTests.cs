using System.Reflection;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.UnitTests;

public sealed class SystemParametersControllerTests
{
    [Theory]
    [InlineData(nameof(SystemParametersController.Get), "view")]
    [InlineData(nameof(SystemParametersController.Update), "update")]
    [InlineData(nameof(SystemParametersController.GetEvaluationComparison), "view")]
    [InlineData(nameof(SystemParametersController.UpdateEvaluationComparison), "update")]
    [InlineData(nameof(SystemParametersController.GetTierCriteria), "view")]
    [InlineData(nameof(SystemParametersController.UpdateTierCriteria), "update")]
    public void GB878_10_EveryActionRequiresTheMatchingSystemParameterPermission(string action, string permissionAction)
    {
        var type = typeof(SystemParametersController);
        var permission = type.GetMethod(action)!.GetCustomAttribute<HasPermissionAttribute>();

        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), a => a.GetType() == typeof(AuthorizeAttribute));
        Assert.Equal("system-parameters", type.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal(("system_parameter", permissionAction, PermissionScopes.All), (permission?.Resource, permission?.Action, permission?.Scope));
    }
}
