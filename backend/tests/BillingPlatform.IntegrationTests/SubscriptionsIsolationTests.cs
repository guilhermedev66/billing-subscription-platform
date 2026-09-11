using System.Net;
using System.Net.Http.Json;
using BillingPlatform.Subscriptions.Infrastructure.Persistence;
using BillingPlatform.SimulationClock.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Subscriptions.Domain;

namespace BillingPlatform.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SubscriptionsIsolationTests(PostgreSqlFixture database)
{
    [Fact]
    public async Task Subscriptions_are_tenant_isolated_and_plan_change_returns_proration_receipt()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organizationA = await ApiTestClient.RegisterAsync(
            client,
            "Subscription Organization A",
            ApiTestClient.UniqueEmail("subscription-a"));
        var organizationB = await ApiTestClient.RegisterAsync(
            client,
            "Subscription Organization B",
            ApiTestClient.UniqueEmail("subscription-b"));

        var customer = await CreateCustomerAsync(client, organizationA.Token);
        var product = await CreateProductAsync(client, organizationA.Token);
        var currentPrice = await CreatePriceAsync(client, organizationA.Token, product.Id, 2900, null);
        var newPrice = await CreatePriceAsync(client, organizationA.Token, product.Id, null, 1200);
        var subscription = await CreateSubscriptionAsync(
            client,
            organizationA.Token,
            customer.Id,
            currentPrice.Id,
            null);

