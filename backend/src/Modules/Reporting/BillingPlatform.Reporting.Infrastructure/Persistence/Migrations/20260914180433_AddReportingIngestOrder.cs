using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingPlatform.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingIngestOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ingest_order",
                schema: "reporting",
                table: "source_events",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "ix_source_events_ingest_order",
                schema: "reporting",
                table: "source_events",
                column: "ingest_order",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_source_events_ingest_order",
                schema: "reporting",
                table: "source_events");

            migrationBuilder.DropColumn(
                name: "ingest_order",
                schema: "reporting",
                table: "source_events");
        }
    }
}
