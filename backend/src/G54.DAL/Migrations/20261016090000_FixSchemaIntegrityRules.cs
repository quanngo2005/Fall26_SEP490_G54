using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Fixes trigger-level integrity gaps in the v0.10 schema:
/// hierarchy cycles, parent target edits bypassing the SUM rule,
/// parent period date edits, and edits to approved verifications.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261016090000_FixSchemaIntegrityRules")]
public partial class FixSchemaIntegrityRules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(UpSql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DownSql);
    }

    private const string UpSql = """
-- 1. Cycle-safe hierarchy helpers (UNION stops on revisited rows).
CREATE OR REPLACE FUNCTION evaluation_period_is_descendant(child_id UUID, ancestor_id UUID)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    WITH RECURSIVE p AS (
        SELECT id, parent_period_id FROM evaluation_period WHERE id = child_id
        UNION
        SELECT e.id, e.parent_period_id FROM evaluation_period e JOIN p ON e.id = p.parent_period_id
    )
    SELECT EXISTS (SELECT 1 FROM p WHERE id = ancestor_id);
$$;

CREATE OR REPLACE FUNCTION department_is_descendant(child_department UUID, ancestor_department UUID)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    WITH RECURSIVE d AS (
        SELECT id, parent_department_id FROM department WHERE id = child_department
        UNION
        SELECT x.id, x.parent_department_id FROM department x JOIN d ON x.id = d.parent_department_id
    )
    SELECT EXISTS (SELECT 1 FROM d WHERE id = ancestor_department);
$$;

-- 1b. Reject department parent cycles.
CREATE OR REPLACE FUNCTION prevent_department_cycle()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.parent_department_id IS NOT NULL
       AND department_is_descendant(NEW.parent_department_id, NEW.id) THEN
        RAISE EXCEPTION 'Department parent relationship would create a cycle';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_prevent_department_cycle ON department;
CREATE TRIGGER trg_prevent_department_cycle
BEFORE INSERT OR UPDATE OF parent_department_id ON department
FOR EACH ROW EXECUTE FUNCTION prevent_department_cycle();

-- 2. SUM rule is checked both for the row's parent and for the row as a parent.
CREATE OR REPLACE FUNCTION validate_target_children_sum()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    check_id UUID;
    p_kpi kpi_aggregation_type;
    child_count INTEGER;
    child_sum NUMERIC(30,4);
    parent_target NUMERIC(30,4);
    p_status target_assignment_status;
BEGIN
    FOREACH check_id IN ARRAY ARRAY[
        COALESCE(NEW.parent_assignment_id, OLD.parent_assignment_id),
        CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE NEW.id END
    ] LOOP
        CONTINUE WHEN check_id IS NULL;

        SELECT k.aggregation_type, t.target_value, t.status
        INTO p_kpi, parent_target, p_status
        FROM target_assignment t
        JOIN kpi_definition k ON k.id = t.kpi_definition_id
        WHERE t.id = check_id;

        CONTINUE WHEN NOT FOUND OR p_kpi <> 'SUM' OR p_status <> 'ACTIVE';

        SELECT COUNT(*), COALESCE(SUM(target_value), 0)
        INTO child_count, child_sum
        FROM target_assignment
        WHERE parent_assignment_id = check_id AND status = 'ACTIVE';

        IF child_count > 0 AND child_sum <> parent_target THEN
            RAISE EXCEPTION 'Active child targets (%) must sum to parent target (%) for SUM KPI on TargetAssignment %',
                child_sum, parent_target, check_id;
        END IF;
    END LOOP;

    RETURN NULL;
END;
$$;

-- 3. Parent period date changes must still contain all child periods.
CREATE OR REPLACE FUNCTION validate_evaluation_period()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    p evaluation_period%ROWTYPE;
BEGIN
    IF EXISTS (
        SELECT 1 FROM evaluation_period c
        WHERE c.parent_period_id = NEW.id
          AND (c.start_date < NEW.start_date OR c.end_date > NEW.end_date)
    ) THEN
        RAISE EXCEPTION 'Evaluation period % must contain all of its child periods', NEW.id;
    END IF;

    IF NEW.period_type = 'YEAR' THEN
        IF NEW.parent_period_id IS NOT NULL THEN
            RAISE EXCEPTION 'YEAR period cannot have a parent';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.parent_period_id IS NULL THEN
        RAISE EXCEPTION 'Non-YEAR evaluation period must have a parent';
    END IF;

    SELECT * INTO p FROM evaluation_period WHERE id = NEW.parent_period_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Parent evaluation period % not found', NEW.parent_period_id;
    END IF;

    IF NEW.period_type = 'HALF_YEAR' AND p.period_type <> 'YEAR' THEN
        RAISE EXCEPTION 'HALF_YEAR must be directly under YEAR';
    ELSIF NEW.period_type = 'QUARTER' AND p.period_type <> 'HALF_YEAR' THEN
        RAISE EXCEPTION 'QUARTER must be directly under HALF_YEAR';
    ELSIF NEW.period_type = 'MONTH' AND p.period_type <> 'QUARTER' THEN
        RAISE EXCEPTION 'MONTH must be directly under QUARTER';
    ELSIF NEW.period_type = 'DAY' AND p.period_type <> 'MONTH' THEN
        RAISE EXCEPTION 'DAY must be directly under MONTH';
    END IF;

    IF NEW.start_date < p.start_date OR NEW.end_date > p.end_date THEN
        RAISE EXCEPTION 'Child period dates must be contained within parent period';
    END IF;

    RETURN NEW;
END;
$$;

-- 4. Approved verifications may still be edited (e.g. comment) but not re-decided.
CREATE OR REPLACE FUNCTION enforce_verification_lifecycle()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    submission_status_value submission_status;
    approved_exists BOOLEAN;
BEGIN
    IF TG_OP = 'UPDATE' AND NEW.decision = OLD.decision
       AND NEW.result_submission_id = OLD.result_submission_id THEN
        RETURN NEW;
    END IF;

    IF TG_OP = 'UPDATE' AND OLD.decision = 'APPROVED' THEN
        RAISE EXCEPTION 'Approved Verification decision cannot be changed';
    END IF;

    SELECT status INTO submission_status_value
    FROM result_submission WHERE id = NEW.result_submission_id;

    SELECT EXISTS (
        SELECT 1 FROM verification v
        WHERE v.result_submission_id = NEW.result_submission_id
          AND v.decision = 'APPROVED'
          AND v.id <> NEW.id
    ) INTO approved_exists;

    IF approved_exists OR submission_status_value = 'APPROVED' THEN
        RAISE EXCEPTION 'Approved ResultSubmission is closed and cannot be verified again';
    END IF;

    RETURN NEW;
END;
$$;
""";

    private const string DownSql = """
DROP TRIGGER IF EXISTS trg_prevent_department_cycle ON department;
DROP FUNCTION IF EXISTS prevent_department_cycle();

CREATE OR REPLACE FUNCTION evaluation_period_is_descendant(child_id UUID, ancestor_id UUID)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    WITH RECURSIVE p AS (
        SELECT id, parent_period_id FROM evaluation_period WHERE id = child_id
        UNION ALL
        SELECT e.id, e.parent_period_id FROM evaluation_period e JOIN p ON e.id = p.parent_period_id
    )
    SELECT EXISTS (SELECT 1 FROM p WHERE id = ancestor_id);
$$;

CREATE OR REPLACE FUNCTION department_is_descendant(child_department UUID, ancestor_department UUID)
RETURNS BOOLEAN LANGUAGE sql STABLE AS $$
    WITH RECURSIVE d AS (
        SELECT id, parent_department_id FROM department WHERE id = child_department
        UNION ALL
        SELECT x.id, x.parent_department_id FROM department x JOIN d ON x.id = d.parent_department_id
    )
    SELECT EXISTS (SELECT 1 FROM d WHERE id = ancestor_department);
$$;

CREATE OR REPLACE FUNCTION validate_target_children_sum()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    parent_id UUID;
    p_kpi kpi_aggregation_type;
    child_count INTEGER;
    child_sum NUMERIC(30,4);
    parent_target NUMERIC(30,4);
    p_status target_assignment_status;
BEGIN
    parent_id := COALESCE(NEW.parent_assignment_id, OLD.parent_assignment_id);
    IF parent_id IS NULL THEN
        RETURN NULL;
    END IF;

    SELECT k.aggregation_type, t.target_value, t.status
    INTO p_kpi, parent_target, p_status
    FROM target_assignment t
    JOIN kpi_definition k ON k.id = t.kpi_definition_id
    WHERE t.id = parent_id;

    IF NOT FOUND OR p_kpi <> 'SUM' OR p_status <> 'ACTIVE' THEN
        RETURN NULL;
    END IF;

    SELECT COUNT(*), COALESCE(SUM(target_value), 0)
    INTO child_count, child_sum
    FROM target_assignment
    WHERE parent_assignment_id = parent_id AND status = 'ACTIVE';

    IF child_count > 0 AND child_sum <> parent_target THEN
        RAISE EXCEPTION 'Active child targets (%) must sum to parent target (%) for SUM KPI on TargetAssignment %',
            child_sum, parent_target, parent_id;
    END IF;

    RETURN NULL;
END;
$$;

CREATE OR REPLACE FUNCTION validate_evaluation_period()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    p evaluation_period%ROWTYPE;
BEGIN
    IF NEW.period_type = 'YEAR' THEN
        IF NEW.parent_period_id IS NOT NULL THEN
            RAISE EXCEPTION 'YEAR period cannot have a parent';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.parent_period_id IS NULL THEN
        RAISE EXCEPTION 'Non-YEAR evaluation period must have a parent';
    END IF;

    SELECT * INTO p FROM evaluation_period WHERE id = NEW.parent_period_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Parent evaluation period % not found', NEW.parent_period_id;
    END IF;

    IF NEW.period_type = 'HALF_YEAR' AND p.period_type <> 'YEAR' THEN
        RAISE EXCEPTION 'HALF_YEAR must be directly under YEAR';
    ELSIF NEW.period_type = 'QUARTER' AND p.period_type <> 'HALF_YEAR' THEN
        RAISE EXCEPTION 'QUARTER must be directly under HALF_YEAR';
    ELSIF NEW.period_type = 'MONTH' AND p.period_type <> 'QUARTER' THEN
        RAISE EXCEPTION 'MONTH must be directly under QUARTER';
    ELSIF NEW.period_type = 'DAY' AND p.period_type <> 'MONTH' THEN
        RAISE EXCEPTION 'DAY must be directly under MONTH';
    END IF;

    IF NEW.start_date < p.start_date OR NEW.end_date > p.end_date THEN
        RAISE EXCEPTION 'Child period dates must be contained within parent period';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION enforce_verification_lifecycle()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    submission_status_value submission_status;
    approved_exists BOOLEAN;
BEGIN
    SELECT status INTO submission_status_value
    FROM result_submission WHERE id = NEW.result_submission_id;

    SELECT EXISTS (
        SELECT 1 FROM verification v
        WHERE v.result_submission_id = NEW.result_submission_id
          AND v.decision = 'APPROVED'
          AND v.id <> NEW.id
    ) INTO approved_exists;

    IF approved_exists OR submission_status_value = 'APPROVED' THEN
        RAISE EXCEPTION 'Approved ResultSubmission is closed and cannot be verified again';
    END IF;

    RETURN NEW;
END;
$$;
""";
}
