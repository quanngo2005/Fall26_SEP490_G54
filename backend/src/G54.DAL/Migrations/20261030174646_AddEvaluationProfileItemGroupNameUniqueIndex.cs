using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Group names are unique among siblings, case-insensitively, so two concurrent requests cannot both add the same
/// group (the service only checks before it inserts). One index covers the root groups of a profile and one the
/// groups under a parent: a single index over <c>parent_item_id</c> would not cover the roots, because NULLs never
/// compare equal. The case-insensitive expression is not expressible in the EF model, so the indexes are created
/// in SQL and the model snapshot is unchanged.
/// Siblings that already share a name are kept and told apart by an id suffix on all but the first one.
/// The weight-sum constraint triggers are deferred, and an index cannot be created while the clean-up leaves
/// trigger events pending, so the constraints are made immediate first (as the tree restructuring migration does).
/// </summary>
public partial class AddEvaluationProfileItemGroupNameUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET CONSTRAINTS ALL IMMEDIATE;

            WITH ranked AS (
                SELECT id,
                       ROW_NUMBER() OVER (
                           PARTITION BY evaluation_profile_id, parent_item_id, UPPER(name)
                           ORDER BY display_order, id) AS position
                FROM evaluation_profile_item
                WHERE name IS NOT NULL)
            UPDATE evaluation_profile_item item
            SET name = LEFT(item.name, 180) || ' [' || LEFT(item.id::text, 8) || ']',
                updated_at = now()
            FROM ranked
            WHERE ranked.id = item.id AND ranked.position > 1;

            CREATE UNIQUE INDEX uq_evaluation_profile_item_root_group_name
                ON evaluation_profile_item (evaluation_profile_id, UPPER(name))
                WHERE parent_item_id IS NULL AND name IS NOT NULL;

            CREATE UNIQUE INDEX uq_evaluation_profile_item_child_group_name
                ON evaluation_profile_item (parent_item_id, UPPER(name))
                WHERE parent_item_id IS NOT NULL AND name IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS uq_evaluation_profile_item_child_group_name;
            DROP INDEX IF EXISTS uq_evaluation_profile_item_root_group_name;
            """);
    }
}
