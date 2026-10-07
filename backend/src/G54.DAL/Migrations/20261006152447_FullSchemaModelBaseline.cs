using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class FullSchemaModelBaseline : Migration
{
    /// <inheritdoc />
    // InitialProgestSchema and AuthLoginSupport already create this schema.
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
