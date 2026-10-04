using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Ride.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ride");

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "ride",
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
                schema: "ride",
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
                schema: "ride",
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
                schema: "ride",
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
                name: "rides",
                schema: "ride",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    driver_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: false),
                    destination_latitude = table.Column<double>(type: "double precision", nullable: false),
                    destination_longitude = table.Column<double>(type: "double precision", nullable: false),
                    vehicle_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    estimated_distance_meters = table.Column<int>(type: "integer", nullable: false),
                    estimated_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    estimated_price = table.Column<long>(type: "bigint", nullable: false),
                    final_price = table.Column<long>(type: "bigint", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    driver_assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    driver_arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rides", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAt_NextAttemptAt",
                schema: "ride",
                table: "outbox_messages",
                columns: new[] { "PublishedAt", "NextAttemptAt" },
                filter: "\"PublishedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_personal_data_reads_ReaderId_ReadAt",
                schema: "ride",
                table: "personal_data_reads",
                columns: new[] { "ReaderId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_personal_data_reads_SubjectId_ReadAt",
                schema: "ride",
                table: "personal_data_reads",
                columns: new[] { "SubjectId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_rides_customer_id_requested_at",
                schema: "ride",
                table: "rides",
                columns: new[] { "customer_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rides_chauffeur_en_cours",
                schema: "ride",
                table: "rides",
                columns: new[] { "driver_id", "status" },
                filter: "driver_id IS NOT NULL AND status = ANY (ARRAY[1, 2, 3, 4, 5, 6])");

            migrationBuilder.CreateIndex(
                name: "ix_rides_customer_en_cours",
                schema: "ride",
                table: "rides",
                columns: new[] { "customer_id", "status" },
                filter: "status = ANY (ARRAY[1, 2, 3, 4, 5, 6])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "ride");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "ride");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ride");

            migrationBuilder.DropTable(
                name: "personal_data_reads",
                schema: "ride");

            migrationBuilder.DropTable(
                name: "rides",
                schema: "ride");
        }
    }
}
