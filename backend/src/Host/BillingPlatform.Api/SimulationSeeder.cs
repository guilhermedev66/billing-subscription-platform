using BillingPlatform.Catalog.Application;
using BillingPlatform.Catalog.Domain;
using BillingPlatform.Customers.Application;
using BillingPlatform.Subscriptions.Application;

namespace BillingPlatform.Api;

internal sealed class SimulationSeeder(
    ICustomerService customerService,
    IProductService productService,
    IPriceService priceService,
    ISubscriptionService subscriptionService,
    ISubscriptionMutationCoordinator subscriptionMutationCoordinator)
{
    public async Task<SimulationSeedResult> SeedAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var customers = await customerService.ListAsync(organizationId, cancellationToken);
        var customer = customers.FirstOrDefault() ?? await customerService.CreateAsync(
            organizationId,
            new CreateCustomerCommand("Demo Customer", $"demo-{organizationId:N}@example.test", 0, false),
            cancellationToken);

        var products = await productService.ListAsync(organizationId, cancellationToken);
        var product = products.FirstOrDefault() ?? await productService.CreateAsync(
            organizationId,
            new CreateProductCommand("Simulation Pro", "Demo recurring plan", true),
            cancellationToken);

        var prices = await priceService.ListAsync(organizationId, product.Id, cancellationToken);
        var price = prices.FirstOrDefault() ?? await priceService.CreateAsync(
            organizationId,
            new CreatePriceCommand(
                product.Id, PricingModel.Flat, "USD", BillingInterval.Month,
                2900, null, null, null, [], null),
            cancellationToken) ?? throw new InvalidOperationException("The demo price could not be created.");

        var subscriptions = await subscriptionService.ListAsync(organizationId, cancellationToken);
        var subscription = subscriptions.FirstOrDefault(subscription => subscription.PriceId == price.Id);
        if (subscription is null)
        {
            subscription = (await subscriptionMutationCoordinator.CreateAsync(
                organizationId,
                new CreateSubscriptionCommand(customer.Id, price.Id, null),
                "simulation-seed-subscription",
                cancellationToken))?.Value;
        }

        return new SimulationSeedResult(customer, product, price, subscription);
    }
}

internal sealed record SimulationSeedResult(
    CustomerSummary Customer,
    ProductSummary Product,
    PriceSummary Price,
    SubscriptionSummary? Subscription);
