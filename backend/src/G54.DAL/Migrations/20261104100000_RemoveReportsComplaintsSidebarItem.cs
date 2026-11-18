using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// The sidebar item <c>reportsComplaints</c> had no route and is gone from the catalog; <c>appeals</c> is the working
/// entry. Removes the retired id (and any route override saved for it) from the saved configurations so an administrator
/// can still save them. Data-only and idempotent; no schema change.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261104100000_RemoveReportsComplaintsSidebarItem")]
public partial class RemoveReportsComplaintsSidebarItem : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE sidebar_configurations
            SET item_ids = array_remove(item_ids, 'reportsComplaints'),
                routes = routes - 'reportsComplaints'
            WHERE 'reportsComplaints' = ANY(item_ids)
               OR jsonb_exists(routes, 'reportsComplaints');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The retired item cannot be restored: which configurations held it is not recorded, and the catalog no longer
        // lists it. Nothing to undo.
    }
}
