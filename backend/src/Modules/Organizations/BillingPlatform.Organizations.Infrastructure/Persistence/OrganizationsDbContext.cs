using BillingPlatform.Organizations.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Organizations.Infrastructure.Persistence;

public sealed class OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMembership> Memberships => Set<OrganizationMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("organizations");

        modelBuilder.Entity<Organization>(builder =>
        {
            builder.ToTable("organizations");
            builder.HasKey(organization => organization.Id);
            builder.Property(organization => organization.Name).HasMaxLength(200);
            builder.Property(organization => organization.DefaultCurrency).HasMaxLength(3);
            builder.Property(organization => organization.InvoicePrefix).HasMaxLength(16);
            builder.Property(organization => organization.WebhookSecret).HasMaxLength(128);
        });

        modelBuilder.Entity<OrganizationMembership>(builder =>
        {
            builder.ToTable("organization_memberships");
            builder.HasKey(membership => new { membership.OrganizationId, membership.UserId });
            builder.HasIndex(membership => new { membership.UserId, membership.CreatedAt });
            builder
                .HasOne<Organization>()
                .WithMany()
                .HasForeignKey(membership => membership.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
