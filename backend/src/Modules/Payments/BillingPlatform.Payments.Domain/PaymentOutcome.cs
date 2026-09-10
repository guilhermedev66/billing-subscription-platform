namespace BillingPlatform.Payments.Domain;

public enum PaymentOutcome
{
    Succeeded,
    Declined,
    InsufficientFunds,
    Expired,
    Requires3DS,
    ProcessingError
}
