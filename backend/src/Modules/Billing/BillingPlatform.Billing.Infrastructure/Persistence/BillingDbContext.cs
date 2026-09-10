using BillingPlatform.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Billing.Infrastructure.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options)
    : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    internal DbSet<BillingNumberSequence> NumberSequences => Set<BillingNumberSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");

        modelBuilder.Entity<Invoice>(builder =>
        {
            builder.ToTable("invoices");
            builder.HasKey(invoice => invoice.Id);
            builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(24);
            builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(64);
            builder.Property(invoice => invoice.Currency).HasMaxLength(3);
            builder.Property(invoice => invoice.SourceOperationKey).HasMaxLength(255);
            builder.Property(invoice => invoice.Version).IsConcurrencyToken().ValueGeneratedNever();
            builder.Ignore(invoice => invoice.Subtotal);
            builder.Ignore(invoice => invoice.Total);
            builder.HasIndex(invoice => new { invoice.OrganizationId, invoice.InvoiceNumber })
                .IsUnique();
            builder.HasIndex(invoice => new { invoice.OrganizationId, invoice.SourceOperationKey })
                .IsUnique();
            builder.HasIndex(invoice => new { invoice.OrganizationId, invoice.Status });
            builder.HasIndex(invoice => invoice.NextRetryAt);
            builder.OwnsMany(invoice => invoice.LineItems, lineBuilder =>
            {
                lineBuilder.ToTable("invoice_line_items");
                lineBuilder.WithOwner().HasForeignKey("InvoiceId");
                lineBuilder.HasKey(line => line.Id);
                lineBuilder.Property(line => line.Description).HasMaxLength(500);
                lineBuilder.Property(line => line.LineType).HasConversion<string>().HasMaxLength(32);
            });
        });

        modelBuilder.Entity<BillingNumberSequence>(builder =>
        {
            builder.ToTable("invoice_number_sequences");
            builder.HasKey(sequence => sequence.OrganizationId);
        });
    }
}
