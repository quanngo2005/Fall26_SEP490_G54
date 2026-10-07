using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace G54.DAL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<DepartmentLevelType>("department_level_type");
        dataSourceBuilder.MapEnum<EmployeeStatus>("employee_status");
        dataSourceBuilder.MapEnum<RecordStatus>("record_status");
        dataSourceBuilder.MapEnum<BusinessObjectiveStatus>("business_objective_status");
        dataSourceBuilder.MapEnum<BusinessCampaignStatus>("business_campaign_status");
        dataSourceBuilder.MapEnum<KpiStatus>("kpi_status");
        dataSourceBuilder.MapEnum<KpiAggregationType>("kpi_aggregation_type");
        dataSourceBuilder.MapEnum<KpiDirection>("kpi_direction");
        dataSourceBuilder.MapEnum<WorkPriority>("work_priority");
        dataSourceBuilder.MapEnum<WorkStatus>("work_status");
        dataSourceBuilder.MapEnum<WorkflowDefinitionStatus>("workflow_definition_status");
        dataSourceBuilder.MapEnum<HandoverStatus>("handover_status");
        dataSourceBuilder.MapEnum<SubmissionStatus>("submission_status");
        dataSourceBuilder.MapEnum<VerificationDecision>("verification_decision");
        dataSourceBuilder.MapEnum<VerifiedResultStatus>("verified_result_status");
        dataSourceBuilder.MapEnum<ActualProgressStatus>("actual_progress_status");
        dataSourceBuilder.MapEnum<RiskEventSeverity>("risk_event_severity");
        dataSourceBuilder.MapEnum<RiskEventStatus>("risk_event_status");
        dataSourceBuilder.MapEnum<AppealStatus>("appeal_status");
        dataSourceBuilder.MapEnum<PeriodType>("period_type");
        dataSourceBuilder.MapEnum<PeriodStatus>("period_status");
        dataSourceBuilder.MapEnum<EvaluationProfileStatus>("evaluation_profile_status");
        dataSourceBuilder.MapEnum<PerformanceEvaluationStatus>("performance_evaluation_status");
        dataSourceBuilder.MapEnum<TargetAssignmentStatus>("target_assignment_status");
        dataSourceBuilder.MapEnum<TargetSplitDimension>("target_split_dimension");
        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<DatabaseSeeder>();
        return services;
    }
}
