using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Driver.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "capabilities",
                schema: "driver",
                table: "drivers",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<string>(
                name: "vehicle_brand",
                schema: "driver",
                table: "drivers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "vehicle_color",
                schema: "driver",
                table: "drivers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "vehicle_model",
                schema: "driver",
                table: "drivers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "vehicle_passenger_capacity",
                schema: "driver",
                table: "drivers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capabilities",
                schema: "driver",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "vehicle_brand",
                schema: "driver",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "vehicle_color",
                schema: "driver",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "vehicle_model",
                schema: "driver",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "vehicle_passenger_capacity",
                schema: "driver",
                table: "drivers");
        }
    }
}
