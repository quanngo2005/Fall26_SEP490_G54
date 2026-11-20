using G54.BLL.Common.Constants;
using Xunit;

namespace G54.UnitTests;

public sealed class RoleCodesTests
{
    [Fact]
    public void ThereAreExactlySixRoles()
    {
        Assert.Equal(
            ["EMPLOYEE", "DEPT_MANAGER", "BRANCH_MANAGER", "HR_OFFICER", "VERIFIER", "SYSTEM_ADMIN"],
            RoleCodes.All);
    }

    [Theory]
    [InlineData("EMPLOYEE", "employee")]
    [InlineData("DEPT_MANAGER", "departmentManager")]
    [InlineData("BRANCH_MANAGER", "branchManager")]
    [InlineData("HR_OFFICER", "hr")]
    [InlineData("VERIFIER", "qa")]
    [InlineData("SYSTEM_ADMIN", "admin")]
    [InlineData(" system-admin ", null)]
    public void EachRoleOpensItsOwnWorkspace(string code, string? workspace)
    {
        Assert.Equal(workspace, RoleCodes.Workspace(code));
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("MANAGER")]
    [InlineData("QA")]
    [InlineData("COMPLIANCE")]
    [InlineData("HR")]
    [InlineData("HR_MANAGER")]
    [InlineData("DEPARTMENT_MANAGER")]
    [InlineData("")]
    public void RemovedAliasesOpenNoWorkspace(string code)
    {
        Assert.Null(RoleCodes.Workspace(code));
    }
}
