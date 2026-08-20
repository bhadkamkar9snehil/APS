using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanVersionRouteOperationDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RouteOperationDecisionsJson",
                table: "PlanVersionStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouteCode",
                table: "PlanOperationSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouteSequenceNumber",
                table: "PlanOperationSnapshots",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RouteOperationDecisionsJson",
                table: "PlanVersionStates");

            migrationBuilder.DropColumn(
                name: "RouteCode",
                table: "PlanOperationSnapshots");

            migrationBuilder.DropColumn(
                name: "RouteSequenceNumber",
                table: "PlanOperationSnapshots");
        }
    }
}
