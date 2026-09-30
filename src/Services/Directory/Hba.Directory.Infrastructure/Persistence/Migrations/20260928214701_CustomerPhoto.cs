using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Directory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "photo_media_id",
                schema: "directory",
                table: "customers",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "photo_media_id",
                schema: "directory",
                table: "customers");
        }
    }
}
