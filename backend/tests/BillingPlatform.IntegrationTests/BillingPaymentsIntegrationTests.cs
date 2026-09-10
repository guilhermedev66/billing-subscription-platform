using System.Net;
using System.Net.Http.Json;
using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Billing.Infrastructure.Persistence;
using BillingPlatform.Payments.Infrastructure.Persistence;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Domain;
using BillingPlatform.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class BillingPaymentsIntegrationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Simulator_only_duns_business_declines_and_handles_3ds_and_gateway_errors()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Payment Outcomes Organization",
            ApiTestClient.UniqueEmail("payment-outcomes"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var expiredInvoice = await CreateOpenInvoiceAsync(
            factory,
            organization.User.OrganizationId,
            customer.Id,
            "Expired card invoice",
            Guid.NewGuid());
        var threeDsInvoice = await CreateOpenInvoiceAsync(
            factory,
            organization.User.OrganizationId,
            customer.Id,
            "3DS invoice");
        var processingErrorInvoice = await CreateOpenInvoiceAsync(
            factory,
            organization.User.OrganizationId,
            customer.Id,
            "Processing error invoice");

        using (var expiredResponse = await SendPaymentAsync(
                   client,
                   organization.Token,
                   expiredInvoice.Id,
                   "4000 0000 0000 0005",
                   "expired-card"))
        {
            Assert.Equal(HttpStatusCode.OK, expiredResponse.StatusCode);
            var payment = await expiredResponse.Content.ReadFromJsonAsync<PaymentResponse>();
            Assert.NotNull(payment);
            Assert.Equal(3, payment.Attempt.Outcome);
            Assert.Equal(1, payment.Invoice.Status);
            Assert.Equal(0, payment.Invoice.DunningAttemptCount);
            Assert.Null(payment.Invoice.NextRetryAt);
        }

        using (var expiredDecline = await SendPaymentAsync(
                   client,
                   organization.Token,
                   expiredInvoice.Id,
                   "4000 0000 0000 0002",
                   "expired-setup-decline"))
        {
            Assert.Equal(HttpStatusCode.OK, expiredDecline.StatusCode);
        }

        using (var expiredManual = await SendPaymentAsync(
                   client,
                   organization.Token,
                   expiredInvoice.Id,
                   "4000 0000 0000 0005",
                   "expired-manual"))
        {
            Assert.Equal(HttpStatusCode.OK, expiredManual.StatusCode);
            var payment = await expiredManual.Content.ReadFromJsonAsync<PaymentResponse>();
            Assert.NotNull(payment);
            Assert.Equal(3, payment.Attempt.Outcome);
            Assert.Equal(1, payment.Invoice.DunningAttemptCount);
        }

        SetClock(factory, DateTimeOffset.UtcNow.AddDays(4));
        using (var expiredSweep = await SendSweepAsync(
                   client,
                   organization.Token,
                   "expired-not-swept"))
        {
            Assert.Equal(HttpStatusCode.OK, expiredSweep.StatusCode);
            var sweep = await expiredSweep.Content.ReadFromJsonAsync<DunningSweepResponse>();
            Assert.NotNull(sweep);
            Assert.Equal(0, sweep.Processed);
        }

        using (var expiredRecovery = await SendPaymentAsync(
                   client,
                   organization.Token,
                   expiredInvoice.Id,
                   "4242 4242 4242 4242",
                   "expired-manual-recovery"))
        {
            Assert.Equal(HttpStatusCode.OK, expiredRecovery.StatusCode);
            var payment = await expiredRecovery.Content.ReadFromJsonAsync<PaymentResponse>();
            Assert.NotNull(payment);
            Assert.Equal(2, payment.Invoice.Status);
        }

        using (var threeDsResponse = await SendPaymentAsync(
                   client,
                   organization.Token,
                   threeDsInvoice.Id,
                   "4000 0000 0000 3022",
                   "three-ds"))
        {
            Assert.Equal(HttpStatusCode.OK, threeDsResponse.StatusCode);
            var payment = await threeDsResponse.Content.ReadFromJsonAsync<PaymentResponse>();
            Assert.NotNull(payment);
            Assert.Equal(4, payment.Attempt.Outcome);
            Assert.Equal(1, payment.Invoice.Status);
            Assert.Equal(0, payment.Invoice.DunningAttemptCount);
        }

        using (var threeDsConfirmation = await SendPaymentAsync(
                   client,
                   organization.Token,
                   threeDsInvoice.Id,
                   "4000 0000 0000 3022",
                   "three-ds-confirm"))
        {
            Assert.Equal(HttpStatusCode.OK, threeDsConfirmation.StatusCode);
            var payment = await threeDsConfirmation.Content.ReadFromJsonAsync<PaymentResponse>();
            Assert.NotNull(payment);
            Assert.Equal(0, payment.Attempt.Outcome);
            Assert.Equal(2, payment.Invoice.Status);
        }

        using (var processingErrorResponse = await SendPaymentAsync(
                   client,
                   organization.Token,
                   processingErrorInvoice.Id,
                   "4000 0000 0000 0007",
                   "processing-error"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, processingErrorResponse.StatusCode);
        }

        using var processingHistory = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get,
            $"/api/payments/invoices/{processingErrorInvoice.Id}/attempts",
            organization.Token);
        using var processingHistoryResponse = await client.SendAsync(processingHistory);
        Assert.Equal(HttpStatusCode.OK, processingHistoryResponse.StatusCode);
        Assert.Empty(
            await processingHistoryResponse.Content.ReadFromJsonAsync<PaymentAttemptResponse[]>() ?? []);
    }

    [Fact]
    public async Task Payment_attempt_history_and_void_action_are_tenant_scoped()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organizationA = await ApiTestClient.RegisterAsync(
            client,
            "Invoice Actions Organization A",
            ApiTestClient.UniqueEmail("invoice-actions-a"));
        var organizationB = await ApiTestClient.RegisterAsync(
            client,
            "Invoice Actions Organization B",
            ApiTestClient.UniqueEmail("invoice-actions-b"));
        var customer = await CreateCustomerAsync(client, organizationA.Token);

        InvoiceSummary invoice;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
            invoice = Assert.IsType<InvoiceSummary>(await invoiceService.CreateOpenAsync(
                organizationA.User.OrganizationId,
                new CreateOpenInvoiceCommand(
                    customer.Id,
                    null,
                    "USD",
                    [new InvoiceLineItemInput("Manual invoice", 2500, InvoiceLineType.Base)],
                    $"invoice-actions:{Guid.NewGuid():N}")));
        }

        using (var emptyHistory = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/payments/invoices/{invoice.Id}/attempts",
                   organizationA.Token))
        using (var emptyHistoryResponse = await client.SendAsync(emptyHistory))
        {
            Assert.Equal(HttpStatusCode.OK, emptyHistoryResponse.StatusCode);
            Assert.Empty(
                await emptyHistoryResponse.Content.ReadFromJsonAsync<PaymentAttemptResponse[]>() ?? []);
        }

        using (var crossTenantHistory = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/payments/invoices/{invoice.Id}/attempts",
                   organizationB.Token))
        using (var crossTenantHistoryResponse = await client.SendAsync(crossTenantHistory))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantHistoryResponse.StatusCode);
        }

        using (var crossTenantVoid = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/invoices/{invoice.Id}/void",
                   organizationB.Token))
        using (var crossTenantVoidResponse = await client.SendAsync(crossTenantVoid))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantVoidResponse.StatusCode);
        }

        using var voidRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/invoices/{invoice.Id}/void",
            organizationA.Token);
        using var voidResponse = await client.SendAsync(voidRequest);
        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        var voidedInvoice = await voidResponse.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.NotNull(voidedInvoice);
        Assert.Equal(3, voidedInvoice.Status);

        using var repeatedVoidRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/invoices/{invoice.Id}/void",
            organizationA.Token);
        using var repeatedVoidResponse = await client.SendAsync(repeatedVoidRequest);
        Assert.Equal(HttpStatusCode.Conflict, repeatedVoidResponse.StatusCode);
    }

    [Fact]
    public async Task Payment_attempt_rolls_back_all_financial_state_when_subscription_update_fails()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Atomic Payment Organization",
            ApiTestClient.UniqueEmail("atomic-payment"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, 1200);
        var newPrice = await CreatePriceAsync(client, organization.Token, product.Id, 2400);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id);

        using (var initialFailure = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/apply-change",
                   organization.Token,
                   JsonContent.Create(new
                   {
                       newPriceId = newPrice.Id,
                       cardNumber = "4000 0000 0000 0002"
                   })))
        {
            initialFailure.Headers.Add("Idempotency-Key", "atomic-initial-failure");
            using var response = await client.SendAsync(initialFailure);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var invoice = Assert.Single(await GetInvoicesAsync(client, organization.Token));
        await using var failingFactory = database.CreateFactory(services =>
        {
            services.RemoveAll<ISubscriptionPaymentStateService>();
            services.AddScoped<ISubscriptionPaymentStateService, ThrowOnRecoverSubscriptionStateService>();
        });
        using var failingClient = failingFactory.CreateClient();
        using var payment = await SendPaymentAsync(
            failingClient,
            organization.Token,
            invoice.Id,
            "4242 4242 4242 4242",
            "atomic-recovery-failure");
        Assert.Equal(HttpStatusCode.InternalServerError, payment.StatusCode);

        await using var scope = failingFactory.Services.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var storedInvoice = await billing.Invoices.SingleAsync(item => item.Id == invoice.Id);
        var storedSubscription = await subscriptions.Subscriptions.SingleAsync(item => item.Id == subscription.Id);
        Assert.Equal(InvoiceStatus.Open, storedInvoice.Status);
        Assert.Equal(1, await payments.PaymentAttempts.CountAsync(item => item.InvoiceId == invoice.Id));
        Assert.Equal(SubscriptionStatus.PastDue, storedSubscription.Status);
    }

    [Fact]
    public async Task Subscription_change_billing_failure_rolls_back_change_and_is_recoverable()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Atomic Change Organization",
            ApiTestClient.UniqueEmail("atomic-change"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, 1200);
        var upgradePrice = await CreatePriceAsync(client, organization.Token, product.Id, 2400);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id);

        using (var failedChange = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/apply-change",
                   organization.Token,
                   JsonContent.Create(new
                   {
                       newPriceId = upgradePrice.Id,
                       cardNumber = "4000 0000 0000 0007"
                   })))
        {
            failedChange.Headers.Add("Idempotency-Key", "atomic-change");
            using var response = await client.SendAsync(failedChange);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        using (var getSubscription = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/subscriptions/{subscription.Id}",
                   organization.Token))
        using (var response = await client.SendAsync(getSubscription))
        {
            var stored = await response.Content.ReadFromJsonAsync<SubscriptionResponse>();
            Assert.NotNull(stored);
            Assert.Equal(currentPrice.Id, stored.PriceId);
        }

        Assert.Empty(await GetInvoicesAsync(client, organization.Token));

        using var recoveredChange = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{subscription.Id}/apply-change",
            organization.Token,
            JsonContent.Create(new
            {
                newPriceId = upgradePrice.Id,
                cardNumber = "4242 4242 4242 4242"
            }));
        recoveredChange.Headers.Add("Idempotency-Key", "atomic-change");
        using var recoveredResponse = await client.SendAsync(recoveredChange);
        Assert.Equal(HttpStatusCode.OK, recoveredResponse.StatusCode);
        Assert.Single(await GetInvoicesAsync(client, organization.Token));
    }

    [Fact]
    public async Task Dunning_sweep_rolls_back_payment_and_invoice_when_subscription_update_fails()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Atomic Sweep Organization",
            ApiTestClient.UniqueEmail("atomic-sweep"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, 1200);
        var renewalPrice = await CreatePriceAsync(client, organization.Token, product.Id, 2400);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id);

        using (var initialFailure = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/apply-change",
                   organization.Token,
                   JsonContent.Create(new
                   {
                       newPriceId = renewalPrice.Id,
                       cardNumber = "4000 0000 0000 0004"
                   })))
        {
            initialFailure.Headers.Add("Idempotency-Key", "atomic-sweep-initial");
            using var response = await client.SendAsync(initialFailure);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var invoice = Assert.Single(await GetInvoicesAsync(client, organization.Token));
        Assert.NotNull(invoice.NextRetryAt);
        await using var failingFactory = database.CreateFactory(services =>
        {
            services.RemoveAll<ISubscriptionPaymentStateService>();
            services.AddScoped<ISubscriptionPaymentStateService, ThrowOnPaymentStateReadService>();
        });
        SetClock(failingFactory, invoice.NextRetryAt.Value.Add(TimeSpan.FromMicroseconds(1)));
        using var failingClient = failingFactory.CreateClient();
        using (var sweep = await SendSweepAsync(
                   failingClient,
                   organization.Token,
                   "atomic-sweep-failure"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, sweep.StatusCode);
        }

        await using var scope = failingFactory.Services.CreateAsyncScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var storedInvoice = await billing.Invoices.SingleAsync(item => item.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Open, storedInvoice.Status);
        Assert.Equal(1, storedInvoice.DunningAttemptCount);
        Assert.Equal(1, await payments.PaymentAttempts.CountAsync(item => item.InvoiceId == invoice.Id));
        Assert.Equal(
            0,
            await payments.SweepIdempotencyRecords.CountAsync(
                item => item.OrganizationId == organization.User.OrganizationId &&
                        item.IdempotencyKey == "atomic-sweep-failure"));
    }

    [Fact]
    public async Task Negative_proration_credit_invoice_is_settled_as_paid()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Credit Invoice Organization",
            ApiTestClient.UniqueEmail("credit-invoice"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, 2400);
        var downgradePrice = await CreatePriceAsync(client, organization.Token, product.Id, 1200);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id);

        using var downgrade = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{subscription.Id}/apply-change",
            organization.Token,
            JsonContent.Create(new { newPriceId = downgradePrice.Id }));
        downgrade.Headers.Add("Idempotency-Key", "negative-proration-credit");
        using var downgradeResponse = await client.SendAsync(downgrade);
        Assert.Equal(HttpStatusCode.OK, downgradeResponse.StatusCode);

        var invoice = Assert.Single(await GetInvoicesAsync(client, organization.Token));
        Assert.Equal(2, invoice.Status);
        Assert.True(invoice.Total < 0);
        Assert.NotNull(invoice.PaidAt);
    }

    [Fact]
    public async Task Invoices_and_payments_are_tenant_isolated_and_deterministic()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organizationA = await ApiTestClient.RegisterAsync(
            client,
            "Billing Organization A",
            ApiTestClient.UniqueEmail("billing-a"));
        var organizationB = await ApiTestClient.RegisterAsync(
            client,
            "Billing Organization B",
            ApiTestClient.UniqueEmail("billing-b"));
        var customer = await CreateCustomerAsync(client, organizationA.Token);
        var product = await CreateProductAsync(client, organizationA.Token);
        var currentPrice = await CreatePriceAsync(client, organizationA.Token, product.Id, 1200);
        var upgradePrice = await CreatePriceAsync(client, organizationA.Token, product.Id, 2400);
        var subscription = await CreateSubscriptionAsync(
            client, organizationA.Token, customer.Id, currentPrice.Id);

        using (var change = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/apply-change",
                   organizationA.Token,
                   JsonContent.Create(new
                   {
                       newPriceId = upgradePrice.Id,
                       cardNumber = "4000 0000 0000 0002"
                   })))
        {
            change.Headers.Add("Idempotency-Key", "billing-decline-change");
            using var response = await client.SendAsync(change);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var invoices = await GetInvoicesAsync(client, organizationA.Token);
        var invoice = Assert.Single(invoices);
        Assert.Equal(1, invoice.Status);
        Assert.Equal(1, invoice.DunningAttemptCount);
        Assert.True(invoice.Total > 0);

        using (var attemptHistory = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/payments/invoices/{invoice.Id}/attempts",
                   organizationA.Token))
        using (var attemptHistoryResponse = await client.SendAsync(attemptHistory))
        {
            Assert.Equal(HttpStatusCode.OK, attemptHistoryResponse.StatusCode);
            var attempts = await attemptHistoryResponse.Content
                .ReadFromJsonAsync<PaymentAttemptResponse[]>();
            var attempt = Assert.Single(attempts ?? []);
            Assert.Equal(1, attempt.AttemptNumber);
            Assert.Equal(1, attempt.Outcome);
        }

        using (var list = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get, "/api/invoices/", organizationB.Token))
        using (var listResponse = await client.SendAsync(list))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            Assert.Empty(await listResponse.Content.ReadFromJsonAsync<InvoiceResponse[]>() ?? []);
        }

        using (var crossTenantGet = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/invoices/{invoice.Id}",
                   organizationB.Token))
        using (var crossTenantResponse = await client.SendAsync(crossTenantGet))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
        }

        using (var crossTenantPayment = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/payments/invoices/{invoice.Id}/attempt",
                   organizationB.Token,
                   JsonContent.Create(new { cardNumber = "4242 4242 4242 4242" })))
        {
            crossTenantPayment.Headers.Add("Idempotency-Key", "cross-tenant-payment");
            using var crossTenantPaymentResponse = await client.SendAsync(crossTenantPayment);
            Assert.Equal(HttpStatusCode.NotFound, crossTenantPaymentResponse.StatusCode);
        }

        using var payment = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/payments/invoices/{invoice.Id}/attempt",
            organizationA.Token,
            JsonContent.Create(new { cardNumber = "4242 4242 4242 4242" }));
        payment.Headers.Add("Idempotency-Key", "recover-payment");
        using var paymentResponse = await client.SendAsync(payment);
        Assert.Equal(HttpStatusCode.OK, paymentResponse.StatusCode);
        var paymentBody = await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.NotNull(paymentBody);
        Assert.Equal(0, paymentBody.Attempt.Outcome);
        Assert.Equal("4242", paymentBody.Attempt.CardNumberLast4);
        Assert.Equal(2, paymentBody.Invoice.Status);

        using (var attemptHistory = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/payments/invoices/{invoice.Id}/attempts",
                   organizationA.Token))
        using (var attemptHistoryResponse = await client.SendAsync(attemptHistory))
        {
            Assert.Equal(HttpStatusCode.OK, attemptHistoryResponse.StatusCode);
            var attempts = await attemptHistoryResponse.Content
                .ReadFromJsonAsync<PaymentAttemptResponse[]>();
            Assert.Collection(
                attempts ?? [],
                attempt =>
                {
                    Assert.Equal(1, attempt.AttemptNumber);
                    Assert.Equal(1, attempt.Outcome);
                },
                attempt =>
                {
                    Assert.Equal(2, attempt.AttemptNumber);
                    Assert.Equal(0, attempt.Outcome);
                });
        }

        using var replay = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/payments/invoices/{invoice.Id}/attempt",
            organizationA.Token,
            JsonContent.Create(new { cardNumber = "4242 4242 4242 4242" }));
        replay.Headers.Add("Idempotency-Key", "recover-payment");
        using var replayResponse = await client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(
            await paymentResponse.Content.ReadAsStringAsync(),
            await replayResponse.Content.ReadAsStringAsync());

        using var conflictingReplay = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/payments/invoices/{invoice.Id}/attempt",
            organizationA.Token,
            JsonContent.Create(new { cardNumber = "4000 0000 0000 0005" }));
        conflictingReplay.Headers.Add("Idempotency-Key", "recover-payment");
        using var conflictingResponse = await client.SendAsync(conflictingReplay);
        Assert.Equal(HttpStatusCode.Conflict, conflictingResponse.StatusCode);

        using var paidInvoiceAttempt = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/payments/invoices/{invoice.Id}/attempt",
            organizationA.Token,
            JsonContent.Create(new { cardNumber = "4242 4242 4242 4242" }));
        paidInvoiceAttempt.Headers.Add("Idempotency-Key", "already-paid-invoice");
        using var paidInvoiceResponse = await client.SendAsync(paidInvoiceAttempt);
        Assert.Equal(HttpStatusCode.Conflict, paidInvoiceResponse.StatusCode);
    }

    [Fact]
    public async Task Dunning_sweep_follows_day_three_seven_fourteen_and_is_concurrent_safe()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Dunning Organization",
            ApiTestClient.UniqueEmail("dunning"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, 1000);
        var renewalPrice = await CreatePriceAsync(client, organization.Token, product.Id, 2000);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id);

        using (var change = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/apply-change",
                   organization.Token,
                   JsonContent.Create(new
                   {
                       newPriceId = renewalPrice.Id,
                       cardNumber = "4000 0000 0000 0004"
                   })))
        {
            change.Headers.Add("Idempotency-Key", "dunning-initial-failure");
            using var response = await client.SendAsync(change);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var invoice = Assert.Single(await GetInvoicesAsync(client, organization.Token));
        Assert.Equal(1, invoice.DunningAttemptCount);
        var dayThree = invoice.NextRetryAt;
        Assert.NotNull(dayThree);

        SetClock(factory, dayThree.Value.Add(TimeSpan.FromMicroseconds(1)));
        var sweepResponses = await Task.WhenAll(
            Enumerable.Range(1, 5).Select(index => SendSweepAsync(
                client,
                organization.Token,
                $"concurrent-sweep-{index}")));
        Assert.All(sweepResponses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in sweepResponses)
        {
            response.Dispose();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            Assert.Equal(2, await payments.PaymentAttempts.CountAsync(item => item.InvoiceId == invoice.Id));
        }

        invoice = Assert.Single(await GetInvoicesAsync(client, organization.Token));
        Assert.Equal(2, invoice.DunningAttemptCount);
        Assert.NotNull(invoice.NextRetryAt);
        SetClock(factory, invoice.NextRetryAt.Value.Add(TimeSpan.FromMicroseconds(1)));
        using (var sweep = await SendSweepAsync(client, organization.Token, "dunning-day-seven"))
        {
            Assert.Equal(HttpStatusCode.OK, sweep.StatusCode);
        }

        invoice = Assert.Single(await GetInvoicesAsync(client, organization.Token));
        Assert.Equal(3, invoice.DunningAttemptCount);
        Assert.NotNull(invoice.NextRetryAt);
        SetClock(factory, invoice.NextRetryAt.Value.Add(TimeSpan.FromMicroseconds(1)));
        using (var sweep = await SendSweepAsync(client, organization.Token, "dunning-day-fourteen"))
        {
            Assert.Equal(HttpStatusCode.OK, sweep.StatusCode);
        }

        var finalInvoice = (await GetInvoicesAsync(client, organization.Token)).Single();
        Assert.Equal(4, finalInvoice.DunningAttemptCount);
        Assert.Equal(4, finalInvoice.Status);
        using var subscriptionGet = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get,
            $"/api/subscriptions/{subscription.Id}",
            organization.Token);
        using var subscriptionResponse = await client.SendAsync(subscriptionGet);
        var finalSubscription = await subscriptionResponse.Content.ReadFromJsonAsync<BillingSubscriptionResponse>();
        Assert.NotNull(finalSubscription);
        Assert.Equal(3, finalSubscription.Status);
    }

    private static void SetClock(BillingPlatformApiFactory factory, DateTimeOffset value)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IVirtualClock>().SetTime(value);
    }

    private static async Task<InvoiceSummary> CreateOpenInvoiceAsync(
        BillingPlatformApiFactory factory,
        Guid organizationId,
        Guid customerId,
        string description,
        Guid? subscriptionId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
        return Assert.IsType<InvoiceSummary>(await invoiceService.CreateOpenAsync(
            organizationId,
            new CreateOpenInvoiceCommand(
                customerId,
                subscriptionId,
                "USD",
                [new InvoiceLineItemInput(description, 2500, InvoiceLineType.Base)],
                $"payment-outcome:{Guid.NewGuid():N}")));
    }

    private static async Task<HttpResponseMessage> SendPaymentAsync(
        HttpClient client,
        string token,
        Guid invoiceId,
        string cardNumber,
        string idempotencyKey)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/payments/invoices/{invoiceId}/attempt",
            token,
            JsonContent.Create(new { cardNumber }));
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendSweepAsync(
        HttpClient client,
        string token,
        string key)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/payments/dunning-sweep",
            token);
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<InvoiceResponse[]> GetInvoicesAsync(HttpClient client, string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(HttpMethod.Get, "/api/invoices/", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<InvoiceResponse[]>() ?? [];
    }

    private static async Task<BillingCustomerResponse> CreateCustomerAsync(HttpClient client, string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/customers/",
            token,
            JsonContent.Create(new
            {
                name = "Billing Customer",
                email = ApiTestClient.UniqueEmail("billing-customer")
            }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingCustomerResponse>())!;
    }

    private static async Task<BillingProductResponse> CreateProductAsync(HttpClient client, string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/products/",
            token,
            JsonContent.Create(new { name = "Billing Product", description = "M4", active = true }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingProductResponse>())!;
    }

    private static async Task<BillingPriceResponse> CreatePriceAsync(
        HttpClient client,
        string token,
        Guid productId,
        long amountCents)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/prices/",
            token,
            JsonContent.Create(new
            {
                productId,
                pricingModel = 0,
                currency = "USD",
                billingInterval = 0,
                flatUnitAmountCents = amountCents,
                trialDays = (int?)null
            }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingPriceResponse>())!;
    }

    private static async Task<BillingSubscriptionResponse> CreateSubscriptionAsync(
        HttpClient client,
        string token,
        Guid customerId,
        Guid priceId)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/subscriptions/",
            token,
            JsonContent.Create(new { customerId, priceId }));
        request.Headers.Add("Idempotency-Key", $"billing-create-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingSubscriptionResponse>())!;
    }
}

