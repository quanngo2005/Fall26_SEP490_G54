using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Service rating import (UC29, UC32; docs/api/SERVICE_RATING_IMPORT_CONTRACT.md): <c>import_batch.file_type</c> tells the
/// transaction batches (every existing row) from the rating batches, <c>service_rating</c> holds one rating per transaction
/// code, and <c>service_rating_import:manage</c> is granted to the roles that hold <c>transaction-import:manage</c>
/// (SYSTEM_ADMIN, DEPT_MANAGER, BRANCH_MANAGER, VERIFIER) at scope All. The grants are idempotent.
/// </summary>
public partial class AddServiceRatingImport : Migration
{
    private static readonly string[] FileTypeIndexColumns = ["file_type", "imported_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "file_type",
            table: "import_batch",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "TRANSACTION");

        migrationBuilder.CreateTable(
            name: "service_rating",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                transaction_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                rating_date = table.Column<DateOnly>(type: "date", nullable: false),
                score = table.Column<short>(type: "smallint", nullable: false),
                customer_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("service_rating_pkey", x => x.id);
                table.CheckConstraint("ck_service_rating_score", "score BETWEEN 1 AND 5");
                table.ForeignKey(
                    name: "fk_service_rating_batch",
                    column: x => x.import_batch_id,
                    principalTable: "import_batch",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_service_rating_employee",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_import_batch_file_type",
            table: "import_batch",
            columns: FileTypeIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ix_service_rating_batch",
            table: "service_rating",
            column: "import_batch_id");

        migrationBuilder.CreateIndex(
            name: "ix_service_rating_employee",
            table: "service_rating",
            column: "employee_id");

        migrationBuilder.CreateIndex(
            name: "uq_service_rating_transaction_code",
            table: "service_rating",
            column: "transaction_code",
            unique: true);

        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('SERVICE_RATING_IMPORT_MANAGE', 'Import đánh giá dịch vụ', 'service_rating_import', 'manage')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r, permission p
            WHERE r.code IN ('SYSTEM_ADMIN', 'DEPT_MANAGER', 'BRANCH_MANAGER', 'VERIFIER')
              AND p.code = 'SERVICE_RATING_IMPORT_MANAGE'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'SERVICE_RATING_IMPORT_MANAGE');
            DELETE FROM permission WHERE code = 'SERVICE_RATING_IMPORT_MANAGE';
            """);

        migrationBuilder.DropTable(
            name: "service_rating");

        migrationBuilder.DropIndex(
            name: "ix_import_batch_file_type",
            table: "import_batch");

        migrationBuilder.DropColumn(
            name: "file_type",
            table: "import_batch");
    }
}
