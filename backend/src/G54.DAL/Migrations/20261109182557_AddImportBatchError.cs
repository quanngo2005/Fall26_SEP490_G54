using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddImportBatchError : Migration
{
    private static readonly string[] BatchRowColumns = ["import_batch_id", "row_number"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "import_batch_error",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                row_number = table.Column<int>(type: "integer", nullable: false),
                field = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("import_batch_error_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_import_batch_error_batch",
                    column: x => x.import_batch_id,
                    principalTable: "import_batch",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_import_batch_error_batch_row",
            table: "import_batch_error",
            columns: BatchRowColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "import_batch_error");
    }
}
