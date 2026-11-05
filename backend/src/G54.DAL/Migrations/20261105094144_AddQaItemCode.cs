using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddQaItemCode : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "qa_item_code_seq");

        migrationBuilder.AddColumn<long>(
            name: "code_no",
            table: "quality_flag",
            type: "bigint",
            nullable: false,
            defaultValueSql: "nextval('qa_item_code_seq')");

        migrationBuilder.AddColumn<long>(
            name: "code_no",
            table: "qa_sample",
            type: "bigint",
            nullable: false,
            defaultValueSql: "nextval('qa_item_code_seq')");

        migrationBuilder.CreateIndex(
            name: "uq_quality_flag_code_no",
            table: "quality_flag",
            column: "code_no",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "uq_qa_sample_code_no",
            table: "qa_sample",
            column: "code_no",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "uq_quality_flag_code_no",
            table: "quality_flag");

        migrationBuilder.DropIndex(
            name: "uq_qa_sample_code_no",
            table: "qa_sample");

        migrationBuilder.DropColumn(
            name: "code_no",
            table: "quality_flag");

        migrationBuilder.DropColumn(
            name: "code_no",
            table: "qa_sample");

        migrationBuilder.DropSequence(
            name: "qa_item_code_seq");
    }
}
