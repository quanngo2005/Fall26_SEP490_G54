using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class UpdateTargetChildrenSumTriggerBySplitDimension : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
-- G11 (Phase 2.1): Update the validate_target_children_sum trigger to group children by split_dimension.
-- This allows Branch-Year to have both ORG children (Dept-Year) and PERIOD children (Branch-Quarter)
-- without causing a false sum mismatch, as each split_dimension group is validated separately.
CREATE OR REPLACE FUNCTION validate_target_children_sum()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    check_id UUID;
    p_kpi kpi_aggregation_type;
    child_sum_by_dim RECORD;
    parent_target NUMERIC(30,4);
    p_status target_assignment_status;
    split_dim target_split_dimension;
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

        -- Check each split_dimension group separately
        FOR child_sum_by_dim IN
            SELECT COALESCE(split_dimension::text, 'NONE') as split_dim,
                   COUNT(*) as child_count,
                   COALESCE(SUM(target_value), 0) as total
            FROM target_assignment
            WHERE parent_assignment_id = check_id AND status = 'ACTIVE'
            GROUP BY split_dimension
        LOOP
            IF child_sum_by_dim.child_count > 0 AND child_sum_by_dim.total <> parent_target THEN
                RAISE EXCEPTION 'Active child targets (%) for split_dimension % must sum to parent target (%) for SUM KPI on TargetAssignment %',
                    child_sum_by_dim.total, child_sum_by_dim.split_dim, parent_target, check_id;
            END IF;
        END LOOP;
    END LOOP;

    RETURN NULL;
END;
$$;
");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
-- Revert to the previous validate_target_children_sum trigger (sums all children without grouping)
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
");
    }
}
