namespace BillingPlatform.Billing.Domain;

public sealed class InvoiceDomainException(string message) : Exception(message);
