using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonalDataReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personal_data_reads",
                schema: "delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReaderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReaderRoles = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_data_reads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personal_data_reads_ReaderId_ReadAt",
                schema: "delivery",
                table: "personal_data_reads",
                columns: new[] { "ReaderId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_personal_data_reads_SubjectId_ReadAt",
                schema: "delivery",
                table: "personal_data_reads",
                columns: new[] { "SubjectId", "ReadAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personal_data_reads",
                schema: "delivery");
        }
    }
}
