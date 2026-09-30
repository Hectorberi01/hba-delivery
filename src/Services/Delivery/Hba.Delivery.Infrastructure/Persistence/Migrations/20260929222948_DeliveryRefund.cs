using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RefundPartial",
                schema: "delivery",
                table: "deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                schema: "delivery",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundPartial",
                schema: "delivery",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                schema: "delivery",
                table: "deliveries");
        }
    }
}
