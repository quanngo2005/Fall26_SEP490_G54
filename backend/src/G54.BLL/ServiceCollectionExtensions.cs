using G54.BLL.Services;
using Microsoft.Extensions.DependencyInjection;

namespace G54.BLL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddScoped<IHelloService, HelloService>();
        services.AddSingleton<IPasswordEncoder, PasswordEncoder>();
        services.AddSingleton<JwtTokenProvider>();
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
