using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ResourceSchedulingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing resources already have sequence/changeover rules applied to them; scaffolding
            // would have defaulted this to the CLR default (false) and silently dropped those rules
            // from every resource already in the database.
            migrationBuilder.AddColumn<bool>(
                name: "AppliesSequenceRules",
                table: "Resources",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "CapacityBasis",
                table: "Resources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "NominalConcurrentCapacity",
                table: "Resources",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            // ResourceSchedulingMode.Disjunctive = 1. Scaffolding would have written 0, which is not a
            // member of the enum, so every existing resource must be backfilled as Disjunctive - the
            // behaviour they already had.
            migrationBuilder.AddColumn<int>(
                name: "SchedulingMode",
                table: "Resources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliesSequenceRules",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "CapacityBasis",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "NominalConcurrentCapacity",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "SchedulingMode",
                table: "Resources");
        }
    }
}
