using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaveLens.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ReportLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Inspections",
                type: "TEXT",
                maxLength: 8,
                nullable: false,
                defaultValue: "Es"); // reports written before this column existed are in Spanish
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Inspections");
        }
    }
}