        using (var list = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   "/api/subscriptions/",
                   organizationB.Token))
        using (var listResponse = await client.SendAsync(list))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var subscriptions = await listResponse.Content
                .ReadFromJsonAsync<SubscriptionResponse[]>();
            Assert.NotNull(subscriptions);
            Assert.Empty(subscriptions);
        }

        using (var crossTenantGet = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Get,
                   $"/api/subscriptions/{subscription.Id}",
                   organizationB.Token))
        using (var crossTenantResponse = await client.SendAsync(crossTenantGet))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
        }

        using var crossTenantApply = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/apply-change",
                   organizationB.Token,
                   JsonContent.Create(new { newPriceId = newPrice.Id }));
        crossTenantApply.Headers.Add("Idempotency-Key", "cross-tenant-apply");
        using (var crossTenantResponse = await client.SendAsync(crossTenantApply))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
        }

        using (var preview = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/preview-proration",
                   organizationA.Token,
                   JsonContent.Create(new
                   {
                       newPriceId = newPrice.Id,
                       newSeatCount = 3
                   })))
        using (var previewResponse = await client.SendAsync(preview))
        {
            Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
            var receipt = await previewResponse.Content
                .ReadFromJsonAsync<SubscriptionReceiptResponse>();
            Assert.NotNull(receipt);
            Assert.Equal(currentPrice.Id, receipt.Proration.CurrentPlan.PriceId);
            Assert.Equal(newPrice.Id, receipt.Proration.NewPlan.PriceId);
            Assert.True(receipt.Proration.RemainingCycleDays > 0);
            Assert.Equal(3600L, receipt.Proration.NextRegularRenewalAmountCents);
            Assert.Equal(currentPrice.Id, receipt.Subscription.PriceId);
        }

        using var apply = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/apply-change",
                   organizationA.Token,
                   JsonContent.Create(new
                   {
                       newPriceId = newPrice.Id,
                       newSeatCount = 3,
                       cardNumber = "4242 4242 4242 4242"
                   }));
        apply.Headers.Add("Idempotency-Key", "initial-apply");
        using (var applyResponse = await client.SendAsync(apply))
        {
            Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);
            var receipt = await applyResponse.Content
                .ReadFromJsonAsync<SubscriptionReceiptResponse>();
            Assert.NotNull(receipt);
            Assert.Equal(newPrice.Id, receipt.Subscription.PriceId);
            Assert.Equal(3, receipt.Subscription.SeatCount);
            Assert.Equal(3600L, receipt.Proration.NextRegularRenewalAmountCents);
        }

        using var cancel = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/cancel",
                   organizationA.Token);
        cancel.Headers.Add("Idempotency-Key", "first-cancel");
        using (var cancelResponse = await client.SendAsync(cancel))
        {
            Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
            var canceled = await cancelResponse.Content.ReadFromJsonAsync<SubscriptionResponse>();
            Assert.NotNull(canceled);
            Assert.Equal(4, canceled.Status);
        }

        using var cancelAgain = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{subscription.Id}/cancel",
                   organizationA.Token);
        cancelAgain.Headers.Add("Idempotency-Key", "second-cancel");
        using (var cancelAgainResponse = await client.SendAsync(cancelAgain))
        {
            Assert.Equal(HttpStatusCode.Conflict, cancelAgainResponse.StatusCode);
        }
    }

    [Fact]
    public async Task Concurrent_subscription_updates_reject_the_stale_version()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Concurrency Organization",
            ApiTestClient.UniqueEmail("subscription-concurrency"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var perSeatPrice = await CreatePriceAsync(client, organization.Token, product.Id, null, 900);
        var subscription = await CreateSubscriptionAsync(
            client,
            organization.Token,
            customer.Id,
            perSeatPrice.Id,
            2);

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var first = await firstContext.Subscriptions.SingleAsync(item => item.Id == subscription.Id);
        var second = await secondContext.Subscriptions.SingleAsync(item => item.Id == subscription.Id);

        first.ChangeSeatCount(5);
        second.ChangeSeatCount(6);
        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_apply_and_cancel_requests_replay_or_conflict_durably()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Idempotency Organization",
            ApiTestClient.UniqueEmail("subscription-idempotency"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, null, 900);
        var newPrice = await CreatePriceAsync(client, organization.Token, product.Id, null, 1200);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id, 2);

        var firstApply = await SendApplyAsync(
            client, organization.Token, subscription.Id, "duplicate-apply", newPrice.Id, 3);
        var replayedApply = await SendApplyAsync(
            client, organization.Token, subscription.Id, "duplicate-apply", newPrice.Id, 3);
        Assert.Equal(HttpStatusCode.OK, firstApply.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayedApply.StatusCode);
        var firstApplyBody = await firstApply.Content.ReadAsStringAsync();
        var replayedApplyBody = await replayedApply.Content.ReadAsStringAsync();
        Assert.Equal(firstApplyBody, replayedApplyBody);
        firstApply.Dispose();
        replayedApply.Dispose();

        var conflictingApply = await SendApplyAsync(
            client, organization.Token, subscription.Id, "duplicate-apply", newPrice.Id, 4);
        Assert.Equal(HttpStatusCode.Conflict, conflictingApply.StatusCode);
        conflictingApply.Dispose();

        var conflictingCardApply = await SendApplyAsync(
            client,
            organization.Token,
            subscription.Id,
            "duplicate-apply",
            newPrice.Id,
            3,
            "4000 0000 0000 0002");
        Assert.Equal(HttpStatusCode.Conflict, conflictingCardApply.StatusCode);
        conflictingCardApply.Dispose();

        using var firstCancel = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{subscription.Id}/cancel",
            organization.Token);
        firstCancel.Headers.Add("Idempotency-Key", "duplicate-cancel");
        using var firstCancelResponse = await client.SendAsync(firstCancel);
        Assert.Equal(HttpStatusCode.OK, firstCancelResponse.StatusCode);
        var firstCancelBody = await firstCancelResponse.Content.ReadAsStringAsync();

        using var replayedCancel = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{subscription.Id}/cancel",
            organization.Token);
        replayedCancel.Headers.Add("Idempotency-Key", "duplicate-cancel");
        using var replayedCancelResponse = await client.SendAsync(replayedCancel);
        Assert.Equal(HttpStatusCode.OK, replayedCancelResponse.StatusCode);
        Assert.Equal(firstCancelBody, await replayedCancelResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Trialing_changes_are_free_before_and_at_trial_end()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Trial Organization",
            ApiTestClient.UniqueEmail("subscription-trial"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(
            client, organization.Token, product.Id, null, 2000, trialDays: 14);
        var upgradePrice = await CreatePriceAsync(
            client, organization.Token, product.Id, null, 3000);
        var downgradePrice = await CreatePriceAsync(
            client, organization.Token, product.Id, null, 1000);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id, 2);

        var beforeUpgrade = await SendPreviewAsync(
            client, organization.Token, subscription.Id, upgradePrice.Id, 3);
        Assert.Equal(0L, beforeUpgrade.Proration.AmountDueImmediatelyCents);
        Assert.Equal(0L, beforeUpgrade.Proration.ProratedCreditCents);
        Assert.Equal(0L, beforeUpgrade.Proration.ProratedChargeCents);
        Assert.Equal(9000L, beforeUpgrade.Proration.NextRegularRenewalAmountCents);

        var beforeDowngrade = await SendPreviewAsync(
            client, organization.Token, subscription.Id, downgradePrice.Id, 1);
        Assert.Equal(0L, beforeDowngrade.Proration.AmountDueImmediatelyCents);
        Assert.Equal(0L, beforeDowngrade.Proration.ProratedCreditCents);
        Assert.Equal(0L, beforeDowngrade.Proration.ProratedChargeCents);

        var trialEnd = subscription.TrialEnd;
        Assert.NotNull(trialEnd);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IVirtualClock>().SetTime(trialEnd.Value);
        }

        using var apply = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{subscription.Id}/apply-change",
            organization.Token,
            JsonContent.Create(new { newPriceId = upgradePrice.Id, newSeatCount = 3 }));
        apply.Headers.Add("Idempotency-Key", "trial-at-end-apply");
        using var applyResponse = await client.SendAsync(apply);
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);
        var receipt = await applyResponse.Content.ReadFromJsonAsync<SubscriptionReceiptResponse>();
        Assert.NotNull(receipt);
        Assert.Equal(0L, receipt.Proration.AmountDueImmediatelyCents);
        Assert.Equal(0L, receipt.Proration.ProratedCreditCents);
        Assert.Equal(0L, receipt.Proration.ProratedChargeCents);
        Assert.Equal(9000L, receipt.Proration.NextRegularRenewalAmountCents);
        Assert.True(
            (receipt.Subscription.TrialEnd!.Value - trialEnd.Value).Duration()
            <= TimeSpan.FromMicroseconds(1));
        Assert.Equal(0, receipt.Subscription.Status);
    }

    [Fact]
    public async Task Pause_requires_active_and_paused_subscriptions_must_resume_before_changes()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Pause Organization",
            ApiTestClient.UniqueEmail("subscription-pause"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var trialPrice = await CreatePriceAsync(
            client, organization.Token, product.Id, 2000, null, trialDays: 14);
        var activePrice = await CreatePriceAsync(client, organization.Token, product.Id, 2500, null);
        var trialSubscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, trialPrice.Id, null);

        using (var pauseTrial = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{trialSubscription.Id}/pause",
                   organization.Token))
        {
            pauseTrial.Headers.Add("Idempotency-Key", "pause-trial");
            using var response = await client.SendAsync(pauseTrial);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        var activeSubscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, activePrice.Id, null);
        using (var pause = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{activeSubscription.Id}/pause",
                   organization.Token))
        {
            pause.Headers.Add("Idempotency-Key", "pause-active");
            using var response = await client.SendAsync(pause);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var applyWhilePaused = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{activeSubscription.Id}/apply-change",
                   organization.Token,
                   JsonContent.Create(new { newPriceId = trialPrice.Id })))
        {
            applyWhilePaused.Headers.Add("Idempotency-Key", "change-paused");
            using var response = await client.SendAsync(applyWhilePaused);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        using (var resume = ApiTestClient.AuthorizedRequest(
                   HttpMethod.Post,
                   $"/api/subscriptions/{activeSubscription.Id}/resume",
                   organization.Token))
        {
            resume.Headers.Add("Idempotency-Key", "resume-active");
            using var response = await client.SendAsync(resume);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var applyAfterResume = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{activeSubscription.Id}/apply-change",
            organization.Token,
            JsonContent.Create(new { newPriceId = trialPrice.Id }));
        applyAfterResume.Headers.Add("Idempotency-Key", "change-resumed");
        using var applyAfterResumeResponse = await client.SendAsync(applyAfterResume);
        Assert.Equal(HttpStatusCode.OK, applyAfterResumeResponse.StatusCode);
    }

    [Fact]
    public async Task Seat_count_follows_the_target_pricing_model_in_both_directions()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Pricing Transition Organization",
            ApiTestClient.UniqueEmail("subscription-pricing-transition"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var flatPrice = await CreatePriceAsync(client, organization.Token, product.Id, 2500, null);
        var perSeatPrice = await CreatePriceAsync(client, organization.Token, product.Id, null, 900);
        var meteredPrice = await CreateMeteredPriceAsync(client, organization.Token, product.Id);
        var tieredPrice = await CreateTieredPriceAsync(client, organization.Token, product.Id);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, flatPrice.Id, null);

        var flatToSeat = await SendApplyAsync(
            client, organization.Token, subscription.Id, "flat-to-seat", perSeatPrice.Id, 3);
        Assert.Equal(HttpStatusCode.OK, flatToSeat.StatusCode);
        var flatToSeatReceipt = await flatToSeat.Content.ReadFromJsonAsync<SubscriptionReceiptResponse>();
        Assert.NotNull(flatToSeatReceipt);
        Assert.Equal(3, flatToSeatReceipt.Subscription.SeatCount);
        flatToSeat.Dispose();

        var seatToFlat = await SendApplyAsync(
            client, organization.Token, subscription.Id, "seat-to-flat", flatPrice.Id, null);
        Assert.Equal(HttpStatusCode.OK, seatToFlat.StatusCode);
        var seatToFlatReceipt = await seatToFlat.Content.ReadFromJsonAsync<SubscriptionReceiptResponse>();
        Assert.NotNull(seatToFlatReceipt);
        Assert.Null(seatToFlatReceipt.Subscription.SeatCount);
        seatToFlat.Dispose();

        var flatToSeatAgain = await SendApplyAsync(
            client, organization.Token, subscription.Id, "flat-to-seat-again", perSeatPrice.Id, 2);
        Assert.Equal(HttpStatusCode.OK, flatToSeatAgain.StatusCode);
        flatToSeatAgain.Dispose();

        var seatToMetered = await SendApplyAsync(
            client, organization.Token, subscription.Id, "seat-to-metered", meteredPrice.Id, null);
        Assert.Equal(HttpStatusCode.OK, seatToMetered.StatusCode);
        var seatToMeteredReceipt = await seatToMetered.Content.ReadFromJsonAsync<SubscriptionReceiptResponse>();
        Assert.NotNull(seatToMeteredReceipt);
        Assert.Null(seatToMeteredReceipt.Subscription.SeatCount);
        seatToMetered.Dispose();

        var tieredSubscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, tieredPrice.Id, 2);
        var flatToTiered = await SendApplyAsync(
            client, organization.Token, tieredSubscription.Id, "flat-to-tiered", tieredPrice.Id, 2);
        Assert.Equal(HttpStatusCode.OK, flatToTiered.StatusCode);
        flatToTiered.Dispose();

        var tieredToMetered = await SendApplyAsync(
            client, organization.Token, tieredSubscription.Id, "tiered-to-metered", meteredPrice.Id, null);
        Assert.Equal(HttpStatusCode.OK, tieredToMetered.StatusCode);
        var tieredToMeteredReceipt =
            await tieredToMetered.Content.ReadFromJsonAsync<SubscriptionReceiptResponse>();
        Assert.NotNull(tieredToMeteredReceipt);
        Assert.Null(tieredToMeteredReceipt.Subscription.SeatCount);
        tieredToMetered.Dispose();
    }

    [Fact]
    public async Task Concurrent_http_apply_requests_have_one_winner_and_persist_its_state()
    {
        await using var factory = database.CreateFactory();
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "HTTP Concurrency Organization",
            ApiTestClient.UniqueEmail("subscription-http-concurrency"));
        var customer = await CreateCustomerAsync(client, organization.Token);
        var product = await CreateProductAsync(client, organization.Token);
        var currentPrice = await CreatePriceAsync(client, organization.Token, product.Id, null, 800);
        var newPrice = await CreatePriceAsync(client, organization.Token, product.Id, null, 900);
        var subscription = await CreateSubscriptionAsync(
            client, organization.Token, customer.Id, currentPrice.Id, 2);

        var responses = await Task.WhenAll(
            Enumerable.Range(3, 5).Select(seatCount => SendApplyAsync(
                client,
                organization.Token,
                subscription.Id,
                $"http-concurrency-{seatCount}",
                newPrice.Id,
                seatCount)));

        var winners = responses.Where(response => response.StatusCode == HttpStatusCode.OK).ToArray();
        var losers = responses.Where(response => response.StatusCode == HttpStatusCode.Conflict).ToArray();
        Assert.Single(winners);
        Assert.Equal(4, losers.Length);

        var winnerReceipt = await winners[0].Content.ReadFromJsonAsync<SubscriptionReceiptResponse>();
        Assert.NotNull(winnerReceipt);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        using var get = ApiTestClient.AuthorizedRequest(
            HttpMethod.Get,
            $"/api/subscriptions/{subscription.Id}",
            organization.Token);
        using var getResponse = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var persisted = await getResponse.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(persisted);
        Assert.Equal(winnerReceipt.Subscription.SeatCount, persisted.SeatCount);
        Assert.Equal(newPrice.Id, persisted.PriceId);
    }

    [Fact]
    public async Task Create_maps_a_subscription_concurrency_exception_to_conflict()
    {
        await using var factory = database.CreateFactory(services =>
        {
            services.RemoveAll<ISubscriptionService>();
            services.AddScoped<ISubscriptionService, ConcurrencyThrowingSubscriptionService>();
        });
        using var client = factory.CreateClient();
        var organization = await ApiTestClient.RegisterAsync(
            client,
            "Create Conflict Organization",
            ApiTestClient.UniqueEmail("subscription-create-conflict"));

        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/subscriptions/",
            organization.Token,
            JsonContent.Create(new
            {
                customerId = Guid.NewGuid(),
                priceId = Guid.NewGuid()
            }));
        request.Headers.Add("Idempotency-Key", "create-concurrency");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<CustomerResponse> CreateCustomerAsync(
        HttpClient client,
        string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/customers/",
            token,
            JsonContent.Create(new
            {
                name = "Subscription Customer",
                email = ApiTestClient.UniqueEmail("subscription-customer")
            }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = await response.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(customer);
        return customer;
    }

    private static async Task<ProductResponse> CreateProductAsync(
        HttpClient client,
        string token)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/products/",
            token,
            JsonContent.Create(new { name = "Subscription Product", active = true }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        return product;
    }

    private static async Task<PriceResponse> CreatePriceAsync(
        HttpClient client,
        string token,
        Guid productId,
        long? flatAmount,
        long? perSeatAmount,
        int? trialDays = null)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/prices/",
            token,
            JsonContent.Create(new
            {
                productId,
                pricingModel = perSeatAmount is null ? 0 : 1,
                currency = "USD",
                billingInterval = 0,
                flatUnitAmountCents = flatAmount,
                perSeatUnitAmountCents = perSeatAmount,
                trialDays
            }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var price = await response.Content.ReadFromJsonAsync<PriceResponse>();
        Assert.NotNull(price);
        return price;
    }

    private static async Task<PriceResponse> CreateMeteredPriceAsync(
        HttpClient client,
        string token,
        Guid productId)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/prices/",
            token,
            JsonContent.Create(new
            {
                productId,
                pricingModel = 3,
                currency = "USD",
                billingInterval = 0,
                meteredUnitAmountCents = 25,
                meteredAggregation = 0
            }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var price = await response.Content.ReadFromJsonAsync<PriceResponse>();
        Assert.NotNull(price);
        return price;
    }

    private static async Task<PriceResponse> CreateTieredPriceAsync(
        HttpClient client,
        string token,
        Guid productId)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/catalog/prices/",
            token,
            JsonContent.Create(new
            {
                productId,
                pricingModel = 2,
                currency = "USD",
                billingInterval = 0,
                tiers = new object[]
                {
                    new { startingUnit = 1, endingUnit = 5, unitAmountCents = 700 },
                    new { startingUnit = 6, endingUnit = (int?)null, unitAmountCents = 500 }
                }
            }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var price = await response.Content.ReadFromJsonAsync<PriceResponse>();
        Assert.NotNull(price);
        return price;
    }

    private static async Task<SubscriptionReceiptResponse> SendPreviewAsync(
        HttpClient client,
        string token,
        Guid subscriptionId,
        Guid newPriceId,
        int? newSeatCount)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{subscriptionId}/preview-proration",
            token,
            JsonContent.Create(new
            {
                newPriceId,
                newSeatCount,
                cardNumber = "4242 4242 4242 4242"
            }));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<SubscriptionReceiptResponse>();
        Assert.NotNull(receipt);
        return receipt;
    }

    private static async Task<HttpResponseMessage> SendApplyAsync(
        HttpClient client,
        string token,
        Guid subscriptionId,
        string idempotencyKey,
        Guid newPriceId,
        int? newSeatCount,
        string cardNumber = "4242 4242 4242 4242")
    {
        var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            $"/api/subscriptions/{subscriptionId}/apply-change",
            token,
            JsonContent.Create(new
            {
                newPriceId,
                newSeatCount,
                cardNumber
            }));
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<SubscriptionResponse> CreateSubscriptionAsync(
        HttpClient client,
        string token,
        Guid customerId,
        Guid priceId,
        int? seatCount)
    {
        using var request = ApiTestClient.AuthorizedRequest(
            HttpMethod.Post,
            "/api/subscriptions/",
            token,
            JsonContent.Create(new { customerId, priceId, seatCount }));
        request.Headers.Add("Idempotency-Key", $"create-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var subscription = await response.Content.ReadFromJsonAsync<SubscriptionResponse>();
        Assert.NotNull(subscription);
        return subscription;
    }
}

