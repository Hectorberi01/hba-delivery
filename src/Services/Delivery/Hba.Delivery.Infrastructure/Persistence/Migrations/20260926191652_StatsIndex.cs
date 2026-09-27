using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_deliveries_CreatedAt",
                schema: "delivery",
                table: "deliveries",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_deliveries_CreatedAt",
                schema: "delivery",
                table: "deliveries");
        }
    }
}
