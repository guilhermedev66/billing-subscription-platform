using BillingPlatform.Reporting.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Reporting.Infrastructure.Persistence;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options)
    : DbContext(options)
{
    public DbSet<ReportingSourceEvent> SourceEvents => Set<ReportingSourceEvent>();
    public DbSet<SubscriptionRevenueSnapshot> SubscriptionSnapshots => Set<SubscriptionRevenueSnapshot>();
    public DbSet<MrrMovement> MrrMovements => Set<MrrMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reporting");

        modelBuilder.Entity<ReportingSourceEvent>(builder =>
        {
            builder.ToTable("source_events");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.IngestOrder).ValueGeneratedOnAdd();
            builder.Property(item => item.SourceType).HasMaxLength(48);
            builder.Property(item => item.PayloadJson).HasColumnType("jsonb");
            builder.HasIndex(item => new { item.OrganizationId, item.SourceEventId })
                .HasDatabaseName("ux_reporting_source_events_organization_source")
                .IsUnique();
            builder.HasIndex(item => new { item.OrganizationId, item.SubscriptionId, item.SubscriptionVersion });
            builder.HasIndex(item => new { item.OrganizationId, item.PriceId, item.PriceVersion });
            builder.HasIndex(item => new { item.OrganizationId, item.OccurredAt });
            builder.HasIndex(item => item.IngestOrder).IsUnique();
        });

        modelBuilder.Entity<SubscriptionRevenueSnapshot>(builder =>
        {
            builder.ToTable("subscription_snapshots");
            builder.HasKey(item => new { item.OrganizationId, item.SubscriptionId });
            builder.Property(item => item.Currency).HasMaxLength(3);
            builder.Property(item => item.Status).HasMaxLength(16);
            builder.Property(item => item.PricingModel).HasMaxLength(16);
            builder.Property(item => item.AnnualizedFixedCents).HasColumnType("bigint");
            builder.HasIndex(item => new { item.OrganizationId, item.Currency, item.Status });
            builder.HasIndex(item => new { item.OrganizationId, item.PriceId });
        });

        modelBuilder.Entity<MrrMovement>(builder =>
        {
            builder.ToTable("mrr_movements");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.Currency).HasMaxLength(3);
            builder.Property(item => item.Kind).HasMaxLength(16);
            builder.Property(item => item.Reason).HasMaxLength(48);
            builder.Property(item => item.BeforeAnnualizedCents).HasColumnType("bigint");
            builder.Property(item => item.AfterAnnualizedCents).HasColumnType("bigint");
            builder.Property(item => item.DeltaAnnualizedCents).HasColumnType("bigint");
            builder.HasIndex(item => new { item.OrganizationId, item.SourceEventId, item.SubscriptionId })
                .HasDatabaseName("ux_reporting_mrr_movements_organization_source_subscription")
                .IsUnique();
            builder.HasIndex(item => new { item.OrganizationId, item.Currency, item.OccurredAt });
        });
    }
}
