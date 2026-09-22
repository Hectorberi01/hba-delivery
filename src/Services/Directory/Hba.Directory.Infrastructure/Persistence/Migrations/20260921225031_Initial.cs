using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hba.Directory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "directory");

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "directory",
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
                schema: "directory",
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
                name: "merchants",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    average_preparation_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "directory",
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
                name: "customer_favorite_addresses",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    landmark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_favorite_addresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_favorite_addresses_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "directory",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "merchant_pickup_points",
                schema: "directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    landmark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    opening_hours = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_pickup_points", x => x.Id);
                    table.ForeignKey(
                        name: "FK_merchant_pickup_points_merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalSchema: "directory",
                        principalTable: "merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_favorite_addresses_CustomerId",
                schema: "directory",
                table: "customer_favorite_addresses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customers_phone",
                schema: "directory",
                table: "customers",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_pickup_points_MerchantId",
                schema: "directory",
                table: "merchant_pickup_points",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_merchants_is_active",
                schema: "directory",
                table: "merchants",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_merchants_legal_name",
                schema: "directory",
                table: "merchants",
                column: "legal_name");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAt_NextAttemptAt",
                schema: "directory",
                table: "outbox_messages",
                columns: new[] { "PublishedAt", "NextAttemptAt" },
                filter: "\"PublishedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_favorite_addresses",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "merchant_pickup_points",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "merchants",
                schema: "directory");
        }
    }
}
