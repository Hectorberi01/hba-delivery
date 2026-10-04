using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetenueAnnulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cancellation_fee_xof",
                schema: "delivery",
                table: "deliveries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_fee_xof",
                schema: "delivery",
                table: "deliveries");
        }
    }
}
