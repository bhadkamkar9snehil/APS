using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations;

/// <summary>
/// Adds planner-managed delivery flexibility to current demand state and immutable Plan Version demand
/// evidence. Existing rows remain Standard with no explicit tolerance, preserving previous behavior.
/// </summary>
[DbContext(typeof(ApsDbContext))]
[Migration("20260904203000_OrderServiceWindows")]
public sealed class OrderServiceWindows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ServiceCommitment",
            table: "SalesOrderDemandStates",
            type: "INTEGER",
            nullable: false,
            defaultValue: 2);
        migrationBuilder.AddColumn<DateTime>(
            name: "EarliestAcceptableDeliveryDate",
            table: "SalesOrderDemandStates",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "LatestAcceptableDeliveryDate",
            table: "SalesOrderDemandStates",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ServiceCommitment",
            table: "PlanDemandSnapshots",
            type: "INTEGER",
            nullable: false,
            defaultValue: 2);
        migrationBuilder.AddColumn<DateTime>(
            name: "EarliestAcceptableDeliveryDate",
            table: "PlanDemandSnapshots",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "LatestAcceptableDeliveryDate",
            table: "PlanDemandSnapshots",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "ProductionEarliestAcceptableDate",
            table: "PlanDemandSnapshots",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "ProductionLatestAcceptableDate",
            table: "PlanDemandSnapshots",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ServiceCommitment", table: "SalesOrderDemandStates");
        migrationBuilder.DropColumn(name: "EarliestAcceptableDeliveryDate", table: "SalesOrderDemandStates");
        migrationBuilder.DropColumn(name: "LatestAcceptableDeliveryDate", table: "SalesOrderDemandStates");

        migrationBuilder.DropColumn(name: "ServiceCommitment", table: "PlanDemandSnapshots");
        migrationBuilder.DropColumn(name: "EarliestAcceptableDeliveryDate", table: "PlanDemandSnapshots");
        migrationBuilder.DropColumn(name: "LatestAcceptableDeliveryDate", table: "PlanDemandSnapshots");
        migrationBuilder.DropColumn(name: "ProductionEarliestAcceptableDate", table: "PlanDemandSnapshots");
        migrationBuilder.DropColumn(name: "ProductionLatestAcceptableDate", table: "PlanDemandSnapshots");
    }
}
