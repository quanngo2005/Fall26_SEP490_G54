using System;
using System.IO;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261005000000_InitialProgestSchema")]
public partial class InitialProgestSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var assembly = typeof(InitialProgestSchema).Assembly;
        var resourceName = "G54.DAL.Scripts.progest_erd_v0_10_postgresql.sql";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Could not find embedded resource: '{resourceName}'. Make sure G54.DAL.csproj includes EmbeddedResource.");
        }

        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var rollbackSql = @"
DROP TABLE IF EXISTS performance_contribution CASCADE;
DROP TABLE IF EXISTS performance_evaluation CASCADE;
DROP TABLE IF EXISTS actual_progress CASCADE;
DROP TABLE IF EXISTS evidence CASCADE;
DROP TABLE IF EXISTS appeal CASCADE;
DROP TABLE IF EXISTS risk_event CASCADE;
DROP TABLE IF EXISTS work_result_item CASCADE;
DROP TABLE IF EXISTS verified_work_result CASCADE;
DROP TABLE IF EXISTS verification CASCADE;
DROP TABLE IF EXISTS result_submission CASCADE;
DROP TABLE IF EXISTS task_handover CASCADE;
DROP TABLE IF EXISTS work CASCADE;
DROP TABLE IF EXISTS target_assignment CASCADE;
DROP TABLE IF EXISTS evaluation_period CASCADE;
DROP TABLE IF EXISTS evaluation_profile_item CASCADE;
DROP TABLE IF EXISTS evaluation_profile CASCADE;
DROP TABLE IF EXISTS work_type_kpi CASCADE;
DROP TABLE IF EXISTS work_type CASCADE;
DROP TABLE IF EXISTS workflow_definition CASCADE;
DROP TABLE IF EXISTS job_position_work_type CASCADE;
DROP TABLE IF EXISTS campaign_work_type CASCADE;
DROP TABLE IF EXISTS participant CASCADE;
DROP TABLE IF EXISTS objective_kpi CASCADE;
DROP TABLE IF EXISTS kpi_definition CASCADE;
DROP TABLE IF EXISTS kpi_group CASCADE;
DROP TABLE IF EXISTS business_campaign CASCADE;
DROP TABLE IF EXISTS business_objective CASCADE;
DROP TABLE IF EXISTS role_permission CASCADE;
DROP TABLE IF EXISTS user_role CASCADE;
DROP TABLE IF EXISTS permission CASCADE;
DROP TABLE IF EXISTS role CASCADE;
DROP TABLE IF EXISTS account CASCADE;
DROP TABLE IF EXISTS employee CASCADE;
DROP TABLE IF EXISTS job_position CASCADE;
DROP TABLE IF EXISTS department CASCADE;
DROP TABLE IF EXISTS users CASCADE;

DROP FUNCTION IF EXISTS set_updated_at() CASCADE;
DROP FUNCTION IF EXISTS validate_evaluation_period() CASCADE;
DROP FUNCTION IF EXISTS validate_profile_weight_sum() CASCADE;
DROP FUNCTION IF EXISTS validate_profile_activation() CASCADE;
DROP FUNCTION IF EXISTS validate_work_type_kpi_dates() CASCADE;
DROP FUNCTION IF EXISTS evaluation_period_is_descendant(UUID, UUID) CASCADE;
DROP FUNCTION IF EXISTS department_is_descendant(UUID, UUID) CASCADE;
DROP FUNCTION IF EXISTS target_owner_matches(UUID, UUID, UUID, UUID) CASCADE;
DROP FUNCTION IF EXISTS validate_target_assignment() CASCADE;
DROP FUNCTION IF EXISTS validate_target_children_sum() CASCADE;
DROP FUNCTION IF EXISTS validate_work_result_item() CASCADE;
DROP FUNCTION IF EXISTS prevent_work_result_item_source_edit() CASCADE;
DROP FUNCTION IF EXISTS validate_submission_workflow() CASCADE;
DROP FUNCTION IF EXISTS enforce_verification_lifecycle() CASCADE;
DROP FUNCTION IF EXISTS close_submission_on_approval() CASCADE;
DROP FUNCTION IF EXISTS validate_verified_work_result() CASCADE;
DROP FUNCTION IF EXISTS sync_verified_result_invalidation() CASCADE;
DROP FUNCTION IF EXISTS invalidate_source_actual_progress() CASCADE;
DROP FUNCTION IF EXISTS validate_actual_progress() CASCADE;
DROP FUNCTION IF EXISTS validate_performance_contribution() CASCADE;
DROP FUNCTION IF EXISTS validate_performance_contribution_cap() CASCADE;

DROP TYPE IF EXISTS target_split_dimension CASCADE;
DROP TYPE IF EXISTS target_assignment_status CASCADE;
DROP TYPE IF EXISTS performance_evaluation_status CASCADE;
DROP TYPE IF EXISTS evaluation_profile_status CASCADE;
DROP TYPE IF EXISTS period_status CASCADE;
DROP TYPE IF EXISTS period_type CASCADE;
DROP TYPE IF EXISTS appeal_status CASCADE;
DROP TYPE IF EXISTS risk_event_status CASCADE;
DROP TYPE IF EXISTS risk_event_severity CASCADE;
DROP TYPE IF EXISTS actual_progress_status CASCADE;
DROP TYPE IF EXISTS verified_result_status CASCADE;
DROP TYPE IF EXISTS verification_decision CASCADE;
DROP TYPE IF EXISTS submission_status CASCADE;
DROP TYPE IF EXISTS handover_status CASCADE;
DROP TYPE IF EXISTS workflow_definition_status CASCADE;
DROP TYPE IF EXISTS work_status CASCADE;
DROP TYPE IF EXISTS work_priority CASCADE;
DROP TYPE IF EXISTS kpi_direction CASCADE;
DROP TYPE IF EXISTS kpi_aggregation_type CASCADE;
DROP TYPE IF EXISTS kpi_status CASCADE;
DROP TYPE IF EXISTS business_campaign_status CASCADE;
DROP TYPE IF EXISTS business_objective_status CASCADE;
DROP TYPE IF EXISTS record_status CASCADE;
DROP TYPE IF EXISTS employee_status CASCADE;
DROP TYPE IF EXISTS department_level_type CASCADE;
";
        migrationBuilder.Sql(rollbackSql);
    }
}
