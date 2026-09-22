using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "delivery");

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    PartnerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MerchantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PickupPointId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_landmark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    pickup_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pickup_contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pickup_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dropoff_latitude = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_longitude = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_landmark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    dropoff_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dropoff_contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    dropoff_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    recipient_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quote_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tariff_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    price_total_xof = table.Column<long>(type: "bigint", nullable: false),
                    price_base_xof = table.Column<long>(type: "bigint", nullable: false),
                    price_distance_xof = table.Column<long>(type: "bigint", nullable: false),
                    price_surge_xof = table.Column<long>(type: "bigint", nullable: false),
                    driver_earning_xof = table.Column<long>(type: "bigint", nullable: false),
                    distance_meters = table.Column<int>(type: "integer", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    quoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    driver_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    driver_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    driver_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    driver_vehicle_type = table.Column<int>(type: "integer", nullable: true),
                    driver_vehicle_plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CurrentOfferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    otp_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    otp_failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    PackageDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PackageWeightGrams = table.Column<int>(type: "integer", nullable: false),
                    PaymentIntentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PickupProofObjectKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DeliveryProofObjectKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClosureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArrivedAtPickupAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PickedUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "delivery",
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
                schema: "delivery",
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
                schema: "delivery",
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

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_CustomerId_CreatedAt",
                schema: "delivery",
                table: "deliveries",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_MerchantId_CreatedAt",
                schema: "delivery",
                table: "deliveries",
                columns: new[] { "MerchantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_PartnerId_ExternalOrderId",
                schema: "delivery",
                table: "deliveries",
                columns: new[] { "PartnerId", "ExternalOrderId" },
                unique: true,
                filter: "\"ExternalOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_Reference",
                schema: "delivery",
                table: "deliveries",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_Status",
                schema: "delivery",
                table: "deliveries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAt_NextAttemptAt",
                schema: "delivery",
                table: "outbox_messages",
                columns: new[] { "PublishedAt", "NextAttemptAt" },
                filter: "\"PublishedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "delivery");
        }
    }
}
