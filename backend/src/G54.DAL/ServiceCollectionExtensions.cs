using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace G54.DAL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<DatabaseSeeder>();
        return services;
    }
}
