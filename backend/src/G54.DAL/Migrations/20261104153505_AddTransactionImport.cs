using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddTransactionImport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "import_batch",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                total_rows = table.Column<int>(type: "integer", nullable: false),
                imported_rows = table.Column<int>(type: "integer", nullable: false),
                duplicate_rows = table.Column<int>(type: "integer", nullable: false),
                error_rows = table.Column<int>(type: "integer", nullable: false),
                imported_by = table.Column<Guid>(type: "uuid", nullable: false),
                imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("import_batch_pkey", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "imported_transaction",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                transaction_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                product_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                denominator = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                refer_employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("imported_transaction_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_imported_transaction_batch",
                    column: x => x.import_batch_id,
                    principalTable: "import_batch",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_imported_transaction_employee",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_imported_transaction_batch",
            table: "imported_transaction",
            column: "import_batch_id");

        migrationBuilder.CreateIndex(
            name: "ix_imported_transaction_employee",
            table: "imported_transaction",
            column: "employee_id");

        migrationBuilder.CreateIndex(
            name: "uq_imported_transaction_code",
            table: "imported_transaction",
            column: "transaction_code",
            unique: true);
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action) VALUES
                ('TRANSACTION_IMPORT_MANAGE', 'Import giao dịch Core Banking', 'transaction-import', 'manage')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r, permission p
            WHERE r.code = 'ADMIN' AND p.code = 'TRANSACTION_IMPORT_MANAGE'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'TRANSACTION_IMPORT_MANAGE');
            DELETE FROM permission WHERE code = 'TRANSACTION_IMPORT_MANAGE';
            """);

        migrationBuilder.DropTable(
            name: "imported_transaction");

        migrationBuilder.DropTable(
            name: "import_batch");
    }
}
