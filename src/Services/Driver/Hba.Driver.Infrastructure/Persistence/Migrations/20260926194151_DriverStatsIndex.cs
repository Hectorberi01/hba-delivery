using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Driver.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverStatsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_drivers_registered_at",
                schema: "driver",
                table: "drivers",
                column: "registered_at");

            migrationBuilder.CreateIndex(
                name: "IX_drivers_verified_at",
                schema: "driver",
                table: "drivers",
                column: "verified_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_drivers_registered_at",
                schema: "driver",
                table: "drivers");

            migrationBuilder.DropIndex(
                name: "IX_drivers_verified_at",
                schema: "driver",
                table: "drivers");
        }
    }
}
