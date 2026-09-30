using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_requested_at",
                schema: "identity",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_scheduled_for",
                schema: "identity",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_deletion_scheduled_for",
                schema: "identity",
                table: "accounts",
                column: "deletion_scheduled_for",
                filter: "deletion_scheduled_for IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_deletion_scheduled_for",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "deletion_requested_at",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "deletion_scheduled_for",
                schema: "identity",
                table: "accounts");
        }
    }
}
