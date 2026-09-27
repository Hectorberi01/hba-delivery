using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Driver.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "profile_photo_key",
                schema: "driver",
                table: "drivers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                schema: "driver",
                table: "drivers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "driver_documents",
                schema: "driver",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_driver_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_driver_documents_drivers_driver_id",
                        column: x => x.driver_id,
                        principalSchema: "driver",
                        principalTable: "drivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_driver_documents_driver_id_type",
                schema: "driver",
                table: "driver_documents",
                columns: new[] { "driver_id", "type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_documents",
                schema: "driver");

            migrationBuilder.DropColumn(
                name: "profile_photo_key",
                schema: "driver",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                schema: "driver",
                table: "drivers");
        }
    }
}
