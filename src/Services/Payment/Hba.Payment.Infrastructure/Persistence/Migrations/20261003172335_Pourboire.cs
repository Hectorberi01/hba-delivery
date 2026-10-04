using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pourboire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "driver_id",
                schema: "payment",
                table: "payment_intents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "purpose",
                schema: "payment",
                table: "payment_intents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "driver_id",
                schema: "payment",
                table: "payment_intents");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "payment",
                table: "payment_intents");
        }
    }
}
