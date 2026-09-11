using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingPlatform.Webhooks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_lease_id",
                schema: "webhooks",
                table: "outbox_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatch_lease_until",
                schema: "webhooks",
                table: "outbox_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_dispatch_lease_until",
                schema: "webhooks",
                table: "outbox_events",
                column: "dispatch_lease_until");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_events_dispatch_lease_until",
                schema: "webhooks",
                table: "outbox_events");

            migrationBuilder.DropColumn(
                name: "dispatch_lease_id",
                schema: "webhooks",
                table: "outbox_events");

            migrationBuilder.DropColumn(
                name: "dispatch_lease_until",
                schema: "webhooks",
                table: "outbox_events");
        }
    }
}
