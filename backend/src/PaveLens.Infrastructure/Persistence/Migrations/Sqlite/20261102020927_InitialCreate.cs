using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaveLens.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Inspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RoadSegmentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ReferenceWidthM = table.Column<double>(type: "REAL", nullable: false),
                    CalibrationJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SegmentationModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReasoningModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SurveyedAreaM2 = table.Column<double>(type: "REAL", nullable: true),
                    Pci = table.Column<double>(type: "REAL", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProcessingTimeMs = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inspections_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Distresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InspectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    SourceConcept = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MaskJson = table.Column<string>(type: "TEXT", nullable: false),
                    MaskVertexCountRaw = table.Column<int>(type: "INTEGER", nullable: false),
                    DepthM = table.Column<double>(type: "REAL", nullable: true),
                    DepthSource = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CutAreaM2 = table.Column<double>(type: "REAL", nullable: true),
                    VolumeM3 = table.Column<double>(type: "REAL", nullable: true),
                    MassT = table.Column<double>(type: "REAL", nullable: true),
                    Diagnosis = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RecommendedAction = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    BoxHeight = table.Column<double>(type: "REAL", nullable: false),
                    BoxWidth = table.Column<double>(type: "REAL", nullable: false),
                    BoxX = table.Column<double>(type: "REAL", nullable: false),
                    BoxY = table.Column<double>(type: "REAL", nullable: false),
                    AreaM2 = table.Column<double>(type: "REAL", nullable: false),
                    Eccentricity = table.Column<double>(type: "REAL", nullable: false),
                    LengthM = table.Column<double>(type: "REAL", nullable: false),
                    OrientationDeg = table.Column<double>(type: "REAL", nullable: false),
                    PerimeterM = table.Column<double>(type: "REAL", nullable: false),
                    WidthM = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Distresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Distresses_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepairSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InspectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Tools = table.Column<string>(type: "TEXT", nullable: false),
                    Risk = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TargetCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepairSteps_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InspectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DistressId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Distresses_DistressId",
                        column: x => x.DistressId,
                        principalTable: "Distresses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_DistressId",
                table: "ConversationMessages",
                column: "DistressId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_InspectionId_DistressId_CreatedAt",
                table: "ConversationMessages",
                columns: new[] { "InspectionId", "DistressId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Distresses_InspectionId_Code",
                table: "Distresses",
                columns: new[] { "InspectionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_Sha256",
                table: "Images",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_CreatedAt",
                table: "Inspections",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_ImageId",
                table: "Inspections",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_RoadSegmentId",
                table: "Inspections",
                column: "RoadSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_Status",
                table: "Inspections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RepairSteps_InspectionId_Order",
                table: "RepairSteps",
                columns: new[] { "InspectionId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "RepairSteps");

            migrationBuilder.DropTable(
                name: "Distresses");

            migrationBuilder.DropTable(
                name: "Inspections");

            migrationBuilder.DropTable(
                name: "Images");
        }
    }
}
