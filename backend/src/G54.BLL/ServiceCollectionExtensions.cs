using G54.BLL.Services;
using Microsoft.Extensions.DependencyInjection;

namespace G54.BLL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddScoped<IHelloService, HelloService>();
        return services;
    }
}
