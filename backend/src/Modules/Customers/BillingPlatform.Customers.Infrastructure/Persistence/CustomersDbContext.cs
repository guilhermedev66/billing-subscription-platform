using BillingPlatform.Customers.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Customers.Infrastructure.Persistence;

public sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options)
    : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("customers");

        modelBuilder.Entity<Customer>(builder =>
        {
            builder.ToTable("customers");
            builder.HasKey(customer => customer.Id);
            builder.Property(customer => customer.Name).HasMaxLength(200);
            builder.Property(customer => customer.Email).HasMaxLength(320);
            builder.Property(customer => customer.BalanceCents).HasColumnType("bigint");
            builder.HasIndex(customer => new { customer.OrganizationId, customer.Email })
                .IsUnique();
        });
    }
}
