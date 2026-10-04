using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetenueAnnulationEtMesureDegradee0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cancellation_fee",
                schema: "pricing",
                table: "quotes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_fee",
                schema: "pricing",
                table: "quotes");
        }
    }
}
