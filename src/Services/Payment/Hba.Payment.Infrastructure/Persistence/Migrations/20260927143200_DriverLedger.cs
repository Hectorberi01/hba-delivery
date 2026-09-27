using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "driver_ledger_entries",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_driver_ledger_entries_delivery_id",
                schema: "payment",
                table: "driver_ledger_entries",
                column: "delivery_id",
                unique: true,
                filter: "kind = 1 AND delivery_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_driver_ledger_entries_driver_id_occurred_at",
                schema: "payment",
                table: "driver_ledger_entries",
                columns: new[] { "driver_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_ledger_entries",
                schema: "payment");
        }
    }
}
