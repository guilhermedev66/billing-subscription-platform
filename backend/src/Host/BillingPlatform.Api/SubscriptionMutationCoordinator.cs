using BillingPlatform.Subscriptions.Application;
using BillingPlatform.Webhooks.Application;

namespace BillingPlatform.Api;

internal sealed class SubscriptionMutationCoordinator(
    FinancialTransactionCoordinator transactionCoordinator,
    ISubscriptionService subscriptionService,
    IWebhookEventWriter webhookEventWriter) : ISubscriptionMutationCoordinator
{
    public Task<SubscriptionMutationResult<SubscriptionSummary>?> CreateAsync(
        Guid organizationId,
        CreateSubscriptionCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            token => subscriptionService.CreateAsync(organizationId, command, idempotencyKey, token),
            "subscription.created", cancellationToken);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> CancelAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            token => subscriptionService.CancelAsync(organizationId, subscriptionId, idempotencyKey, token),
            "subscription.canceled", cancellationToken);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> PauseAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            token => subscriptionService.PauseAsync(organizationId, subscriptionId, idempotencyKey, token),
            "subscription.paused", cancellationToken);

    public Task<SubscriptionMutationResult<SubscriptionSummary>?> ResumeAsync(
        Guid organizationId,
        Guid subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            token => subscriptionService.ResumeAsync(organizationId, subscriptionId, idempotencyKey, token),
            "subscription.resumed", cancellationToken);

    private Task<SubscriptionMutationResult<SubscriptionSummary>?> ExecuteAsync(
        Func<CancellationToken, Task<SubscriptionMutationResult<SubscriptionSummary>?>> mutation,
        string eventType,
        CancellationToken cancellationToken) =>
        transactionCoordinator.ExecuteAsync(
            async token =>
            {
                var result = await mutation(token);
                if (result is not null && !result.Replayed)
                {
                    await webhookEventWriter.EnqueueAsync(
                        result.Value.OrganizationId, eventType, "subscription",
                        result.Value.Id, new { subscription = result.Value }, token);
                }

                return result;
            }, cancellationToken);
}
