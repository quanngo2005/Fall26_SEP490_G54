using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Aligns table relationships with ERD Rev 20/10/2026 while keeping every non-relationship column.
/// Kept as-is on purpose: work.assignee_employee_id, the campaign participant table and objective_kpi.
/// Data that cannot be re-linked is deleted (see PR description):
///   - every risk_event (no source for the new NOT NULL recorded_by) and its evidence;
///   - actual_progress rows with no matching employee target, and work_result_item rows left without progress;
///   - evaluation_profile rows no job position pointed at, with their items, evaluations and contributions.
/// Down restores the old columns and triggers but cannot bring deleted rows back.
/// </summary>
public partial class AlignRelationshipsWithErd : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DropLegacyTriggersSql);
        migrationBuilder.Sql(SelfParentsSql);
        migrationBuilder.Sql(WorkCreatorSql);
        migrationBuilder.Sql(ProfilePerPositionSql);
        migrationBuilder.Sql(TargetObjectiveSql);
        migrationBuilder.Sql(RiskEventRecorderSql);
        migrationBuilder.Sql(ProgressLedgerSql);
        migrationBuilder.Sql(EvaluationAppealHandoverSql);
        migrationBuilder.Sql(DropWorkflowSql);
        migrationBuilder.Sql(CreateTriggersSql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DownSql);
        migrationBuilder.Sql(LegacyTriggersSql(
            "validate_target_assignment",
            "validate_work_result_item",
            "prevent_work_result_item_source_edit",
            "validate_submission_workflow",
            "sync_verified_result_invalidation",
            "invalidate_source_actual_progress",
            "validate_actual_progress",
            "validate_performance_contribution",
            "validate_performance_contribution_cap"));
    }

    /// <summary>
    /// Re-reads the original trigger functions from the embedded base schema so Down restores them verbatim.
    /// Each function <c>f</c> has a trigger named <c>trg_f</c>.
    /// </summary>
    private static string LegacyTriggersSql(params string[] functionNames)
    {
        const string resourceName = "G54.DAL.Scripts.progest_erd_v0_10_postgresql.sql";
        using var stream = typeof(AlignRelationshipsWithErd).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not find embedded resource: '{resourceName}'.");
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);

        var sql = new StringBuilder();
        foreach (var name in functionNames)
        {
            sql.Append(Slice(script, $"CREATE OR REPLACE FUNCTION {name}(", "\n$$;")).Append('\n');
            sql.Append(Slice(script, $"CREATE TRIGGER trg_{name}\n", ";")).Append('\n');
        }

        return sql.ToString();
    }

    private static string Slice(string script, string start, string end)
    {
        var from = script.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
        {
            throw new InvalidOperationException($"Base schema script has no '{start.Trim()}'.");
        }

        var to = script.IndexOf(end, from, StringComparison.Ordinal) + end.Length;
        return script[from..to];
    }

    private const string DropLegacyTriggersSql = """
        -- Deferred constraint triggers would block the ALTER TABLEs below (55006).
        SET CONSTRAINTS ALL IMMEDIATE;

        -- These read relationship columns that move or disappear below; recreated at the end of Up.
        DROP TRIGGER IF EXISTS trg_validate_submission_workflow ON result_submission;
        DROP FUNCTION IF EXISTS validate_submission_workflow();
        DROP TRIGGER IF EXISTS trg_sync_verified_result_invalidation ON verified_work_result;
        DROP FUNCTION IF EXISTS sync_verified_result_invalidation();
        DROP TRIGGER IF EXISTS trg_invalidate_source_actual_progress ON verified_work_result;
        DROP TRIGGER IF EXISTS trg_validate_actual_progress ON actual_progress;
        DROP TRIGGER IF EXISTS trg_validate_work_result_item ON work_result_item;
        DROP TRIGGER IF EXISTS trg_prevent_work_result_item_source_edit ON work_result_item;
        DROP TRIGGER IF EXISTS trg_validate_performance_contribution ON performance_contribution;
        DROP TRIGGER IF EXISTS trg_validate_performance_contribution_cap ON performance_contribution;
        DROP TRIGGER IF EXISTS trg_validate_target_assignment ON target_assignment;

        -- Backfills must not look like user edits (work.updated_at feeds department metrics).
        ALTER TABLE work DISABLE TRIGGER trg_work_updated_at;
        ALTER TABLE evaluation_profile DISABLE TRIGGER trg_evaluation_profile_updated_at;
        ALTER TABLE target_assignment DISABLE TRIGGER trg_target_assignment_updated_at;
        ALTER TABLE actual_progress DISABLE TRIGGER trg_actual_progress_updated_at;
        ALTER TABLE work_result_item DISABLE TRIGGER trg_work_result_item_updated_at;
        ALTER TABLE performance_contribution DISABLE TRIGGER trg_performance_contribution_updated_at;
        """;

    private const string SelfParentsSql = """
        ALTER TABLE kpi_group ADD COLUMN parent_kpi_group_id uuid;
        ALTER TABLE kpi_group ADD CONSTRAINT fk_kpi_group_parent
            FOREIGN KEY (parent_kpi_group_id) REFERENCES kpi_group(id) ON DELETE RESTRICT;
        ALTER TABLE kpi_group ADD CONSTRAINT ck_kpi_group_no_self_parent
            CHECK (parent_kpi_group_id IS NULL OR parent_kpi_group_id <> id);
        CREATE INDEX ix_kpi_group_parent ON kpi_group(parent_kpi_group_id);

        ALTER TABLE business_objective ADD COLUMN parent_objective_id uuid;
        ALTER TABLE business_objective ADD CONSTRAINT fk_business_objective_parent
            FOREIGN KEY (parent_objective_id) REFERENCES business_objective(id) ON DELETE RESTRICT;
        ALTER TABLE business_objective ADD CONSTRAINT ck_business_objective_no_self_parent
            CHECK (parent_objective_id IS NULL OR parent_objective_id <> id);
        CREATE INDEX ix_business_objective_parent ON business_objective(parent_objective_id);

        ALTER TABLE business_campaign ADD COLUMN parent_campaign_id uuid;
        ALTER TABLE business_campaign ADD CONSTRAINT fk_business_campaign_parent
            FOREIGN KEY (parent_campaign_id) REFERENCES business_campaign(id) ON DELETE RESTRICT;
        ALTER TABLE business_campaign ADD CONSTRAINT ck_business_campaign_no_self_parent
            CHECK (parent_campaign_id IS NULL OR parent_campaign_id <> id);
        CREATE INDEX ix_business_campaign_parent ON business_campaign(parent_campaign_id);

        ALTER TABLE work ADD COLUMN parent_work_id uuid;
        ALTER TABLE work ADD CONSTRAINT fk_work_parent
            FOREIGN KEY (parent_work_id) REFERENCES work(id) ON DELETE RESTRICT;
        ALTER TABLE work ADD CONSTRAINT ck_work_no_self_parent
            CHECK (parent_work_id IS NULL OR parent_work_id <> id);
        CREATE INDEX ix_work_parent ON work(parent_work_id);

        ALTER TABLE performance_evaluation ADD COLUMN parent_evaluation_id uuid;
        ALTER TABLE performance_evaluation ADD CONSTRAINT fk_performance_evaluation_parent
            FOREIGN KEY (parent_evaluation_id) REFERENCES performance_evaluation(id) ON DELETE RESTRICT;
        ALTER TABLE performance_evaluation ADD CONSTRAINT ck_performance_evaluation_no_self_parent
            CHECK (parent_evaluation_id IS NULL OR parent_evaluation_id <> id);
        CREATE INDEX ix_performance_evaluation_parent ON performance_evaluation(parent_evaluation_id);
        """;

    private const string WorkCreatorSql = """
        -- No creator was recorded; the assignee is the only employee known to be tied to the work.
        ALTER TABLE work ADD COLUMN created_by uuid;
        UPDATE work SET created_by = assignee_employee_id;
        ALTER TABLE work ALTER COLUMN created_by SET NOT NULL;
        ALTER TABLE work ADD CONSTRAINT fk_work_created_by
            FOREIGN KEY (created_by) REFERENCES employee(id) ON DELETE RESTRICT;
        CREATE INDEX ix_work_created_by ON work(created_by);
        """;

    private const string ProfilePerPositionSql = """
        ALTER TABLE evaluation_profile ADD COLUMN job_position_id uuid;

        -- A profile shared by several positions goes to the first position by code.
        UPDATE evaluation_profile ep
        SET job_position_id = jp.id
        FROM (
            SELECT DISTINCT ON (evaluation_profile_id) evaluation_profile_id, id
            FROM job_position
            ORDER BY evaluation_profile_id, code
        ) jp
        WHERE jp.evaluation_profile_id = ep.id;

        CREATE TEMP TABLE orphan_profile AS
            SELECT id FROM evaluation_profile WHERE job_position_id IS NULL;
        CREATE TEMP TABLE orphan_evaluation AS
            SELECT id FROM performance_evaluation
            WHERE evaluation_profile_id IN (SELECT id FROM orphan_profile);

        DELETE FROM performance_contribution
        WHERE performance_evaluation_id IN (SELECT id FROM orphan_evaluation);
        DELETE FROM performance_evaluation WHERE id IN (SELECT id FROM orphan_evaluation);
        DELETE FROM evaluation_profile_item WHERE evaluation_profile_id IN (SELECT id FROM orphan_profile);
        DELETE FROM evaluation_profile WHERE id IN (SELECT id FROM orphan_profile);
        DROP TABLE orphan_evaluation;
        DROP TABLE orphan_profile;

        ALTER TABLE evaluation_profile ALTER COLUMN job_position_id SET NOT NULL;
        ALTER TABLE evaluation_profile ADD CONSTRAINT fk_evaluation_profile_job_position
            FOREIGN KEY (job_position_id) REFERENCES job_position(id) ON DELETE RESTRICT;
        CREATE UNIQUE INDEX uq_evaluation_profile_position_effective
            ON evaluation_profile(job_position_id, effective_from);
        ALTER TABLE job_position DROP COLUMN evaluation_profile_id;
        """;

    private const string TargetObjectiveSql = """
        ALTER TABLE target_assignment ADD COLUMN business_objective_id uuid;

        -- Seed target_assignment.business_objective_id from objective_kpi where the KPI maps to one objective.
        UPDATE target_assignment t
        SET business_objective_id = ok.business_objective_id
        FROM (
            SELECT kpi_definition_id, (array_agg(business_objective_id))[1] AS business_objective_id
            FROM objective_kpi
            GROUP BY kpi_definition_id
            HAVING COUNT(*) = 1
        ) ok
        WHERE ok.kpi_definition_id = t.kpi_definition_id;

        ALTER TABLE target_assignment ADD CONSTRAINT fk_target_assignment_objective
            FOREIGN KEY (business_objective_id) REFERENCES business_objective(id) ON DELETE RESTRICT;
        CREATE INDEX ix_target_assignment_objective ON target_assignment(business_objective_id);

        ALTER TABLE target_assignment DROP CONSTRAINT ck_target_assignment_version_link;
        ALTER TABLE target_assignment
            DROP COLUMN previous_version_id,
            DROP COLUMN approved_by,
            DROP COLUMN assigned_by;

        """;

    private const string RiskEventRecorderSql = """
        ALTER TABLE verified_work_result DROP COLUMN invalidated_by_risk_event_id;

        -- recorded_by is required and was never captured, so existing risk events cannot be kept.
        DELETE FROM evidence WHERE risk_event_id IS NOT NULL;
        DELETE FROM risk_event;

        ALTER TABLE risk_event ADD COLUMN recorded_by uuid NOT NULL;
        ALTER TABLE risk_event ADD CONSTRAINT fk_risk_event_recorded_by
            FOREIGN KEY (recorded_by) REFERENCES employee(id) ON DELETE RESTRICT;
        CREATE INDEX ix_risk_event_recorded_by ON risk_event(recorded_by);
        """;

    private const string ProgressLedgerSql = """
        -- actual_progress now tracks a target. Legacy rows sit on DAY periods, so match the employee
        -- target whose period contains progress_date: ACTIVE first, then the narrowest period, then latest version.
        ALTER TABLE actual_progress ADD COLUMN target_assignment_id uuid;
        UPDATE actual_progress ap
        SET target_assignment_id = (
            SELECT t.id
            FROM target_assignment t
            JOIN evaluation_period p ON p.id = t.evaluation_period_id
            WHERE t.kpi_definition_id = ap.kpi_definition_id
              AND t.employee_id = ap.employee_id
              AND ap.progress_date BETWEEN p.start_date AND p.end_date
            ORDER BY (t.status = 'ACTIVE') DESC, (p.end_date - p.start_date), t.version DESC
            LIMIT 1
        );

        -- One row per (target, day): the latest row survives and absorbs the others' values.
        CREATE TEMP TABLE progress_map AS
        SELECT id AS old_id,
               first_value(id) OVER (
                   PARTITION BY target_assignment_id, progress_date
                   ORDER BY updated_at DESC, id
               ) AS new_id
        FROM actual_progress
        WHERE target_assignment_id IS NOT NULL;

        UPDATE actual_progress ap
        SET value = s.value, denominator = s.denominator
        FROM (
            SELECT m.new_id, SUM(p.value) AS value, SUM(p.denominator) AS denominator
            FROM progress_map m
            JOIN actual_progress p ON p.id = m.old_id
            GROUP BY m.new_id
            HAVING COUNT(*) > 1
        ) s
        WHERE ap.id = s.new_id;

        -- work_result_item -> actual_progress (was actual_progress.source_item_id).
        ALTER TABLE work_result_item ADD COLUMN actual_progress_id uuid, ADD COLUMN employee_id uuid;
        UPDATE work_result_item wri
        SET actual_progress_id = m.new_id, employee_id = ap.employee_id
        FROM actual_progress ap
        JOIN progress_map m ON m.old_id = ap.id
        WHERE ap.source_item_id = wri.id;
        DELETE FROM work_result_item WHERE actual_progress_id IS NULL;

        -- performance_contribution -> actual_progress (was actual_progress.performance_contribution_id).
        ALTER TABLE performance_contribution ADD COLUMN actual_progress_id uuid, ADD COLUMN risk_event_id uuid;
        UPDATE performance_contribution pc
        SET actual_progress_id = s.new_id
        FROM (
            SELECT ap.performance_contribution_id AS contribution_id,
                   (array_agg(m.new_id ORDER BY ap.progress_date, ap.id))[1] AS new_id
            FROM actual_progress ap
            JOIN progress_map m ON m.old_id = ap.id
            WHERE ap.performance_contribution_id IS NOT NULL
            GROUP BY ap.performance_contribution_id
        ) s
        WHERE pc.id = s.contribution_id;

        DELETE FROM actual_progress WHERE id NOT IN (SELECT new_id FROM progress_map);
        DROP TABLE progress_map;

        ALTER TABLE actual_progress ALTER COLUMN target_assignment_id SET NOT NULL;
        ALTER TABLE actual_progress
            DROP COLUMN source_item_id,
            DROP COLUMN performance_contribution_id,
            DROP COLUMN employee_id,
            DROP COLUMN kpi_definition_id,
            DROP COLUMN evaluation_period_id;
        ALTER TABLE actual_progress ADD CONSTRAINT fk_actual_progress_target
            FOREIGN KEY (target_assignment_id) REFERENCES target_assignment(id) ON DELETE RESTRICT;
        CREATE UNIQUE INDEX uq_actual_progress_target_date ON actual_progress(target_assignment_id, progress_date);

        ALTER TABLE work_result_item
            ALTER COLUMN actual_progress_id SET NOT NULL,
            ALTER COLUMN employee_id SET NOT NULL;
        ALTER TABLE work_result_item DROP COLUMN kpi_definition_id;
        ALTER TABLE work_result_item ADD CONSTRAINT fk_work_result_item_progress
            FOREIGN KEY (actual_progress_id) REFERENCES actual_progress(id) ON DELETE RESTRICT;
        ALTER TABLE work_result_item ADD CONSTRAINT fk_work_result_item_employee
            FOREIGN KEY (employee_id) REFERENCES employee(id) ON DELETE RESTRICT;
        CREATE INDEX ix_work_result_item_progress ON work_result_item(actual_progress_id);
        CREATE INDEX ix_work_result_item_employee ON work_result_item(employee_id);
        CREATE UNIQUE INDEX uq_work_result_item_progress_employee
            ON work_result_item(verified_work_result_id, actual_progress_id, employee_id);

        -- Contribution is sourced from progress or a risk event; the KPI/group breakdown links go.
        ALTER TABLE performance_contribution
            DROP CONSTRAINT ck_performance_contribution_kind,
            DROP CONSTRAINT ck_performance_contribution_kpi_level,
            DROP CONSTRAINT ck_performance_contribution_group_level,
            DROP CONSTRAINT fk_performance_contribution_evaluation_identity;
        ALTER TABLE performance_contribution
            DROP COLUMN kpi_definition_id,
            DROP COLUMN evaluation_profile_item_id,
            DROP COLUMN target_assignment_id,
            DROP COLUMN parent_contribution_id;
        ALTER TABLE performance_contribution ALTER COLUMN performance_evaluation_id DROP NOT NULL;
        ALTER TABLE performance_evaluation DROP CONSTRAINT uq_performance_evaluation_identity;
        ALTER TABLE performance_contribution ADD CONSTRAINT fk_performance_contribution_progress
            FOREIGN KEY (actual_progress_id) REFERENCES actual_progress(id) ON DELETE RESTRICT;
        ALTER TABLE performance_contribution ADD CONSTRAINT fk_performance_contribution_risk_event
            FOREIGN KEY (risk_event_id) REFERENCES risk_event(id) ON DELETE RESTRICT;
        CREATE INDEX ix_performance_contribution_progress ON performance_contribution(actual_progress_id);
        CREATE UNIQUE INDEX uq_performance_contribution_risk_event ON performance_contribution(risk_event_id);
        CREATE INDEX ix_performance_contribution_employee_period
            ON performance_contribution(employee_id, evaluation_period_id);
        CREATE INDEX ix_performance_contribution_evaluation ON performance_contribution(performance_evaluation_id);
        """;

    private const string EvaluationAppealHandoverSql = """
        ALTER TABLE performance_evaluation DROP COLUMN reviewer_employee_id;
        ALTER TABLE evidence DROP COLUMN uploaded_by;

        ALTER TABLE appeal ALTER COLUMN result_submission_id DROP NOT NULL;
        ALTER TABLE appeal ADD COLUMN performance_evaluation_id uuid, ADD COLUMN resolved_by uuid;
        ALTER TABLE appeal ADD CONSTRAINT fk_appeal_evaluation
            FOREIGN KEY (performance_evaluation_id) REFERENCES performance_evaluation(id) ON DELETE RESTRICT;
        ALTER TABLE appeal ADD CONSTRAINT fk_appeal_resolved_by
            FOREIGN KEY (resolved_by) REFERENCES employee(id) ON DELETE RESTRICT;
        ALTER TABLE appeal ADD CONSTRAINT ck_appeal_target_xor
            CHECK ((result_submission_id IS NULL) <> (performance_evaluation_id IS NULL));
        CREATE INDEX ix_appeal_evaluation ON appeal(performance_evaluation_id);
        CREATE INDEX ix_appeal_resolved_by ON appeal(resolved_by);

        ALTER TABLE task_handover ADD COLUMN approved_by uuid;
        ALTER TABLE task_handover ADD CONSTRAINT fk_task_handover_approved_by
            FOREIGN KEY (approved_by) REFERENCES employee(id) ON DELETE RESTRICT;
        CREATE INDEX ix_task_handover_approved_by ON task_handover(approved_by);
        """;

    private const string DropWorkflowSql = """
        ALTER TABLE result_submission DROP COLUMN workflow_definition_id;
        ALTER TABLE work_type DROP COLUMN workflow_definition_id;
        DROP TABLE workflow_definition;
        DROP TYPE workflow_definition_status;
        """;

    private const string CreateTriggersSql = """
        ALTER TABLE work ENABLE TRIGGER trg_work_updated_at;
        ALTER TABLE evaluation_profile ENABLE TRIGGER trg_evaluation_profile_updated_at;
        ALTER TABLE target_assignment ENABLE TRIGGER trg_target_assignment_updated_at;
        ALTER TABLE actual_progress ENABLE TRIGGER trg_actual_progress_updated_at;
        ALTER TABLE work_result_item ENABLE TRIGGER trg_work_result_item_updated_at;
        ALTER TABLE performance_contribution ENABLE TRIGGER trg_performance_contribution_updated_at;

        CREATE OR REPLACE FUNCTION validate_target_assignment()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        DECLARE
            p target_assignment%ROWTYPE;
        BEGIN
            IF NEW.parent_assignment_id IS NOT NULL THEN
                IF NEW.parent_assignment_id = NEW.id THEN
                    RAISE EXCEPTION 'TargetAssignment cannot be its own parent';
                END IF;

                SELECT * INTO p FROM target_assignment WHERE id = NEW.parent_assignment_id;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'Parent TargetAssignment % not found', NEW.parent_assignment_id;
                END IF;

                IF p.kpi_definition_id <> NEW.kpi_definition_id THEN
                    RAISE EXCEPTION 'Parent and child TargetAssignment must use the same KPI';
                END IF;

                IF NEW.split_dimension IS NULL THEN
                    RAISE EXCEPTION 'split_dimension is required when parent_assignment_id is set';
                END IF;

                IF NEW.split_dimension = 'PERIOD' THEN
                    IF p.department_id IS DISTINCT FROM NEW.department_id
                       OR p.employee_id IS DISTINCT FROM NEW.employee_id THEN
                        RAISE EXCEPTION 'PERIOD split may change only the evaluation period';
                    END IF;

                    IF NEW.evaluation_period_id = p.evaluation_period_id
                       OR NOT evaluation_period_is_descendant(NEW.evaluation_period_id, p.evaluation_period_id) THEN
                        RAISE EXCEPTION 'PERIOD split must move to a different descendant evaluation period';
                    END IF;
                ELSE
                    IF p.evaluation_period_id <> NEW.evaluation_period_id THEN
                        RAISE EXCEPTION 'ORG split must keep the same evaluation period';
                    END IF;

                    IF p.department_id IS NULL THEN
                        RAISE EXCEPTION 'ORG split requires parent assignment to target a department';
                    END IF;

                    IF NOT target_owner_matches(
                        p.department_id, p.employee_id,
                        NEW.department_id, NEW.employee_id
                    ) THEN
                        RAISE EXCEPTION 'ORG split child must be in the organizational descendant branch of the parent';
                    END IF;
                END IF;

                IF EXISTS (
                    WITH RECURSIVE chain AS (
                        SELECT id, parent_assignment_id
                        FROM target_assignment
                        WHERE id = NEW.parent_assignment_id
                        UNION ALL
                        SELECT t.id, t.parent_assignment_id
                        FROM target_assignment t
                        JOIN chain c ON t.id = c.parent_assignment_id
                    )
                    SELECT 1 FROM chain WHERE id = NEW.id
                ) THEN
                    RAISE EXCEPTION 'TargetAssignment parent relationship would create a cycle';
                END IF;
            ELSE
                IF NEW.split_dimension IS NOT NULL THEN
                    RAISE EXCEPTION 'split_dimension must be NULL when parent_assignment_id is NULL';
                END IF;
            END IF;

            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER trg_validate_target_assignment
        BEFORE INSERT OR UPDATE ON target_assignment
        FOR EACH ROW EXECUTE FUNCTION validate_target_assignment();

        CREATE OR REPLACE FUNCTION validate_actual_progress()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        DECLARE
            a kpi_aggregation_type;
            p_start DATE;
            p_end DATE;
        BEGIN
            SELECT k.aggregation_type, ep.start_date, ep.end_date
            INTO a, p_start, p_end
            FROM target_assignment t
            JOIN kpi_definition k ON k.id = t.kpi_definition_id
            JOIN evaluation_period ep ON ep.id = t.evaluation_period_id
            WHERE t.id = NEW.target_assignment_id;

            IF a = 'RATIO' THEN
                IF NEW.denominator IS NULL OR NEW.denominator <= 0 THEN
                    RAISE EXCEPTION 'RATIO ActualProgress requires denominator > 0';
                END IF;
            ELSE
                IF NEW.denominator IS NOT NULL THEN
                    RAISE EXCEPTION 'Non-RATIO ActualProgress must not have denominator';
                END IF;
            END IF;

            IF NEW.progress_date < p_start OR NEW.progress_date > p_end THEN
                RAISE EXCEPTION 'ActualProgress.progress_date must fall inside its TargetAssignment period';
            END IF;

            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER trg_validate_actual_progress
        BEFORE INSERT OR UPDATE ON actual_progress
        FOR EACH ROW EXECUTE FUNCTION validate_actual_progress();

        CREATE OR REPLACE FUNCTION validate_work_result_item()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        DECLARE
            v_work_type_id UUID;
            v_kpi UUID;
            v_agg kpi_aggregation_type;
        BEGIN
            SELECT t.kpi_definition_id, k.aggregation_type
            INTO v_kpi, v_agg
            FROM actual_progress ap
            JOIN target_assignment t ON t.id = ap.target_assignment_id
            JOIN kpi_definition k ON k.id = t.kpi_definition_id
            WHERE ap.id = NEW.actual_progress_id;

            SELECT w.work_type_id
            INTO v_work_type_id
            FROM verified_work_result vwr
            JOIN verification v ON v.id = vwr.verification_id
            JOIN result_submission rs ON rs.id = v.result_submission_id
            JOIN work w ON w.id = rs.work_id
            WHERE vwr.id = NEW.verified_work_result_id;

            IF v_work_type_id IS NULL THEN
                RAISE EXCEPTION 'Unclassified Work cannot generate WorkResultItem KPI data';
            END IF;

            IF NOT EXISTS (
                SELECT 1
                FROM work_type_kpi wtk
                JOIN verified_work_result vwr ON vwr.id = NEW.verified_work_result_id
                JOIN verification ver ON ver.id = vwr.verification_id
                WHERE wtk.work_type_id = v_work_type_id
                  AND wtk.kpi_definition_id = v_kpi
                  AND ver.verified_at::date >= wtk.effective_from
                  AND (wtk.effective_to IS NULL OR ver.verified_at::date <= wtk.effective_to)
            ) THEN
                RAISE EXCEPTION 'KPI % is not applicable to the WorkType of WorkResultItem %', v_kpi, NEW.id;
            END IF;

            IF v_agg = 'RATIO' THEN
                IF NEW.denominator IS NULL OR NEW.denominator <= 0 THEN
                    RAISE EXCEPTION 'RATIO KPI WorkResultItem requires denominator > 0';
                END IF;
            ELSE
                IF NEW.denominator IS NOT NULL THEN
                    RAISE EXCEPTION 'Non-RATIO KPI WorkResultItem must not have denominator';
                END IF;
            END IF;

            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER trg_validate_work_result_item
        BEFORE INSERT OR UPDATE ON work_result_item
        FOR EACH ROW EXECUTE FUNCTION validate_work_result_item();

        -- Every item now feeds progress, so its source fields are always frozen.
        CREATE OR REPLACE FUNCTION prevent_work_result_item_source_edit()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        BEGIN
            IF NEW.actual_progress_id IS DISTINCT FROM OLD.actual_progress_id
               OR NEW.employee_id IS DISTINCT FROM OLD.employee_id
               OR NEW.value IS DISTINCT FROM OLD.value
               OR NEW.denominator IS DISTINCT FROM OLD.denominator THEN
                RAISE EXCEPTION 'WorkResultItem % cannot be changed after ActualProgress has been generated; create an adjustment result instead', OLD.id;
            END IF;
            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER trg_prevent_work_result_item_source_edit
        BEFORE UPDATE ON work_result_item
        FOR EACH ROW EXECUTE FUNCTION prevent_work_result_item_source_edit();

        -- Progress rows aggregate a day of results: take the invalidated items back out
        -- and invalidate the row only once nothing is left.
        CREATE OR REPLACE FUNCTION invalidate_source_actual_progress()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        BEGIN
            IF NEW.status = 'INVALID' AND OLD.status IS DISTINCT FROM NEW.status THEN
                UPDATE actual_progress ap
                SET value = GREATEST(ap.value - s.value, 0),
                    denominator = CASE
                        WHEN ap.denominator IS NULL THEN NULL
                        WHEN ap.denominator - COALESCE(s.denominator, 0) > 0 THEN ap.denominator - COALESCE(s.denominator, 0)
                        ELSE ap.denominator
                    END,
                    status = CASE WHEN ap.value - s.value <= 0 THEN 'INVALID'::actual_progress_status ELSE ap.status END,
                    updated_at = now()
                FROM (
                    SELECT actual_progress_id, SUM(value) AS value, SUM(denominator) AS denominator
                    FROM work_result_item
                    WHERE verified_work_result_id = NEW.id
                    GROUP BY actual_progress_id
                ) s
                WHERE ap.id = s.actual_progress_id;
            END IF;
            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER trg_invalidate_source_actual_progress
        AFTER UPDATE OF status ON verified_work_result
        FOR EACH ROW EXECUTE FUNCTION invalidate_source_actual_progress();

        CREATE OR REPLACE FUNCTION validate_performance_contribution()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        DECLARE
            pe performance_evaluation%ROWTYPE;
            risk_employee UUID;
        BEGIN
            IF NEW.performance_evaluation_id IS NOT NULL THEN
                SELECT * INTO pe FROM performance_evaluation WHERE id = NEW.performance_evaluation_id;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'PerformanceEvaluation % not found', NEW.performance_evaluation_id;
                END IF;

                IF NEW.employee_id <> pe.employee_id OR NEW.evaluation_period_id <> pe.evaluation_period_id THEN
                    RAISE EXCEPTION 'Contribution employee/period must match PerformanceEvaluation';
                END IF;
            END IF;

            IF NEW.risk_event_id IS NOT NULL THEN
                SELECT employee_id INTO risk_employee FROM risk_event WHERE id = NEW.risk_event_id;
                IF risk_employee IS DISTINCT FROM NEW.employee_id THEN
                    RAISE EXCEPTION 'Penalty Contribution must be attributed to the RiskEvent employee';
                END IF;
            END IF;

            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER trg_validate_performance_contribution
        BEFORE INSERT OR UPDATE ON performance_contribution
        FOR EACH ROW EXECUTE FUNCTION validate_performance_contribution();

        CREATE OR REPLACE FUNCTION validate_performance_contribution_cap()
        RETURNS TRIGGER
        LANGUAGE plpgsql
        AS $$
        DECLARE
            cap NUMERIC(10,4);
        BEGIN
            IF NEW.actual_progress_id IS NOT NULL AND NEW.achievement_percent_capped IS NOT NULL THEN
                SELECT k.max_achievement_percent
                INTO cap
                FROM actual_progress ap
                JOIN target_assignment t ON t.id = ap.target_assignment_id
                JOIN kpi_definition k ON k.id = t.kpi_definition_id
                WHERE ap.id = NEW.actual_progress_id;

                IF cap IS NOT NULL AND NEW.achievement_percent_capped > cap THEN
                    RAISE EXCEPTION 'achievement_percent_capped % exceeds KPI cap %',
                        NEW.achievement_percent_capped, cap;
                END IF;
            END IF;
            RETURN NEW;
        END;
        $$;

        CREATE TRIGGER trg_validate_performance_contribution_cap
        BEFORE INSERT OR UPDATE ON performance_contribution
        FOR EACH ROW EXECUTE FUNCTION validate_performance_contribution_cap();
        """;

    private const string DownSql = """
        -- Deferred constraint triggers would block the ALTER TABLEs below (55006).
        SET CONSTRAINTS ALL IMMEDIATE;

        DROP TRIGGER IF EXISTS trg_validate_target_assignment ON target_assignment;
        DROP TRIGGER IF EXISTS trg_validate_actual_progress ON actual_progress;
        DROP TRIGGER IF EXISTS trg_validate_work_result_item ON work_result_item;
        DROP TRIGGER IF EXISTS trg_prevent_work_result_item_source_edit ON work_result_item;
        DROP TRIGGER IF EXISTS trg_invalidate_source_actual_progress ON verified_work_result;
        DROP TRIGGER IF EXISTS trg_validate_performance_contribution ON performance_contribution;
        DROP TRIGGER IF EXISTS trg_validate_performance_contribution_cap ON performance_contribution;

        ALTER TABLE work DISABLE TRIGGER trg_work_updated_at;
        ALTER TABLE job_position DISABLE TRIGGER trg_job_position_updated_at;
        ALTER TABLE work_type DISABLE TRIGGER trg_work_type_updated_at;
        ALTER TABLE actual_progress DISABLE TRIGGER trg_actual_progress_updated_at;
        ALTER TABLE work_result_item DISABLE TRIGGER trg_work_result_item_updated_at;

        -- Workflow definition (work_type needs one, so restored rows share a placeholder).
        CREATE TYPE workflow_definition_status AS ENUM ('DRAFT', 'ACTIVE', 'INACTIVE', 'ARCHIVED');
        CREATE TABLE workflow_definition (
            id          uuid NOT NULL DEFAULT gen_random_uuid(),
            code        varchar(50) NOT NULL,
            name        varchar(200) NOT NULL,
            description text,
            version     integer NOT NULL DEFAULT 1,
            status      workflow_definition_status NOT NULL DEFAULT 'ACTIVE',
            updated_at  timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT workflow_definition_pkey PRIMARY KEY (id),
            CONSTRAINT uq_workflow_definition_code_version UNIQUE (code, version),
            CONSTRAINT ck_workflow_definition_version CHECK (version > 0)
        );
        CREATE TRIGGER trg_workflow_definition_updated_at BEFORE UPDATE ON workflow_definition
            FOR EACH ROW EXECUTE FUNCTION set_updated_at();
        INSERT INTO workflow_definition (code, name, description)
        VALUES ('WF-RESTORED', 'Restored workflow', 'Placeholder created when rolling back AlignRelationshipsWithErd');

        ALTER TABLE work_type ADD COLUMN workflow_definition_id uuid;
        UPDATE work_type SET workflow_definition_id = (SELECT id FROM workflow_definition WHERE code = 'WF-RESTORED');
        ALTER TABLE work_type ALTER COLUMN workflow_definition_id SET NOT NULL;
        ALTER TABLE work_type ADD CONSTRAINT fk_work_type_workflow_definition
            FOREIGN KEY (workflow_definition_id) REFERENCES workflow_definition(id) ON DELETE RESTRICT;
        ALTER TABLE result_submission ADD COLUMN workflow_definition_id uuid;
        UPDATE result_submission rs
        SET workflow_definition_id = wt.workflow_definition_id
        FROM work w JOIN work_type wt ON wt.id = w.work_type_id
        WHERE w.id = rs.work_id;
        ALTER TABLE result_submission ADD CONSTRAINT fk_result_submission_workflow_definition
            FOREIGN KEY (workflow_definition_id) REFERENCES workflow_definition(id) ON DELETE RESTRICT;

        -- Task handover, appeal, evidence, evaluation.
        ALTER TABLE task_handover DROP COLUMN approved_by;
        DELETE FROM evidence WHERE appeal_id IN (SELECT id FROM appeal WHERE result_submission_id IS NULL);
        DELETE FROM appeal WHERE result_submission_id IS NULL;
        ALTER TABLE appeal DROP CONSTRAINT ck_appeal_target_xor;
        ALTER TABLE appeal DROP COLUMN performance_evaluation_id, DROP COLUMN resolved_by;
        ALTER TABLE appeal ALTER COLUMN result_submission_id SET NOT NULL;
        ALTER TABLE evidence ADD COLUMN uploaded_by uuid;
        ALTER TABLE evidence ADD CONSTRAINT fk_evidence_uploader
            FOREIGN KEY (uploaded_by) REFERENCES employee(id) ON DELETE RESTRICT;
        ALTER TABLE performance_evaluation ADD COLUMN reviewer_employee_id uuid;
        ALTER TABLE performance_evaluation ADD CONSTRAINT fk_performance_evaluation_reviewer
            FOREIGN KEY (reviewer_employee_id) REFERENCES employee(id) ON DELETE SET NULL;

        -- performance_contribution back to the evaluation breakdown shape.
        DROP INDEX ix_performance_contribution_employee_period;
        DROP INDEX ix_performance_contribution_evaluation;
        DELETE FROM performance_contribution WHERE performance_evaluation_id IS NULL;
        ALTER TABLE performance_contribution ADD COLUMN kpi_definition_id uuid,
            ADD COLUMN evaluation_profile_item_id uuid,
            ADD COLUMN target_assignment_id uuid,
            ADD COLUMN parent_contribution_id uuid;
        ALTER TABLE performance_contribution ALTER COLUMN performance_evaluation_id SET NOT NULL;
        ALTER TABLE performance_evaluation ADD CONSTRAINT uq_performance_evaluation_identity
            UNIQUE (id, employee_id, evaluation_period_id);
        ALTER TABLE performance_contribution ADD CONSTRAINT fk_performance_contribution_evaluation_identity
            FOREIGN KEY (performance_evaluation_id, employee_id, evaluation_period_id)
            REFERENCES performance_evaluation(id, employee_id, evaluation_period_id);
        ALTER TABLE performance_contribution ADD CONSTRAINT fk_performance_contribution_kpi
            FOREIGN KEY (kpi_definition_id) REFERENCES kpi_definition(id) ON DELETE RESTRICT;
        ALTER TABLE performance_contribution ADD CONSTRAINT fk_performance_contribution_profile_item
            FOREIGN KEY (evaluation_profile_item_id) REFERENCES evaluation_profile_item(id) ON DELETE RESTRICT;
        ALTER TABLE performance_contribution ADD CONSTRAINT fk_performance_contribution_target
            FOREIGN KEY (target_assignment_id) REFERENCES target_assignment(id) ON DELETE RESTRICT;
        ALTER TABLE performance_contribution ADD CONSTRAINT fk_performance_contribution_parent
            FOREIGN KEY (parent_contribution_id) REFERENCES performance_contribution(id) ON DELETE CASCADE;
        -- NOT VALID: ledger rows written after Up carry neither breakdown link.
        ALTER TABLE performance_contribution ADD CONSTRAINT ck_performance_contribution_kind
            CHECK ((kpi_definition_id IS NOT NULL) <> (evaluation_profile_item_id IS NOT NULL)) NOT VALID;
        ALTER TABLE performance_contribution ADD CONSTRAINT ck_performance_contribution_kpi_level
            CHECK (
                kpi_definition_id IS NULL
                OR (
                    parent_contribution_id IS NOT NULL
                    AND target_assignment_id IS NOT NULL
                    AND target_value_snapshot IS NOT NULL
                    AND actual_value IS NOT NULL
                    AND weight_snapshot IS NULL
                )
            ) NOT VALID;
        ALTER TABLE performance_contribution ADD CONSTRAINT ck_performance_contribution_group_level
            CHECK (
                evaluation_profile_item_id IS NULL
                OR (
                    parent_contribution_id IS NULL
                    AND target_assignment_id IS NULL
                    AND target_value_snapshot IS NULL
                    AND actual_value IS NULL
                    AND weight_snapshot IS NOT NULL
                    AND weight_snapshot > 0
                    AND weight_snapshot <= 100
                )
            ) NOT VALID;
        CREATE UNIQUE INDEX uq_performance_contribution_kpi
            ON performance_contribution(performance_evaluation_id, kpi_definition_id)
            WHERE kpi_definition_id IS NOT NULL;
        CREATE UNIQUE INDEX uq_performance_contribution_group
            ON performance_contribution(performance_evaluation_id, evaluation_profile_item_id)
            WHERE evaluation_profile_item_id IS NOT NULL;
        CREATE INDEX ix_performance_contribution_parent ON performance_contribution(parent_contribution_id);
        CREATE INDEX ix_performance_contribution_target ON performance_contribution(target_assignment_id);

        -- actual_progress back to employee/KPI/period + reverse links.
        ALTER TABLE actual_progress ADD COLUMN employee_id uuid,
            ADD COLUMN kpi_definition_id uuid,
            ADD COLUMN evaluation_period_id uuid,
            ADD COLUMN source_item_id uuid,
            ADD COLUMN performance_contribution_id uuid;
        UPDATE actual_progress ap
        SET employee_id = t.employee_id,
            kpi_definition_id = t.kpi_definition_id,
            evaluation_period_id = COALESCE(
                (SELECT d.id FROM evaluation_period d
                 WHERE d.period_type = 'DAY' AND ap.progress_date BETWEEN d.start_date AND d.end_date
                 LIMIT 1),
                t.evaluation_period_id)
        FROM target_assignment t
        WHERE t.id = ap.target_assignment_id;
        DELETE FROM work_result_item WHERE actual_progress_id IN (SELECT id FROM actual_progress WHERE employee_id IS NULL);
        UPDATE performance_contribution SET actual_progress_id = NULL
        WHERE actual_progress_id IN (SELECT id FROM actual_progress WHERE employee_id IS NULL);
        DELETE FROM actual_progress WHERE employee_id IS NULL;
        UPDATE actual_progress ap
        SET source_item_id = s.item_id
        FROM (
            SELECT actual_progress_id, (array_agg(id ORDER BY id))[1] AS item_id
            FROM work_result_item GROUP BY actual_progress_id
        ) s
        WHERE s.actual_progress_id = ap.id;
        UPDATE actual_progress ap
        SET performance_contribution_id = s.contribution_id
        FROM (
            SELECT actual_progress_id, (array_agg(id ORDER BY id))[1] AS contribution_id
            FROM performance_contribution WHERE actual_progress_id IS NOT NULL GROUP BY actual_progress_id
        ) s
        WHERE s.actual_progress_id = ap.id;

        -- work_result_item back to KPI.
        ALTER TABLE work_result_item ADD COLUMN kpi_definition_id uuid;
        UPDATE work_result_item wri
        SET kpi_definition_id = t.kpi_definition_id
        FROM actual_progress ap JOIN target_assignment t ON t.id = ap.target_assignment_id
        WHERE ap.id = wri.actual_progress_id;

        ALTER TABLE performance_contribution DROP COLUMN actual_progress_id, DROP COLUMN risk_event_id;
        ALTER TABLE work_result_item DROP COLUMN actual_progress_id, DROP COLUMN employee_id;
        ALTER TABLE work_result_item ALTER COLUMN kpi_definition_id SET NOT NULL;
        ALTER TABLE work_result_item ADD CONSTRAINT fk_work_result_item_kpi
            FOREIGN KEY (kpi_definition_id) REFERENCES kpi_definition(id) ON DELETE RESTRICT;
        ALTER TABLE work_result_item ADD CONSTRAINT uq_work_result_item_kpi UNIQUE (verified_work_result_id, kpi_definition_id);
        CREATE INDEX ix_work_result_item_kpi ON work_result_item(kpi_definition_id);

        ALTER TABLE actual_progress DROP COLUMN target_assignment_id;
        ALTER TABLE actual_progress
            ALTER COLUMN employee_id SET NOT NULL,
            ALTER COLUMN kpi_definition_id SET NOT NULL,
            ALTER COLUMN evaluation_period_id SET NOT NULL;
        ALTER TABLE actual_progress ADD CONSTRAINT fk_actual_progress_employee
            FOREIGN KEY (employee_id) REFERENCES employee(id) ON DELETE RESTRICT;
        ALTER TABLE actual_progress ADD CONSTRAINT fk_actual_progress_kpi
            FOREIGN KEY (kpi_definition_id) REFERENCES kpi_definition(id) ON DELETE RESTRICT;
        ALTER TABLE actual_progress ADD CONSTRAINT fk_actual_progress_period
            FOREIGN KEY (evaluation_period_id) REFERENCES evaluation_period(id) ON DELETE RESTRICT;
        ALTER TABLE actual_progress ADD CONSTRAINT fk_actual_progress_source_item
            FOREIGN KEY (source_item_id) REFERENCES work_result_item(id) ON DELETE SET NULL;
        ALTER TABLE actual_progress ADD CONSTRAINT uq_actual_progress_source_item UNIQUE (source_item_id);
        ALTER TABLE actual_progress ADD CONSTRAINT fk_actual_progress_contribution
            FOREIGN KEY (performance_contribution_id) REFERENCES performance_contribution(id) ON DELETE SET NULL;
        CREATE INDEX ix_actual_progress_contribution ON actual_progress(performance_contribution_id);
        CREATE INDEX ix_actual_progress_employee_period ON actual_progress(employee_id, evaluation_period_id);
        CREATE INDEX ix_actual_progress_kpi_period ON actual_progress(kpi_definition_id, evaluation_period_id);

        -- Risk events: recorder link goes, verified-result invalidator comes back.
        ALTER TABLE risk_event DROP COLUMN recorded_by;
        ALTER TABLE verified_work_result ADD COLUMN invalidated_by_risk_event_id uuid;
        ALTER TABLE verified_work_result ADD CONSTRAINT fk_verified_work_result_invalidator
            FOREIGN KEY (invalidated_by_risk_event_id) REFERENCES risk_event(id) ON DELETE RESTRICT;
        ALTER TABLE verified_work_result ADD CONSTRAINT uq_verified_work_result_risk_event UNIQUE (invalidated_by_risk_event_id);

        -- Targets.
        ALTER TABLE target_assignment DROP COLUMN business_objective_id;
        ALTER TABLE target_assignment ADD COLUMN previous_version_id uuid,
            ADD COLUMN approved_by uuid,
            ADD COLUMN assigned_by uuid;
        ALTER TABLE target_assignment ADD CONSTRAINT fk_target_assignment_previous_version
            FOREIGN KEY (previous_version_id) REFERENCES target_assignment(id) ON DELETE RESTRICT;
        ALTER TABLE target_assignment ADD CONSTRAINT fk_target_assignment_approved_by
            FOREIGN KEY (approved_by) REFERENCES employee(id) ON DELETE SET NULL;
        ALTER TABLE target_assignment ADD CONSTRAINT fk_target_assignment_assigned_by
            FOREIGN KEY (assigned_by) REFERENCES employee(id) ON DELETE SET NULL;
        ALTER TABLE target_assignment ADD CONSTRAINT ck_target_assignment_version_link
            CHECK ((version = 1 AND previous_version_id IS NULL) OR (version > 1 AND previous_version_id IS NOT NULL)) NOT VALID;

        -- Profiles: position -> profile again (latest ACTIVE profile per position).
        ALTER TABLE job_position ADD COLUMN evaluation_profile_id uuid;
        UPDATE job_position jp
        SET evaluation_profile_id = ep.id
        FROM (
            SELECT DISTINCT ON (job_position_id) job_position_id, id
            FROM evaluation_profile
            ORDER BY job_position_id, (status = 'ACTIVE') DESC, effective_from DESC
        ) ep
        WHERE ep.job_position_id = jp.id;
        ALTER TABLE job_position ADD CONSTRAINT fk_job_position_evaluation_profile
            FOREIGN KEY (evaluation_profile_id) REFERENCES evaluation_profile(id) ON DELETE RESTRICT;
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM job_position WHERE evaluation_profile_id IS NULL) THEN
                ALTER TABLE job_position ALTER COLUMN evaluation_profile_id SET NOT NULL;
            END IF;
        END;
        $$;
        ALTER TABLE evaluation_profile DROP COLUMN job_position_id;

        -- Work: creator link goes.
        ALTER TABLE work DROP COLUMN created_by;

        -- Self-parents.
        ALTER TABLE performance_evaluation DROP COLUMN parent_evaluation_id;
        ALTER TABLE work DROP COLUMN parent_work_id;
        ALTER TABLE business_campaign DROP COLUMN parent_campaign_id;
        ALTER TABLE business_objective DROP COLUMN parent_objective_id;
        ALTER TABLE kpi_group DROP COLUMN parent_kpi_group_id;

        ALTER TABLE work ENABLE TRIGGER trg_work_updated_at;
        ALTER TABLE job_position ENABLE TRIGGER trg_job_position_updated_at;
        ALTER TABLE work_type ENABLE TRIGGER trg_work_type_updated_at;
        ALTER TABLE actual_progress ENABLE TRIGGER trg_actual_progress_updated_at;
        ALTER TABLE work_result_item ENABLE TRIGGER trg_work_result_item_updated_at;

        """;
}
