using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "whatsapp_opt_in",
                schema: "identity",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "whatsapp_opt_in_at",
                schema: "identity",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "whatsapp_opt_in",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "whatsapp_opt_in_at",
                schema: "identity",
                table: "accounts");
        }
    }
}
