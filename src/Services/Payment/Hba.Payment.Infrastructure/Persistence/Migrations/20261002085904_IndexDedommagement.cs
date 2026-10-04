using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexDedommagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_driver_ledger_mission",
                schema: "payment",
                table: "driver_ledger_entries");

            migrationBuilder.CreateIndex(
                name: "ux_driver_ledger_compensation",
                schema: "payment",
                table: "driver_ledger_entries",
                columns: new[] { "service_type", "reference_id" },
                unique: true,
                filter: "kind = 3 AND reference_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_driver_ledger_compensation",
                schema: "payment",
                table: "driver_ledger_entries");

            migrationBuilder.CreateIndex(
                name: "ux_driver_ledger_mission",
                schema: "payment",
                table: "driver_ledger_entries",
                columns: new[] { "service_type", "reference_id" },
                unique: true,
                filter: "kind = 1 AND reference_id IS NOT NULL");
        }
    }
}
