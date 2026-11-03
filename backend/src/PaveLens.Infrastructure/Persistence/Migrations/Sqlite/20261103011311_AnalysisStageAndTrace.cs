using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaveLens.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AnalysisStageAndTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "Inspections",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceJson",
                table: "Inspections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Stage",
                table: "Inspections");

            migrationBuilder.DropColumn(
                name: "TraceJson",
                table: "Inspections");
        }
    }
}
