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
        services.AddScoped<IWorkService, WorkService>();
        services.AddScoped<IKpiDefinitionService, KpiDefinitionService>();
        services.AddScoped<IKpiMappingService, KpiMappingService>();
        services.AddScoped<IWorkTypeService, WorkTypeService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IVerificationService, VerificationService>();
        return services;
    }
}
