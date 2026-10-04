using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Ride.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverEarningEtPaiement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_intent_id",
                schema: "ride",
                table: "rides",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_intent_id",
                schema: "ride",
                table: "rides");
        }
    }
}
