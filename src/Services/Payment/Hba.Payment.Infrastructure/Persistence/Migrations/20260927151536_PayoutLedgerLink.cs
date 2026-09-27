using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayoutLedgerLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "payout_id",
                schema: "payment",
                table: "driver_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_driver_ledger_entries_payout_id",
                schema: "payment",
                table: "driver_ledger_entries",
                column: "payout_id",
                unique: true,
                filter: "kind = 2 AND payout_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_driver_ledger_entries_payout_id",
                schema: "payment",
                table: "driver_ledger_entries");

            migrationBuilder.DropColumn(
                name: "payout_id",
                schema: "payment",
                table: "driver_ledger_entries");
        }
    }
}
