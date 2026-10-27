using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Maps the PostgreSQL system column <c>xmin</c> of <c>task_handover</c> as the optimistic-concurrency token.
/// The column already exists on every table, so there is nothing to create: this migration only records the
/// model change in the snapshot (scaffolded <c>AddColumn xmin</c> would fail on a real database).
/// </summary>
public partial class AddTaskHandoverXminConcurrencyToken : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
