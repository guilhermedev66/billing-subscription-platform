using BillingPlatform.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
    : DbContext(options)
{
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    public DbSet<PaymentSweepIdempotencyRecord> SweepIdempotencyRecords =>
        Set<PaymentSweepIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");
        modelBuilder.Entity<PaymentAttempt>(builder =>
        {
            builder.ToTable("payment_attempts");
            builder.HasKey(attempt => attempt.Id);
            builder.Property(attempt => attempt.CardNumberLast4).HasMaxLength(4);
            builder.Property(attempt => attempt.Outcome).HasConversion<string>().HasMaxLength(32);
            builder.Property(attempt => attempt.Operation).HasMaxLength(64);
            builder.Property(attempt => attempt.IdempotencyKey).HasMaxLength(255);
            builder.Property(attempt => attempt.RequestHash).HasMaxLength(128);
            builder.Property(attempt => attempt.ResponseBody).HasColumnType("text");
            builder.HasIndex(attempt => new
            {
                attempt.OrganizationId,
                attempt.Operation,
                attempt.IdempotencyKey
            })
            .HasDatabaseName("ux_payment_attempts_organization_id_operation_key")
            .IsUnique();
            builder.HasIndex(attempt => new { attempt.OrganizationId, attempt.InvoiceId, attempt.AttemptedAt });
        });

        modelBuilder.Entity<PaymentSweepIdempotencyRecord>(builder =>
        {
            builder.ToTable("sweep_idempotency_records");
            builder.HasKey(record => record.Id);
            builder.Property(record => record.IdempotencyKey).HasMaxLength(255);
            builder.Property(record => record.RequestHash).HasMaxLength(128);
            builder.Property(record => record.ResponseBody).HasColumnType("text");
            builder.HasIndex(record => new { record.OrganizationId, record.IdempotencyKey })
                .HasDatabaseName("ux_sweep_idempotency_records_organization_id_key")
                .IsUnique();
        });
    }
}
