using System.Net;
using System.Net.Http.Json;
using BillingPlatform.Reporting.Application;
using BillingPlatform.Reporting.Infrastructure.Persistence;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.SimulationClock.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportingIntegrationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Late_older_transition_adds_its_movement_without_replacing_the_newer_snapshot()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await ApiTestClient.RegisterAsync(
            client, "Out of order reporting", ApiTestClient.UniqueEmail("reporting-order"));
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ISubscriptionRevenueEventWriter>();
        var reporting = scope.ServiceProvider.GetRequiredService<IReportingService>();
        var now = scope.ServiceProvider.GetRequiredService<IVirtualClock>().Now;
        var subscriptionId = Guid.NewGuid();
        var firstPrice = new RevenuePriceTerms(
            Guid.NewGuid(), 1, "USD", "Flat", "Month", 1000, null, []);
        var secondPrice = new RevenuePriceTerms(
            Guid.NewGuid(), 1, "USD", "Flat", "Month", 2000, null, []);
        var firstState = new SubscriptionRevenueState(
            "Active", firstPrice.PriceId, null, 1, firstPrice);
        var secondState = new SubscriptionRevenueState(
            "Active", secondPrice.PriceId, null, 2, secondPrice);

        await writer.AppendAsync(new SubscriptionRevenueEvent(
            Guid.NewGuid(), owner.User.OrganizationId, subscriptionId, "changed",
            now.AddMinutes(1), firstState, secondState));
        Assert.Equal(24_000L, Assert.Single(await reporting.CurrentAsync(
            owner.User.OrganizationId)).ArrCents);

        await writer.AppendAsync(new SubscriptionRevenueEvent(
            Guid.NewGuid(), owner.User.OrganizationId, subscriptionId, "created",
            now, null, firstState));
        await reporting.RebuildAsync(owner.User.OrganizationId);

        Assert.Equal(24_000L, Assert.Single(await reporting.CurrentAsync(
            owner.User.OrganizationId)).ArrCents);
        var waterfall = Assert.Single(await reporting.WaterfallAsync(
            owner.User.OrganizationId, now.AddMinutes(-1), now.AddMinutes(2)));
        Assert.Equal(12_000L, waterfall.NewArrCents);
        Assert.Equal(12_000L, waterfall.ExpansionArrCents);
        Assert.Equal(24_000L, waterfall.EndingArrCents);
    }

    [Fact]
    public async Task Out_of_order_ingest_with_a_version_gap_reconciles_the_waterfall_to_the_final_snapshot()
    {
        // Regression for a Codex QA BLOCKER finding: a movement's "before" amount must come from
        // the fact's own immutable Before terms, never from the live replay snapshot, or a version
        // gap under out-of-order ingestion silently drops/duplicates delta so the waterfall no
        // longer reconciles to the final snapshot.
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await ApiTestClient.RegisterAsync(
            client, "Gap reporting", ApiTestClient.UniqueEmail("reporting-gap"));
        await using var scope = factory.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ISubscriptionRevenueEventWriter>();
        var reporting = scope.ServiceProvider.GetRequiredService<IReportingService>();
        var now = scope.ServiceProvider.GetRequiredService<IVirtualClock>().Now;
        var subscriptionId = Guid.NewGuid();
        var price100 = new RevenuePriceTerms(Guid.NewGuid(), 1, "USD", "Flat", "Year", 100, null, []);
        var price200 = new RevenuePriceTerms(Guid.NewGuid(), 1, "USD", "Flat", "Year", 200, null, []);
        var v1After = new SubscriptionRevenueState("Active", price100.PriceId, null, 1, price100);
        var v2After = new SubscriptionRevenueState("Active", price200.PriceId, null, 2, price200);
        var v3After = new SubscriptionRevenueState("Canceled", price200.PriceId, null, 3, price200);

        // Ingest order v1, v3, v2 — v2 (the true chronological middle event) arrives last,
        // leaving a version gap (1 then 3) at the time v3 is applied.
        await writer.AppendAsync(new SubscriptionRevenueEvent(
            Guid.NewGuid(), owner.User.OrganizationId, subscriptionId, "created",
            now, null, v1After));
        await writer.AppendAsync(new SubscriptionRevenueEvent(
            Guid.NewGuid(), owner.User.OrganizationId, subscriptionId, "canceled",
            now.AddMinutes(2), v2After, v3After));
        await writer.AppendAsync(new SubscriptionRevenueEvent(
            Guid.NewGuid(), owner.User.OrganizationId, subscriptionId, "changed",
            now.AddMinutes(1), v1After, v2After));

        var waterfall = Assert.Single(await reporting.WaterfallAsync(
            owner.User.OrganizationId, now.AddMinutes(-1), now.AddMinutes(3)));
        Assert.Equal(100L, waterfall.NewArrCents);
        Assert.Equal(100L, waterfall.ExpansionArrCents);
        Assert.Equal(200L, waterfall.ChurnArrCents);
        Assert.Equal(0L, waterfall.EndingArrCents);
        Assert.Equal(0L, Assert.Single(await reporting.CurrentAsync(
            owner.User.OrganizationId)).ArrCents);

        await reporting.RebuildAsync(owner.User.OrganizationId);
        var rebuilt = Assert.Single(await reporting.WaterfallAsync(
            owner.User.OrganizationId, now.AddMinutes(-1), now.AddMinutes(3)));
        Assert.Equal(0L, rebuilt.EndingArrCents);
    }

    [Fact]
    public async Task Price_mutation_revalues_contracts_and_rebuild_preserves_audit_and_tenant_isolation()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var owner = await ApiTestClient.RegisterAsync(
            client, "Reporting owner", ApiTestClient.UniqueEmail("reporting-owner"));
        var other = await ApiTestClient.RegisterAsync(
            client, "Reporting other", ApiTestClient.UniqueEmail("reporting-other"));

        var customer = await CreateAsync(client, owner.Token, "/api/customers/", new
        {
            name = "Revenue customer",
            email = ApiTestClient.UniqueEmail("reporting-customer")
        });
        var product = await CreateAsync(client, owner.Token, "/api/catalog/products/", new
        {
            name = "Revenue product",
            active = true
        });
        var price = await CreateAsync(client, owner.Token, "/api/catalog/prices/", new
        {
            productId = product,
            pricingModel = 0,
            currency = "USD",
            billingInterval = 0,
            flatUnitAmountCents = 1000
        });
        var subscription = await CreateAsync(client, owner.Token, "/api/subscriptions/", new
        {
            customerId = customer,
            priceId = price
        }, idempotencyKey: "reporting-create");

        var initial = await SummaryAsync(client, owner.Token);
        Assert.Single(initial);
        Assert.Equal("USD", initial[0].Currency);
        Assert.Equal(12_000L, initial[0].ArrCents);
        Assert.Equal(1000L, initial[0].MrrCents);
        Assert.Empty(await SummaryAsync(client, other.Token));

        using (var update = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Put, $"/api/catalog/prices/{price}", owner.Token,
                   JsonContent.Create(new
                   {
                       pricingModel = 0,
                       currency = "USD",
                       billingInterval = 0,
                       flatUnitAmountCents = 1500
                   })))
        using (var response = await client.SendAsync(update))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var changed = await SummaryAsync(client, owner.Token);
        Assert.Equal(18_000L, Assert.Single(changed).ArrCents);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var sourceEvents = await db.SourceEvents.AsNoTracking()
                .Where(item => item.OrganizationId == owner.User.OrganizationId)
                .OrderBy(item => item.IngestOrder)
                .ToListAsync();
            Assert.Equal(2, sourceEvents.Count);
            Assert.Equal("subscription.transition", sourceEvents[0].SourceType);
            Assert.Equal("catalog.price_updated", sourceEvents[1].SourceType);
            using (var originalPayload = System.Text.Json.JsonDocument.Parse(sourceEvents[0].PayloadJson))
            {
                Assert.Equal(1000L, originalPayload.RootElement.GetProperty("after")
                    .GetProperty("price").GetProperty("flatUnitAmountCents").GetInt64());
            }
            Assert.Equal(2, await db.MrrMovements.CountAsync(item =>
                item.OrganizationId == owner.User.OrganizationId));

            using (var waterfallRequest = ApiTestClient.AuthorizedRequest(
                       HttpMethod.Get,
                       $"/api/reporting/waterfall?from={Uri.EscapeDataString(sourceEvents[0].OccurredAt.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(sourceEvents[1].OccurredAt.AddDays(1).ToString("O"))}",
                       owner.Token))
            using (var waterfallResponse = await client.SendAsync(waterfallRequest))
            {
                Assert.Equal(HttpStatusCode.OK, waterfallResponse.StatusCode);
                var waterfall = await waterfallResponse.Content.ReadFromJsonAsync<WaterfallTotals[]>();
                Assert.NotNull(waterfall);
                var usd = Assert.Single(waterfall);
                Assert.Equal(12_000L, usd.NewArrCents);
                Assert.Equal(6_000L, usd.ExpansionArrCents);
                Assert.Equal(18_000L, usd.EndingArrCents);
            }

            using var crossTenant = ApiTestClient.AuthorizedRequest(
                HttpMethod.Get, $"/api/reporting/events/{sourceEvents[0].SourceEventId}", other.Token);
            using var missing = ApiTestClient.AuthorizedRequest(
                HttpMethod.Get, $"/api/reporting/events/{Guid.NewGuid()}", other.Token);
            using var crossTenantResponse = await client.SendAsync(crossTenant);
            using var missingResponse = await client.SendAsync(missing);
            Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
            Assert.Equal(missingResponse.StatusCode, crossTenantResponse.StatusCode);

            var writer = scope.ServiceProvider.GetRequiredService<ISubscriptionRevenueEventWriter>();
            var terms = new RevenuePriceTerms(price, 2, "USD", "Flat", "Month", 1500, null, []);
            await writer.AppendAsync(new SubscriptionRevenueEvent(
                Guid.NewGuid(), owner.User.OrganizationId, subscription, "past_due",
                sourceEvents[1].OccurredAt, new SubscriptionRevenueState(
                    "Active", price, null, 1, terms),
                new SubscriptionRevenueState("PastDue", price, null, 2, terms)));
            Assert.Equal(18_000L, Assert.Single(await SummaryAsync(client, owner.Token)).AtRiskArrCents);
            var older = new SubscriptionRevenueEvent(
                Guid.NewGuid(), owner.User.OrganizationId, subscription, "late_duplicate_version",
                sourceEvents[0].OccurredAt.AddDays(-1), null,
                new SubscriptionRevenueState("Active", price, null, 1,
                    new RevenuePriceTerms(price, 1, "USD", "Flat", "Month",
                        999, null, [])));
            await writer.AppendAsync(older);
            await writer.AppendAsync(older);
            Assert.Equal(4, await db.SourceEvents.CountAsync(item =>
                item.OrganizationId == owner.User.OrganizationId));
            Assert.Equal(2, await db.MrrMovements.CountAsync(item =>
                item.OrganizationId == owner.User.OrganizationId));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IReportingService>()
                .RebuildAsync(owner.User.OrganizationId);
        }
        Assert.Equal(18_000L, Assert.Single(await SummaryAsync(client, owner.Token)).ArrCents);
    }

    private static async Task<Guid> CreateAsync(
        HttpClient client,
        string token,
        string path,
        object body,
        string? idempotencyKey = null)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post, path, token, JsonContent.Create(body));
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<RevenueTotals[]> SummaryAsync(HttpClient client, string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get, "/api/reporting/summary", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<RevenueTotals[]>() ?? [];
    }
}
