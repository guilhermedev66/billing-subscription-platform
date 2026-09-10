using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingPlatform.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueCustomerEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_organization_id_email",
                schema: "customers",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_customers_organization_id_email",
                schema: "customers",
                table: "customers",
                columns: new[] { "organization_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_organization_id_email",
                schema: "customers",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_customers_organization_id_email",
                schema: "customers",
                table: "customers",
                columns: new[] { "organization_id", "email" });
        }
    }
}
