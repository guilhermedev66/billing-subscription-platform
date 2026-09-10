using BillingPlatform.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Subscriptions.Infrastructure.Persistence;

public sealed class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options)
    : DbContext(options)
{
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<SubscriptionIdempotencyRecord> IdempotencyRecords =>
        Set<SubscriptionIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("subscriptions");

        modelBuilder.Entity<Subscription>(builder =>
        {
            builder.ToTable("subscriptions");
            builder.HasKey(subscription => subscription.Id);
            builder.Property(subscription => subscription.Status)
                .HasConversion<string>()
                .HasMaxLength(16);
            builder.Property(subscription => subscription.Version)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            builder.HasIndex(subscription => new
            {
                subscription.OrganizationId,
                subscription.CustomerId
            });

        modelBuilder.Entity<SubscriptionIdempotencyRecord>(builder =>
        {
            builder.ToTable("idempotency_records");
            builder.HasKey(record => record.Id);
            builder.Property(record => record.Operation).HasMaxLength(64);
            builder.Property(record => record.IdempotencyKey).HasMaxLength(255);
            builder.Property(record => record.RequestHash).HasMaxLength(128);
            builder.Property(record => record.ResponseBody).HasColumnType("text");
            builder.Property(record => record.ResponseLocation).HasMaxLength(500);
            builder.HasIndex(record => new
            {
                record.OrganizationId,
                record.Operation,
                record.IdempotencyKey
            }).HasDatabaseName("ux_idempotency_records_organization_id_operation_key")
              .IsUnique();
        });
            builder.HasIndex(subscription => new
            {
                subscription.OrganizationId,
                subscription.Status
            });
        });
    }
}
