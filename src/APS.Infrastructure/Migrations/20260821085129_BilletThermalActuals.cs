using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BilletThermalActuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MeasuredTemperatureC",
                table: "StrandMaterialActuals",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemperatureObservedOnUtc",
                table: "StrandMaterialActuals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThermalState",
                table: "StrandMaterialActuals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemperatureObservedOnUtc",
                table: "MaterialLots",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeasuredTemperatureC",
                table: "StrandMaterialActuals");

            migrationBuilder.DropColumn(
                name: "TemperatureObservedOnUtc",
                table: "StrandMaterialActuals");

            migrationBuilder.DropColumn(
                name: "ThermalState",
                table: "StrandMaterialActuals");

            migrationBuilder.DropColumn(
                name: "TemperatureObservedOnUtc",
                table: "MaterialLots");
        }
    }
}
