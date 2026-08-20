using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanningScenarioMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanningScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioCode = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsBaseline = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningScenarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceScenarioOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanningScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperatingState = table.Column<int>(type: "INTEGER", nullable: false),
                    CapacityFactorPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    EffectiveFromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestrictedProcessOperationType = table.Column<int>(type: "INTEGER", nullable: true),
                    AllowedGradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    ForbiddenGradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceScenarioOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceScenarioOverrides_PlanningScenarios_PlanningScenarioId",
                        column: x => x.PlanningScenarioId,
                        principalTable: "PlanningScenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningScenarios_ScenarioCode",
                table: "PlanningScenarios",
                column: "ScenarioCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceScenarioOverrides_PlanningScenarioId",
                table: "ResourceScenarioOverrides",
                column: "PlanningScenarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceScenarioOverrides");

            migrationBuilder.DropTable(
                name: "PlanningScenarios");
        }
    }
}
