using G54.DAL.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace G54.UnitTests;

public sealed class AuthDbContextModelTests
{
    [Fact]
    public void Model_ContainsOnlyAuthenticationEntities()
    {
        var services = new ServiceCollection();
        services.AddAuthenticationDataAccess("Host=localhost;Database=g54_auth_model_test");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var entityTypes = context.Model.GetEntityTypes().ToArray();

        Assert.Equal(7, entityTypes.Length);
        Assert.Contains(entityTypes, entity => entity.GetTableName() == "account");
        Assert.Contains(entityTypes, entity => entity.GetTableName() == "employee");
        Assert.Contains(entityTypes, entity => entity.GetTableName() == "auth_audit_log");
        Assert.Contains(entityTypes, entity => entity.GetTableName() == "role");
        Assert.Contains(entityTypes, entity => entity.GetTableName() == "permission");
        Assert.Contains(entityTypes, entity => entity.GetTableName() == "user_role");
        Assert.Contains(entityTypes, entity => entity.GetTableName() == "role_permission");
    }
}
