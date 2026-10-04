using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetenueAnnulationEtMesureDegradee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cancellation_fee_basis_points",
                schema: "pricing",
                table: "tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "distance_is_degraded",
                schema: "pricing",
                table: "quotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_fee_basis_points",
                schema: "pricing",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "distance_is_degraded",
                schema: "pricing",
                table: "quotes");
        }
    }
}