internal sealed record BillingCustomerResponse(Guid Id, Guid OrganizationId, string Name, string Email);

internal sealed record BillingProductResponse(Guid Id, Guid OrganizationId, string Name, string Description, bool Active);

internal sealed record BillingPriceResponse(Guid Id, Guid ProductId, Guid OrganizationId);

internal sealed record BillingSubscriptionResponse(
    Guid Id,
    Guid OrganizationId,
    Guid CustomerId,
    Guid PriceId,
    int Status,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    DateTimeOffset? TrialEnd,
    int? SeatCount,
    DateTimeOffset? CanceledAt,
    DateTimeOffset CreatedAt,
    int Version);

internal sealed record InvoiceResponse(
    Guid Id,
    Guid OrganizationId,
    Guid CustomerId,
    Guid? SubscriptionId,
    int Status,
    string InvoiceNumber,
    string Currency,
    InvoiceLineResponse[] LineItems,
    long Subtotal,
    long Total,
    DateTimeOffset IssueDate,
    DateTimeOffset DueDate,
    DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt,
    int DunningAttemptCount,
    DateTimeOffset? NextRetryAt);

internal sealed record InvoiceLineResponse(Guid Id, string Description, long AmountCents, int LineType);

internal sealed record PaymentResponse(PaymentAttemptResponse Attempt, InvoiceResponse Invoice);

