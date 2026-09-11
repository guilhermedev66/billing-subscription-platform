using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Identity.Application;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Webhooks.Application;
using BillingPlatform.Webhooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class M5WebhooksAndSimulationIntegrationTests(PostgreSqlFixture database)
{
    private const string WebhookSecret = "m5-test-secret-0123456789abcdef0";

    [Fact]
    public async Task Duplicate_inbound_delivery_is_acknowledged_without_reapplying_effect()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Inbox Organization", ApiTestClient.UniqueEmail("m5-inbox"));
        var endpoint = await RegisterEndpointAsync(client, organization.Token, ["subscription.updated"]);
        var clock = factory.Services.GetRequiredService<IVirtualClock>();
        clock.FastForward(TimeSpan.FromDays(30));
        var aggregateId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var rawBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = eventId,
            type = "subscription.updated",
            aggregateType = "subscription",
            aggregateId,
            sequence = 1,
            occurredAt = clock.Now,
            data = new { status = "active" }
        });
        var signature = WebhookSignature.CreateHeader(WebhookSecret, clock.Now, rawBody);

        using var first = await SendInboundAsync(client, organization.Token, endpoint.Id, rawBody, signature);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<InboundWebhookResponse>();
        Assert.NotNull(firstResult);
        Assert.False(firstResult.Duplicate);
        Assert.True(firstResult.Applied);

        using var duplicate = await SendInboundAsync(client, organization.Token, endpoint.Id, rawBody, signature);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var duplicateResult = await duplicate.Content.ReadFromJsonAsync<InboundWebhookResponse>();
        Assert.NotNull(duplicateResult);
        Assert.True(duplicateResult.Duplicate);
        Assert.False(duplicateResult.Applied);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
        Assert.Equal(1, await db.InboxEntries.CountAsync(item =>
            item.OrganizationId == organization.User.OrganizationId && item.EventId == eventId));
        Assert.Equal(1, await db.Projections.CountAsync(item =>
            item.OrganizationId == organization.User.OrganizationId && item.AggregateId == aggregateId));
    }

    [Fact]
    public async Task Out_of_order_inbound_events_keep_the_highest_sequence_projection()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Ordering Organization", ApiTestClient.UniqueEmail("m5-ordering"));
        var endpoint = await RegisterEndpointAsync(client, organization.Token, ["subscription.updated"]);
        var clock = factory.Services.GetRequiredService<IVirtualClock>();
        var aggregateId = Guid.NewGuid();
        var newer = await SendEventAsync(client, organization.Token, endpoint, aggregateId, 2, "v2", clock);
        Assert.True(newer.Applied);
        var older = await SendEventAsync(client, organization.Token, endpoint, aggregateId, 1, "v1", clock);
        Assert.True(older.Applied);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
        var projection = await db.Projections.SingleAsync(item =>
            item.OrganizationId == organization.User.OrganizationId && item.AggregateId == aggregateId);
        Assert.Equal(2, projection.Sequence);
        Assert.Contains("v2", System.Text.Encoding.UTF8.GetString(projection.RawBody));
    }

    [Fact]
    public async Task Concurrent_renewal_workers_create_one_invoice_and_emit_outbox_events()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Renewal Organization", ApiTestClient.UniqueEmail("m5-renewal"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var price = await CreatePriceAsync(client, organization.Token, product.Id);
        var subscription = await CreateSubscriptionAsync(client, organization.Token, customer.Id, price.Id);
        factory.Services.GetRequiredService<IVirtualClock>().SetTime(subscription.CurrentPeriodEnd);

        var requests = Enumerable.Range(0, 2).Select(_ =>
        {
            var request = ApiTestClient.AuthorizedRequest(
                HttpMethod.Post, "/api/simulation/renewal-cron", organization.Token);
            return client.SendAsync(request);
        }).ToArray();
        var responses = await Task.WhenAll(requests);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        using var invoiceRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get, "/api/invoices/", organization.Token);
        using var invoiceResponse = await client.SendAsync(invoiceRequest);
        Assert.Equal(HttpStatusCode.OK, invoiceResponse.StatusCode);
        var invoices = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceResponse[]>() ?? [];
        Assert.Single(invoices, invoice => invoice.SubscriptionId == subscription.Id);

        using var eventsRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get, "/api/webhooks/events", organization.Token);
        using var eventsResponse = await client.SendAsync(eventsRequest);
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
        var events = await eventsResponse.Content.ReadFromJsonAsync<WebhookEventResponse[]>() ?? [];
        Assert.Contains(events, item => item.EventType == "subscription.renewed");
        Assert.Contains(events, item => item.EventType == "invoice.paid");
    }

    [Fact]
    public async Task Dispatcher_sends_exact_raw_body_with_verifiable_hmac_and_logs_delivery()
    {
        var handler = new CapturingHttpHandler();
        await using var factory = database.CreateFactory(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StaticHttpClientFactory(handler));
        });
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Delivery Organization", ApiTestClient.UniqueEmail("m5-delivery"));
        var endpoint = await RegisterEndpointAsync(client, organization.Token, ["invoice.paid"]);
        handler.StatusCodes.Enqueue(HttpStatusCode.InternalServerError);
        handler.StatusCodes.Enqueue(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IWebhookService>();
            await service.EnqueueAsync(
                organization.User.OrganizationId,
                "invoice.paid",
                "invoice",
                Guid.NewGuid(),
                new { total = 2500 },
                CancellationToken.None);
        }

        using var dispatchRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/webhooks/dispatch", organization.Token);
        using var dispatchResponse = await client.SendAsync(dispatchRequest);
        Assert.Equal(HttpStatusCode.OK, dispatchResponse.StatusCode);
        var clock = factory.Services.GetRequiredService<IVirtualClock>();
        clock.FastForward(TimeSpan.FromSeconds(3));
        using var retryRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/webhooks/dispatch", organization.Token);
        using var retryResponse = await client.SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.NotNull(handler.Body);
        Assert.NotNull(handler.Signature);

        Assert.True(WebhookSignature.Verify(
            handler.Signature!, WebhookSecret, handler.Body!, clock.Now, out _));
        Assert.Equal("application/json", handler.ContentType);

        using var eventsRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get, "/api/webhooks/events", organization.Token);
        using var eventsResponse = await client.SendAsync(eventsRequest);
        var events = await eventsResponse.Content.ReadFromJsonAsync<WebhookEventResponse[]>() ?? [];
        var eventItem = Assert.Single(events);
        Assert.Equal(handler.Body, Convert.FromBase64String(eventItem.RawBody));

        using var deliveriesRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get,
            $"/api/webhooks/events/{eventItem.Id}/deliveries",
            organization.Token);
        using var deliveriesResponse = await client.SendAsync(deliveriesRequest);
        Assert.Equal(HttpStatusCode.OK, deliveriesResponse.StatusCode);
        var deliveries = await deliveriesResponse.Content.ReadFromJsonAsync<WebhookDeliveryResponse[]>() ?? [];
        Assert.Equal(2, deliveries.Length);
        Assert.Equal(500, deliveries[0].StatusCode);
        Assert.Equal(200, deliveries[1].StatusCode);
        Assert.Equal(1, deliveries[1].RetryCount);
        Assert.Equal(endpoint.Id, deliveries[0].EndpointId);
    }

    [Fact]
    public async Task Dispatcher_discards_outcome_when_its_lease_is_reclaimed()
    {
        var handler = new CapturingHttpHandler();
        await using var factory = database.CreateFactory(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StaticHttpClientFactory(handler));
        });
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Lease Organization", ApiTestClient.UniqueEmail("m5-lease"));
        await RegisterEndpointAsync(client, organization.Token, ["invoice.paid"]);
        Guid eventId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            eventId = await scope.ServiceProvider.GetRequiredService<IWebhookService>().EnqueueAsync(
                organization.User.OrganizationId,
                "invoice.paid",
                "invoice",
                Guid.NewGuid(),
                new { total = 2500 },
                CancellationToken.None);
        }

        var replacementLeaseId = Guid.NewGuid();
        handler.BeforeResponse = async cancellationToken =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
            var replacementLeaseUntil = factory.Services.GetRequiredService<IVirtualClock>()
                .Now.AddMinutes(5);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE webhooks.outbox_events
                SET dispatch_lease_id = {replacementLeaseId},
                    dispatch_lease_until = {replacementLeaseUntil}
                WHERE id = {eventId} AND dispatch_lease_id IS NOT NULL;
                """, cancellationToken);
        };

        using var dispatchRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/webhooks/dispatch", organization.Token);
        using var dispatchResponse = await client.SendAsync(dispatchRequest);
        Assert.Equal(HttpStatusCode.OK, dispatchResponse.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
        var outboxEvent = await verificationDb.OutboxEvents.AsNoTracking()
            .SingleAsync(item => item.Id == eventId);
        Assert.Null(outboxEvent.DispatchedAt);
        Assert.Equal(replacementLeaseId, outboxEvent.DispatchLeaseId);
        Assert.Equal(0, await verificationDb.DeliveryAttempts.CountAsync(
            attempt => attempt.OutboxEventId == eventId));
    }

    [Fact]
    public async Task Webhook_endpoint_registration_is_durable_and_idempotent()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Idempotency Organization", ApiTestClient.UniqueEmail("m5-idempotency"));
        const string idempotencyKey = "m5-endpoint-idempotency";

        var requests = Enumerable.Range(0, 2)
            .Select(_ => CreateEndpointRegistrationRequest(
                organization.Token,
                idempotencyKey,
                "http://127.0.0.1:1/webhook",
                ["invoice.paid"]))
            .ToArray();
        var responses = await Task.WhenAll(requests.Select(client.SendAsync));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var responseBodies = await Task.WhenAll(
            responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.Equal(responseBodies[0], responseBodies[1]);
        Assert.Equal(responses[0].Headers.Location, responses[1].Headers.Location);
        foreach (var request in requests)
        {
            request.Dispose();
        }
        foreach (var response in responses)
        {
            response.Dispose();
        }

        using var conflictingRequest = CreateEndpointRegistrationRequest(
            organization.Token,
            idempotencyKey,
            "http://127.0.0.1:2/webhook",
            ["invoice.paid"]);
        using var conflictingResponse = await client.SendAsync(conflictingRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflictingResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
        Assert.Equal(1, await db.Endpoints.CountAsync(
            endpoint => endpoint.OrganizationId == organization.User.OrganizationId));
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync(
            record => record.OrganizationId == organization.User.OrganizationId));
    }

    [Fact]
    public async Task Webhook_endpoint_registration_returns_validation_for_null_event_types()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Validation Organization", ApiTestClient.UniqueEmail("m5-validation"));
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/webhooks/endpoints",
            organization.Token,
            JsonContent.Create(new
            {
                url = "http://127.0.0.1:1/webhook",
                secret = WebhookSecret,
                eventTypes = (string[]?)null
            }));
        request.Headers.Add("Idempotency-Key", "m5-null-event-types");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subscription_change_decline_emits_one_dunning_outcome_even_after_replay()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Dunning Event Organization", ApiTestClient.UniqueEmail("m5-dunning-event"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, 1000);
        var upgradePrice = await CreatePriceAsync(client, organization.Token, product.Id, 2000);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id);
        const string idempotencyKey = "m5-subscription-change-dunning";

        for (var index = 0; index < 2; index++)
        {
            using var changeRequest = ApiTestClient.AuthorizedRequest(
                HttpMethod.Post,
                $"/api/subscriptions/{subscription.Id}/apply-change",
                organization.Token,
                JsonContent.Create(new
                {
                    newPriceId = upgradePrice.Id,
                    cardNumber = "4000 0000 0000 0004"
                }));
            changeRequest.Headers.Add("Idempotency-Key", idempotencyKey);
            using var changeResponse = await client.SendAsync(changeRequest);
            Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);
        }

        using var eventsRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get, "/api/webhooks/events", organization.Token);
        using var eventsResponse = await client.SendAsync(eventsRequest);
        var events = await eventsResponse.Content.ReadFromJsonAsync<WebhookEventResponse[]>() ?? [];
        Assert.Single(events, item => item.EventType == "dunning.outcome");
    }

    [Fact]
    public async Task Webhook_resources_and_simulation_actions_are_tenant_isolated()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organizationA = await ApiTestClient.RegisterAsync(
            client, "M5 Tenant A", ApiTestClient.UniqueEmail("m5-tenant-a"));
        var organizationB = await ApiTestClient.RegisterAsync(
            client, "M5 Tenant B", ApiTestClient.UniqueEmail("m5-tenant-b"));
        var endpointB = await RegisterEndpointAsync(
            client, organizationB.Token, ["subscription.updated"]);
        Guid eventB;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            eventB = await scope.ServiceProvider.GetRequiredService<IWebhookService>().EnqueueAsync(
                organizationB.User.OrganizationId,
                "subscription.updated",
                "subscription",
                Guid.NewGuid(),
                new { status = "active" },
                CancellationToken.None);
        }

        using (var endpointsRequest = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get, "/api/webhooks/endpoints", organizationA.Token))
        using (var endpointsResponse = await client.SendAsync(endpointsRequest))
        {
            Assert.Equal(HttpStatusCode.OK, endpointsResponse.StatusCode);
            Assert.Empty(await endpointsResponse.Content.ReadFromJsonAsync<WebhookEndpointResponse[]>() ?? []);
        }

        using (var eventsRequest = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get, "/api/webhooks/events", organizationA.Token))
        using (var eventsResponse = await client.SendAsync(eventsRequest))
        {
            Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
            Assert.Empty(await eventsResponse.Content.ReadFromJsonAsync<WebhookEventResponse[]>() ?? []);
        }

        using (var deliveriesRequest = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/webhooks/events/{eventB}/deliveries",
                   organizationA.Token))
        using (var deliveriesResponse = await client.SendAsync(deliveriesRequest))
        {
            Assert.Equal(HttpStatusCode.NotFound, deliveriesResponse.StatusCode);
        }

        var foreignBody = JsonSerializer.SerializeToUtf8Bytes(new { id = Guid.NewGuid() });
        using (var inboundResponse = await SendInboundAsync(
                   client,
                   organizationA.Token,
                   endpointB.Id,
                   foreignBody,
                   WebhookSignature.CreateHeader(WebhookSecret, factory.Services.GetRequiredService<IVirtualClock>().Now, foreignBody)))
        {
            Assert.Equal(HttpStatusCode.NotFound, inboundResponse.StatusCode);
        }

        var customerB = await CreateCustomerAsync(client, organizationB.Token);
        var productB = await CreateProductAsync(client, organizationB.Token);
        var priceB = await CreatePriceAsync(client, organizationB.Token, productB.Id);
        var subscriptionB = await CreateSubscriptionAsync(
            client, organizationB.Token, customerB.Id, priceB.Id);
        factory.Services.GetRequiredService<IVirtualClock>().SetTime(subscriptionB.CurrentPeriodEnd);
        using (var cronRequest = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post, "/api/simulation/renewal-cron", organizationA.Token))
        using (var cronResponse = await client.SendAsync(cronRequest))
        {
            Assert.Equal(HttpStatusCode.OK, cronResponse.StatusCode);
        }

        using var subscriptionRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get, $"/api/subscriptions/{subscriptionB.Id}", organizationB.Token);
        using var subscriptionResponse = await client.SendAsync(subscriptionRequest);
        var unchangedSubscription = await subscriptionResponse.Content
            .ReadFromJsonAsync<BillingSubscriptionResponse>();
        Assert.NotNull(unchangedSubscription);
        Assert.Equal(subscriptionB.CurrentPeriodEnd, unchangedSubscription.CurrentPeriodEnd);
    }

    [Fact]
    public async Task Outbox_write_failure_rolls_back_the_payment_and_invoice_transaction()
    {
        await using var factory = database.CreateFactory(services =>
        {
            services.RemoveAll<IWebhookEventWriter>();
            services.AddScoped<IWebhookEventWriter, ThrowingWebhookEventWriter>();
        });
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Atomic Outbox Organization", ApiTestClient.UniqueEmail("m5-atomic-outbox"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        InvoiceSummary invoice;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
            invoice = Assert.IsType<InvoiceSummary>(await service.CreateOpenAsync(
                organization.User.OrganizationId,
                new CreateOpenInvoiceCommand(
                    customer.Id,
                    null,
                    "USD",
                    [new InvoiceLineItemInput("Atomic outbox", 1900, InvoiceLineType.Base)],
                    $"m5-atomic-{Guid.NewGuid():N}"),
                CancellationToken.None));
        }

        using var paymentRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/payments/invoices/{invoice.Id}/attempt",
            organization.Token,
            JsonContent.Create(new { cardNumber = "4242 4242 4242 4242" }));
        paymentRequest.Headers.Add("Idempotency-Key", "m5-atomic-payment");
        using var paymentResponse = await client.SendAsync(paymentRequest);
        Assert.Equal(HttpStatusCode.InternalServerError, paymentResponse.StatusCode);

        using var attemptsRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get,
            $"/api/payments/invoices/{invoice.Id}/attempts",
            organization.Token);
        using var attemptsResponse = await client.SendAsync(attemptsRequest);
        Assert.Empty(await attemptsResponse.Content.ReadFromJsonAsync<PaymentAttemptResponse[]>() ?? []);

        using var invoiceRequest = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get, $"/api/invoices/{invoice.Id}", organization.Token);
        using var invoiceResponse = await client.SendAsync(invoiceRequest);
        var persisted = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceResponse>();
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Status);
    }

    [Fact]
    public async Task Simulation_routes_require_the_simulation_operator_capability()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client, "M5 Simulation Authorization Organization", ApiTestClient.UniqueEmail("m5-sim-auth"));
        string nonOperatorToken;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
            nonOperatorToken = tokenService.Create(
                new IdentityUserInfo(organization.User.Id, organization.User.Email),
                organization.User.OrganizationId).Token;
        }

        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/simulation/advance/1", nonOperatorToken);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<WebhookEndpointResponse> RegisterEndpointAsync(
        HttpClient client,
        string token,
        IReadOnlyList<string> eventTypes,
        string? idempotencyKey = null)
    {
        using var request = CreateEndpointRegistrationRequest(
            token,
            idempotencyKey ?? $"m5-webhook-endpoint-{Guid.NewGuid():N}",
            "http://127.0.0.1:1/webhook",
            eventTypes);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WebhookEndpointResponse>())!;
    }

    private static HttpRequestMessage CreateEndpointRegistrationRequest(
        string token,
        string idempotencyKey,
        string url,
        IReadOnlyList<string> eventTypes)
    {
        var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/webhooks/endpoints",
            token,
            JsonContent.Create(new { url, secret = WebhookSecret, eventTypes }));
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<HttpResponseMessage> SendInboundAsync(
        HttpClient client,
        string token,
        Guid endpointId,
        byte[] rawBody,
        string signature)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/webhooks/{endpointId}/inbound",
            token,
            new ByteArrayContent(rawBody));
        request.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Signature", signature);
        return await client.SendAsync(request);
    }

    private static async Task<InboundWebhookResponse> SendEventAsync(
        HttpClient client,
        string token,
        WebhookEndpointResponse endpoint,
        Guid aggregateId,
        int sequence,
        string marker,
        IVirtualClock clock)
    {
        var eventId = Guid.NewGuid();
        var rawBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = eventId,
            type = "subscription.updated",
            aggregateType = "subscription",
            aggregateId,
            sequence,
            occurredAt = clock.Now,
            data = new { marker }
        });
        using var response = await SendInboundAsync(
            client, token, endpoint.Id, rawBody,
            WebhookSignature.CreateHeader(WebhookSecret, clock.Now, rawBody));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InboundWebhookResponse>())!;
    }

    private static async Task<BillingCustomerResponse> CreateCustomerAsync(HttpClient client, string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/customers/", token,
            JsonContent.Create(new { name = "M5 Customer", email = ApiTestClient.UniqueEmail("m5-customer") }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingCustomerResponse>())!;
    }

    private static async Task<BillingProductResponse> CreateProductAsync(HttpClient client, string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/catalog/products/", token,
            JsonContent.Create(new { name = "M5 Product", description = "Renewal", active = true }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingProductResponse>())!;
    }

    private static async Task<BillingPriceResponse> CreatePriceAsync(
        HttpClient client, string token, Guid productId, long amountCents = 2500)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/catalog/prices/", token,
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
        HttpClient client, string token, Guid customerId, Guid priceId)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, "/api/subscriptions/", token,
            JsonContent.Create(new { customerId, priceId }));
        request.Headers.Add("Idempotency-Key", $"m5-subscription-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingSubscriptionResponse>())!;
    }
}

internal sealed record WebhookEndpointResponse(
    Guid Id,
    Guid OrganizationId,
    string Url,
    string[] EventTypes,
    bool Active,
    DateTimeOffset CreatedAt);

internal sealed record InboundWebhookResponse(
    Guid EventId,
    bool Duplicate,
    bool Applied,
    string EventType,
    int Sequence);

internal sealed record WebhookEventResponse(
    Guid Id,
    Guid OrganizationId,
    string EventType,
    string AggregateType,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? DispatchedAt,
    int DeliveryAttemptCount,
    string RawBody);

internal sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid OutboxEventId,
    Guid EndpointId,
    DateTimeOffset AttemptedAt,
    int? StatusCode,
    long DurationMilliseconds,
    int AttemptNumber,
    int RetryCount,
    string? ResponseBody,
    string? Error);

internal sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    private readonly HttpClient client = new(handler);

    public HttpClient CreateClient(string name) => client;
}

internal sealed class CapturingHttpHandler : HttpMessageHandler
{
    public Queue<HttpStatusCode> StatusCodes { get; } = new();
    public byte[]? Body { get; private set; }
    public string? Signature { get; private set; }
    public string? ContentType { get; private set; }
    public Func<CancellationToken, Task>? BeforeResponse { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        Signature = request.Headers.GetValues("X-Signature").Single();
        ContentType = request.Content.Headers.ContentType?.MediaType;
        if (BeforeResponse is not null)
        {
            await BeforeResponse(cancellationToken);
        }
        var statusCode = StatusCodes.Count == 0 ? HttpStatusCode.OK : StatusCodes.Dequeue();
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("accepted")
        };
    }
}

internal sealed class ThrowingWebhookEventWriter : IWebhookEventWriter
{
    public Task<Guid> EnqueueAsync(
        Guid organizationId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object data,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("simulated outbox failure");
}
