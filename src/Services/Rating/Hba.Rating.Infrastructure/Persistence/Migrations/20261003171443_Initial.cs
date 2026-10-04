using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Rating.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rating");

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "rating",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => new { x.Scope, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "rating",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "rating",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PartitionKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "personal_data_reads",
                schema: "rating",
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

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "rating",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mission_type = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mission_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAt_NextAttemptAt",
                schema: "rating",
                table: "outbox_messages",
                columns: new[] { "PublishedAt", "NextAttemptAt" },
                filter: "\"PublishedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_personal_data_reads_ReaderId_ReadAt",
                schema: "rating",
                table: "personal_data_reads",
                columns: new[] { "ReaderId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_personal_data_reads_SubjectId_ReadAt",
                schema: "rating",
                table: "personal_data_reads",
                columns: new[] { "SubjectId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_client_a_noter",
                schema: "rating",
                table: "reviews",
                columns: new[] { "customer_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_livreur",
                schema: "rating",
                table: "reviews",
                columns: new[] { "driver_id", "stars" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_mission",
                schema: "rating",
                table: "reviews",
                columns: new[] { "mission_type", "reference_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "rating");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "rating");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "rating");

            migrationBuilder.DropTable(
                name: "personal_data_reads",
                schema: "rating");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "rating");
        }
    }
}
