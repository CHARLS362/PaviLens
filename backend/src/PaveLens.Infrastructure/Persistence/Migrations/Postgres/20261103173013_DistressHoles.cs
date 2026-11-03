using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaveLens.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class DistressHoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HolesJson",
                table: "Distresses",
                type: "text",
                nullable: false,
                defaultValue: "[]"); // distresses measured before this column existed were stored without holes
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HolesJson",
                table: "Distresses");
        }
    }
}
