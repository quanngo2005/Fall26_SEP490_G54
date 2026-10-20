using G54.DAL;
using G54.DAL.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace G54.UnitTests;

public sealed class DataAccessRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothContextsBuildTheirPostgresModelsRegardlessOfRegistrationOrder(bool authFirst)
    {
        const string connectionString = "Host=localhost;Database=registration_test;Username=postgres;Password=unused";
        var services = new ServiceCollection();
        if (authFirst)
        {
            services.AddAuthenticationDataAccess(connectionString);
            services.AddDataAccess(connectionString);
        }
        else
        {
            services.AddDataAccess(connectionString);
            services.AddAuthenticationDataAccess(connectionString);
        }

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var app = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        // Npgsql validates native enum mappings while building the model, without
        // opening a database connection. InMemory tests cannot detect this DI bug.
        Assert.NotEmpty(app.Model.GetEntityTypes());
        Assert.NotEmpty(auth.Model.GetEntityTypes());
    }
}
