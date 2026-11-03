using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaveLens.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class CascadeDistressQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMessages_Distresses_DistressId",
                table: "ConversationMessages");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMessages_Distresses_DistressId",
                table: "ConversationMessages",
                column: "DistressId",
                principalTable: "Distresses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMessages_Distresses_DistressId",
                table: "ConversationMessages");

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMessages_Distresses_DistressId",
                table: "ConversationMessages",
                column: "DistressId",
                principalTable: "Distresses",
                principalColumn: "Id");
        }
    }
}
