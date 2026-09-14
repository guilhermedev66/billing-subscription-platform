using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingPlatform.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "mrr_movements",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    before_annualized_cents = table.Column<long>(type: "bigint", nullable: false),
                    after_annualized_cents = table.Column<long>(type: "bigint", nullable: false),
                    delta_annualized_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mrr_movements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_events",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subscription_version = table.Column<int>(type: "integer", nullable: true),
                    price_version = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_snapshots",
                schema: "reporting",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_version = table.Column<int>(type: "integer", nullable: false),
                    subscription_version = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pricing_model = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    seat_count = table.Column<int>(type: "integer", nullable: true),
                    annualized_fixed_cents = table.Column<long>(type: "bigint", nullable: false),
                    metered_excluded = table.Column<bool>(type: "boolean", nullable: false),
                    ever_revenue_bearing = table.Column<bool>(type: "boolean", nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_source_event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_snapshots", x => new { x.organization_id, x.subscription_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_mrr_movements_organization_id_currency_occurred_at",
                schema: "reporting",
                table: "mrr_movements",
                columns: new[] { "organization_id", "currency", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_reporting_mrr_movements_organization_source_subscription",
                schema: "reporting",
                table: "mrr_movements",
                columns: new[] { "organization_id", "source_event_id", "subscription_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_events_organization_id_occurred_at",
                schema: "reporting",
                table: "source_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_source_events_organization_id_price_id_price_version",
                schema: "reporting",
                table: "source_events",
                columns: new[] { "organization_id", "price_id", "price_version" });

            migrationBuilder.CreateIndex(
                name: "ix_source_events_organization_id_subscription_id_subscription_",
                schema: "reporting",
                table: "source_events",
                columns: new[] { "organization_id", "subscription_id", "subscription_version" });

            migrationBuilder.CreateIndex(
                name: "ux_reporting_source_events_organization_source",
                schema: "reporting",
                table: "source_events",
                columns: new[] { "organization_id", "source_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_snapshots_organization_id_currency_status",
                schema: "reporting",
                table: "subscription_snapshots",
                columns: new[] { "organization_id", "currency", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_snapshots_organization_id_price_id",
                schema: "reporting",
                table: "subscription_snapshots",
                columns: new[] { "organization_id", "price_id" });

            migrationBuilder.Sql("""
                CREATE FUNCTION reporting.reject_source_event_mutation()
                RETURNS trigger LANGUAGE plpgsql AS $reporting$
                BEGIN
                    RAISE EXCEPTION 'reporting.source_events is append-only';
                END;
                $reporting$;
                CREATE TRIGGER source_events_append_only
                BEFORE UPDATE OR DELETE ON reporting.source_events
                FOR EACH ROW EXECUTE FUNCTION reporting.reject_source_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mrr_movements",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "source_events",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "subscription_snapshots",
                schema: "reporting");

            migrationBuilder.Sql("DROP FUNCTION reporting.reject_source_event_mutation();");
        }
    }
}
