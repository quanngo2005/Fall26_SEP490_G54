using G54.BLL.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<IStorageService, StorageService>();
        services.AddScoped<IWorkSubmissionValidator, WorkSubmissionValidator>();
        services.AddScoped<IEventBus, EventBus>();
        services.AddScoped<IResultSubmissionService, ResultSubmissionService>();
        services.AddScoped<IVerificationService, VerificationService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ISidebarConfigurationService, SidebarConfigurationService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddSingleton<IPerformanceScoreFormula, PerformanceScoreFormula>();
        services.AddScoped<IPerformanceEvaluationService, PerformanceEvaluationService>();
        services.AddScoped<IEvaluationService, EvaluationService>();
        services.AddScoped<ICampaignService, CampaignService>();
        return services;
    }
}
