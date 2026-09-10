using BillingPlatform.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Price> Prices => Set<Price>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");

        modelBuilder.Entity<Product>(builder =>
        {
            builder.ToTable("products");
            builder.HasKey(product => product.Id);
            builder.Property(product => product.Name).HasMaxLength(200);
            builder.Property(product => product.Description).HasMaxLength(2000);
            builder.HasIndex(product => new { product.OrganizationId, product.Name });
        });

        modelBuilder.Entity<Price>(builder =>
        {
            builder.ToTable("prices");
            builder.HasKey(price => price.Id);
            builder.Property(price => price.PricingModel)
                .HasConversion<string>()
                .HasMaxLength(32);
            builder.Property(price => price.Currency).HasMaxLength(3);
            builder.Property(price => price.BillingInterval)
                .HasConversion<string>()
                .HasMaxLength(16);
            builder.Property(price => price.MeteredAggregation)
                .HasConversion<string>()
                .HasMaxLength(16);
            builder.Property(price => price.FlatUnitAmountCents).HasColumnType("bigint");
            builder.Property(price => price.PerSeatUnitAmountCents).HasColumnType("bigint");
            builder.Property(price => price.MeteredUnitAmountCents).HasColumnType("bigint");
            builder.HasIndex(price => new { price.OrganizationId, price.ProductId });
            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(price => price.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.OwnsMany(
                price => price.Tiers,
                tierBuilder =>
                {
                    tierBuilder.ToTable("price_tiers");
                    tierBuilder.WithOwner().HasForeignKey("price_id");
                    tierBuilder.HasKey("price_id", nameof(PricingTier.Id));
                    tierBuilder.Property(tier => tier.Id).HasColumnName("id");
                    tierBuilder.Property(tier => tier.StartingUnit).HasColumnName("starting_unit");
                    tierBuilder.Property(tier => tier.EndingUnit).HasColumnName("ending_unit");
                    tierBuilder.Property(tier => tier.UnitAmountCents)
                        .HasColumnName("unit_amount_cents")
                        .HasColumnType("bigint");
                });
        });
    }
}
