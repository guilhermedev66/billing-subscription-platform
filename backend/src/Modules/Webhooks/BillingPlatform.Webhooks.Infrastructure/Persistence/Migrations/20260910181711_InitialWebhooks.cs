using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingPlatform.Webhooks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "webhooks");

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbox_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "endpoints",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    secret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    event_types_json = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_entries",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    raw_body = table.Column<byte[]>(type: "bytea", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_body = table.Column<byte[]>(type: "bytea", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "projections",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    raw_body = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_outbox_event_id_endpoint_id_attempt_number",
                schema: "webhooks",
                table: "delivery_attempts",
                columns: new[] { "outbox_event_id", "endpoint_id", "attempt_number" });

            migrationBuilder.CreateIndex(
                name: "ix_endpoints_organization_id_url",
                schema: "webhooks",
                table: "endpoints",
                columns: new[] { "organization_id", "url" });

            migrationBuilder.CreateIndex(
                name: "ux_webhook_inbox_organization_event",
                schema: "webhooks",
                table: "inbox_entries",
                columns: new[] { "organization_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_organization_id_dispatched_at_available_at",
                schema: "webhooks",
                table: "outbox_events",
                columns: new[] { "organization_id", "dispatched_at", "available_at" });

            migrationBuilder.CreateIndex(
                name: "ux_webhook_projection_aggregate",
                schema: "webhooks",
                table: "projections",
                columns: new[] { "organization_id", "aggregate_type", "aggregate_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "endpoints",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "inbox_entries",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "projections",
                schema: "webhooks");
        }
    }
}
