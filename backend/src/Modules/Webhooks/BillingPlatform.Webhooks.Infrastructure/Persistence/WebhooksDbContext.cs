using BillingPlatform.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Webhooks.Infrastructure.Persistence;

public sealed class WebhooksDbContext(DbContextOptions<WebhooksDbContext> options) : DbContext(options)
{
    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookOutboxEvent> OutboxEvents => Set<WebhookOutboxEvent>();
    public DbSet<WebhookDeliveryAttempt> DeliveryAttempts => Set<WebhookDeliveryAttempt>();
    public DbSet<WebhookInboxEntry> InboxEntries => Set<WebhookInboxEntry>();
    public DbSet<WebhookProjection> Projections => Set<WebhookProjection>();
    public DbSet<WebhookIdempotencyRecord> IdempotencyRecords => Set<WebhookIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("webhooks");

        modelBuilder.Entity<WebhookEndpoint>(builder =>
        {
            builder.ToTable("endpoints");
            builder.HasKey(endpoint => endpoint.Id);
            builder.Property(endpoint => endpoint.Url).HasMaxLength(2048);
            builder.Property(endpoint => endpoint.Secret).HasMaxLength(256);
            builder.Property(endpoint => endpoint.EventTypesJson).HasColumnType("text");
            builder.HasIndex(endpoint => new { endpoint.OrganizationId, endpoint.Url });
        });

        modelBuilder.Entity<WebhookOutboxEvent>(builder =>
        {
            builder.ToTable("outbox_events");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.EventType).HasMaxLength(128);
            builder.Property(item => item.AggregateType).HasMaxLength(128);
            builder.Property(item => item.RawBody).HasColumnType("bytea");
            builder.HasIndex(item => new { item.OrganizationId, item.DispatchedAt, item.AvailableAt });
            builder.HasIndex(item => item.DispatchLeaseUntil);
        });

        modelBuilder.Entity<WebhookDeliveryAttempt>(builder =>
        {
            builder.ToTable("delivery_attempts");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.ResponseBody).HasColumnType("text");
            builder.Property(item => item.Error).HasColumnType("text");
            builder.HasIndex(item => new { item.OutboxEventId, item.EndpointId, item.AttemptNumber });
        });

        modelBuilder.Entity<WebhookInboxEntry>(builder =>
        {
            builder.ToTable("inbox_entries");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.EventType).HasMaxLength(128);
            builder.Property(item => item.RawBody).HasColumnType("bytea");
            builder.HasIndex(item => new { item.OrganizationId, item.EventId })
                .HasDatabaseName("ux_webhook_inbox_organization_event")
                .IsUnique();
        });

        modelBuilder.Entity<WebhookProjection>(builder =>
        {
            builder.ToTable("projections");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.AggregateType).HasMaxLength(128);
            builder.Property(item => item.EventType).HasMaxLength(128);
            builder.Property(item => item.RawBody).HasColumnType("bytea");
            builder.HasIndex(item => new { item.OrganizationId, item.AggregateType, item.AggregateId })
                .HasDatabaseName("ux_webhook_projection_aggregate")
                .IsUnique();
        });

        modelBuilder.Entity<WebhookIdempotencyRecord>(builder =>
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
                })
                .HasDatabaseName("ux_webhook_idempotency_organization_operation_key")
                .IsUnique();
        });
    }
}
