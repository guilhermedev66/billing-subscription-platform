using BillingPlatform.Billing.Domain;
using BillingPlatform.Payments.Domain;

namespace BillingPlatform.UnitTests;

public sealed class BillingAndPaymentsDomainTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_invoice_line_items_are_immutable_and_totals_are_integer_cents()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "INV-0001",
            "USD",
            Now,
            Now,
            Now);
        invoice.AddLineItem("Base plan", 2900, InvoiceLineType.Base);
        invoice.AddLineItem("Unused time", -1450, InvoiceLineType.ProrationCredit);
        invoice.Open();

        Assert.Equal(1450L, invoice.Subtotal);
        Assert.Equal(1450L, invoice.Total);
        Assert.Throws<InvoiceDomainException>(() =>
            invoice.AddLineItem("Late edit", 1, InvoiceLineType.Base));
    }

    [Fact]
    public void Dunning_schedule_reaches_uncollectible_on_day_fourteen()
    {
        var invoice = CreateOpenInvoice();

        invoice.RecordPaymentFailure(Now);
        Assert.Equal(1, invoice.DunningAttemptCount);
        Assert.Equal(Now.AddDays(3), invoice.NextRetryAt);

        invoice.RecordPaymentFailure(Now.AddDays(3));
        Assert.Equal(2, invoice.DunningAttemptCount);
        Assert.Equal(Now.AddDays(7), invoice.NextRetryAt);

        invoice.RecordPaymentFailure(Now.AddDays(7));
        Assert.Equal(3, invoice.DunningAttemptCount);
        Assert.Equal(Now.AddDays(14), invoice.NextRetryAt);

        invoice.RecordPaymentFailure(Now.AddDays(14));
        Assert.Equal(4, invoice.DunningAttemptCount);
        Assert.Equal(InvoiceStatus.Uncollectible, invoice.Status);
        Assert.Null(invoice.NextRetryAt);
    }

    [Fact]
    public void Payment_attempt_never_accepts_or_stores_a_full_card_number()
    {
        var exception = Assert.Throws<ArgumentException>(() => PaymentAttempt.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "4242424242424242",
            PaymentOutcome.Succeeded,
            Now,
            "charge",
            "payment-key",
            "hash",
            "{}",
            200,
            1));

        Assert.Contains("last four", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Invoice CreateOpenInvoice()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-0001",
            "USD",
            Now,
            Now,
            Now);
        invoice.AddLineItem("Renewal", 2900, InvoiceLineType.Base);
        invoice.Open();
        return invoice;
    }
}
