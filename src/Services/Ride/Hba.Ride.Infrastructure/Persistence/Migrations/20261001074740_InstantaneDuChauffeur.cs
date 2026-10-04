using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Ride.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstantaneDuChauffeur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "driver_display_id",
                schema: "ride",
                table: "rides",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_display_name",
                schema: "ride",
                table: "rides",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_phone",
                schema: "ride",
                table: "rides",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_vehicle_brand",
                schema: "ride",
                table: "rides",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_vehicle_color",
                schema: "ride",
                table: "rides",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_vehicle_model",
                schema: "ride",
                table: "rides",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_vehicle_plate",
                schema: "ride",
                table: "rides",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "driver_vehicle_type",
                schema: "ride",
                table: "rides",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "driver_display_id",
                schema: "ride",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "driver_display_name",
                schema: "ride",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "driver_phone",
                schema: "ride",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "driver_vehicle_brand",
                schema: "ride",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "driver_vehicle_color",
                schema: "ride",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "driver_vehicle_model",
                schema: "ride",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "driver_vehicle_plate",
                schema: "ride",
                table: "rides");

            migrationBuilder.DropColumn(
                name: "driver_vehicle_type",
                schema: "ride",
                table: "rides");
        }
    }
}
