using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentStatsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_created_at",
                schema: "payment",
                table: "payment_intents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_succeeded_at",
                schema: "payment",
                table: "payment_intents",
                column: "succeeded_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_intents_created_at",
                schema: "payment",
                table: "payment_intents");

            migrationBuilder.DropIndex(
                name: "IX_payment_intents_succeeded_at",
                schema: "payment",
                table: "payment_intents");
        }
    }
}
