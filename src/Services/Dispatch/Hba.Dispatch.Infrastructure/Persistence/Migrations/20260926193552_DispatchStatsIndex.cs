using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Dispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DispatchStatsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_offers_sent_at",
                schema: "dispatch",
                table: "offers",
                column: "sent_at");

            migrationBuilder.CreateIndex(
                name: "IX_dispatches_created_at",
                schema: "dispatch",
                table: "dispatches",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_offers_sent_at",
                schema: "dispatch",
                table: "offers");

            migrationBuilder.DropIndex(
                name: "IX_dispatches_created_at",
                schema: "dispatch",
                table: "dispatches");
        }
    }
}
