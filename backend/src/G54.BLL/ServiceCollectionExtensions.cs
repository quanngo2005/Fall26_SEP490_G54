using G54.BLL.Common;
using G54.BLL.Common.Import.Reading;
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
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IWorkService, WorkService>();
        services.AddScoped<IKpiDefinitionService, KpiDefinitionService>();
        services.AddScoped<IKpiMappingService, KpiMappingService>();
        services.AddScoped<IBusinessObjectiveService, BusinessObjectiveService>();
        services.AddScoped<IWorkTypeService, WorkTypeService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IStorageService, StorageService>();
        services.AddScoped<IEvidenceService, EvidenceService>();
        services.AddScoped<IWorkSubmissionValidator, WorkSubmissionValidator>();
        services.AddScoped<IEventBus, EventBus>();
        services.AddScoped<IResultSubmissionService, ResultSubmissionService>();
        services.AddScoped<IVerificationService, VerificationService>();
        services.AddScoped<IAppealService, AppealService>();
        services.AddScoped<IRiskEventService, RiskEventService>();
        services.AddScoped<IRiskEventScreenService, RiskEventScreenService>();
        services.AddSingleton<IQaSamplingPolicy, RandomQaSamplingPolicy>();
        services.AddScoped<IQualityAssuranceService, QualityAssuranceService>();
        services.AddScoped<IQaQueueService, QaQueueService>();
        services.AddScoped<IEvaluationPeriodService, EvaluationPeriodService>();
        services.AddScoped<IHandoverService, HandoverService>();
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<ISidebarConfigurationService, SidebarConfigurationService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IJobPositionService, JobPositionService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<IPerformanceScoreFormula, PerformanceScoreFormula>();
        services.AddScoped<IPerformanceScoreCalculator, PerformanceScoreCalculator>();
        services.AddScoped<IPerformanceEvaluationService, PerformanceEvaluationService>();
        services.AddScoped<IPerformanceDashboardService, PerformanceDashboardService>();
        services.AddScoped<ITargetAssignmentService, TargetAssignmentService>();
        services.AddScoped<IWorkingDayCalendar, WeekdayWorkingDayCalendar>();
        services.AddScoped<IEvaluationService, EvaluationService>();
        services.AddScoped<IEvaluationProfileService, EvaluationProfileService>();
        services.AddScoped<ICampaignService, CampaignService>();
        services.AddScoped<ITargetAllocationService, TargetAllocationService>();
        services.AddScoped<ITransactionImportService, TransactionImportService>();
        services.AddScoped<IWorkTransactionService, WorkTransactionService>();
        services.AddHostedService<WorkOverdueBackgroundService>();
        services.AddScoped<IHandoverEscalationService, HandoverEscalationService>();
        services.AddHostedService<HandoverEscalationBackgroundService>();

        services.AddScoped<IExcelSheetReader, ClosedXmlSheetReader>();
        services.AddScoped<IEmployeeImportService, EmployeeImportService>();
        return services;
    }
}
