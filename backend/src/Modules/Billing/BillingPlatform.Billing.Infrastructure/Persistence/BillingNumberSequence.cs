namespace BillingPlatform.Billing.Infrastructure.Persistence;

internal sealed class BillingNumberSequence
{
    public Guid OrganizationId { get; set; }

    public long NextNumber { get; set; }
}