internal sealed record PaymentAttemptResponse(
    Guid Id,
    Guid OrganizationId,
    Guid InvoiceId,
    string CardNumberLast4,
    int Outcome,
    DateTimeOffset AttemptedAt,
    int AttemptNumber);

internal sealed record DunningSweepResponse(
    int Considered,
    int Processed,
    int Succeeded,
    int Failed,
    int BecameUncollectible);

internal sealed class ThrowOnRecoverSubscriptionStateService(
    ISubscriptionService subscriptionService) : ISubscriptionPaymentStateService
{
    public Task<SubscriptionSummary?> GetAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        subscriptionService.GetAsync(organizationId, subscriptionId, cancellationToken);

    public Task<SubscriptionSummary?> MarkPastDueAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test subscription state update failed.");

    public Task<SubscriptionSummary?> MarkUnpaidAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test subscription state update failed.");

    public Task<SubscriptionSummary?> RecoverAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test subscription state update failed.");
}

internal sealed class ThrowOnPaymentStateReadService : ISubscriptionPaymentStateService
{
    public Task<SubscriptionSummary?> GetAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test subscription state read failed.");

    public Task<SubscriptionSummary?> MarkPastDueAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test subscription state update failed.");

    public Task<SubscriptionSummary?> MarkUnpaidAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test subscription state update failed.");

    public Task<SubscriptionSummary?> RecoverAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The test subscription state update failed.");
}
