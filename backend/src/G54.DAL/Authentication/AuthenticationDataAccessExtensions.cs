using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace G54.DAL.Authentication;

public static class AuthenticationDataAccessExtensions
{
    public static IServiceCollection AddAuthenticationDataAccess(
        this IServiceCollection services,
        string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<EmployeeStatus>("employee_status");
        dataSourceBuilder.MapEnum<AuthRecordStatus>("record_status");
        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);
        services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(dataSource));
        return services;
    }
}
