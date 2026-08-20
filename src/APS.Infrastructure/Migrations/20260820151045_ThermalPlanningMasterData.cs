using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ThermalPlanningMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GradeProcessTemperatureRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SteelGradeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumEntryTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetEntryTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumEntryTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumExitTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetExitTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumExitTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumHoldingMinutesAfterExit = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeProcessTemperatureRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeProcessTemperatureRequirements_SteelGrades_SteelGradeId",
                        column: x => x.SteelGradeId,
                        principalTable: "SteelGrades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceTemperatureCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumAchievableExitTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    NominalExitTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumAchievableExitTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumHeatingRateCPerMinute = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    NominalTemperatureLossCPerMinuteWhileHolding = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CanCorrectTemperature = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTemperatureCapabilities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GradeProcessTemperatureRequirements_SteelGradeId_ProcessOperationType",
                table: "GradeProcessTemperatureRequirements",
                columns: new[] { "SteelGradeId", "ProcessOperationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTemperatureCapabilities_ResourceId_ProcessOperationType",
                table: "ResourceTemperatureCapabilities",
                columns: new[] { "ResourceId", "ProcessOperationType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradeProcessTemperatureRequirements");

            migrationBuilder.DropTable(
                name: "ResourceTemperatureCapabilities");
        }
    }
}
