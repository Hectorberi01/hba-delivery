using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Ride.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverEarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "driver_earning",
                schema: "ride",
                table: "rides",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "driver_earning",
                schema: "ride",
                table: "rides");
        }
    }
}
