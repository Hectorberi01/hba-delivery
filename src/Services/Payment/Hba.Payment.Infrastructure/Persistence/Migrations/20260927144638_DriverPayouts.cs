using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "amount",
                schema: "payment",
                table: "driver_ledger_entries",
                newName: "amount_xof");

            migrationBuilder.CreateTable(
                name: "payout_requests",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount_xof = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payout_requests_driver_id_requested_at",
                schema: "payment",
                table: "payout_requests",
                columns: new[] { "driver_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payout_requests_status_requested_at",
                schema: "payment",
                table: "payout_requests",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_requests_driver_en_cours",
                schema: "payment",
                table: "payout_requests",
                column: "driver_id",
                unique: true,
                filter: "status IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payout_requests",
                schema: "payment");

            migrationBuilder.RenameColumn(
                name: "amount_xof",
                schema: "payment",
                table: "driver_ledger_entries",
                newName: "amount");
        }
    }
}