internal sealed record SubscriptionResponse(
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

internal sealed record SubscriptionReceiptResponse(
    SubscriptionResponse Subscription,
    ProrationResponse Proration);

internal sealed record ProrationResponse(
    ProrationPlanResponse CurrentPlan,
    ProrationPlanResponse NewPlan,
    int RemainingCycleDays,
    decimal RemainingCyclePercentage,
    long ProratedCreditCents,
    long ProratedChargeCents,
    long AmountDueImmediatelyCents,
    long NextRegularRenewalAmountCents);

internal sealed record ProrationPlanResponse(
    Guid PriceId,
    string Currency,
    long RecurringAmountCents);

internal sealed class ConcurrencyThrowingSubscriptionService : ISubscriptionService
{
    public Task<SubscriptionMutationResult<SubscriptionSummary>?> CreateAsync(
        Guid organizationId,
        CreateSubscriptionCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new SubscriptionConcurrencyException();

    public Task<IReadOnlyList<SubscriptionSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionSummary>>([]);

    public Task<SubscriptionSummary?> GetAsync(
        Guid organizationId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriptionSummary?>(null);

    public Task<SubscriptionProrationReceipt?> PreviewChangeAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriptionProrationReceipt?>(null);

    public Task<SubscriptionMutationResult<SubscriptionProrationReceipt>?> ApplyChangeAsync(
        Guid organizationId,
        Guid subscriptionId,
        SubscriptionChangeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriptionMutationResult<SubscriptionProrationReceipt>?>(null);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> CancelAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriptionMutationResult<SubscriptionSummary>?>(null);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> PauseAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriptionMutationResult<SubscriptionSummary>?>(null);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> ResumeAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriptionMutationResult<SubscriptionSummary>?>(null);

    public Task<SubscriptionSummary?> RenewAsync(
        Guid organizationId,
        Guid subscriptionId,
        DateTimeOffset now,
        DateTimeOffset newPeriodEnd,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SubscriptionSummary?>(null);
}
