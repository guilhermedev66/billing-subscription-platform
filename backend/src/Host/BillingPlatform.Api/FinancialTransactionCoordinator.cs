using BillingPlatform.Billing.Infrastructure.Persistence;
using BillingPlatform.Payments.Infrastructure.Persistence;
using BillingPlatform.Subscriptions.Infrastructure.Persistence;
using BillingPlatform.Webhooks.Infrastructure.Persistence;
using BillingPlatform.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillingPlatform.Api;

internal sealed class FinancialTransactionCoordinator(
    SubscriptionsDbContext subscriptionsDbContext,
    BillingDbContext billingDbContext,
    PaymentsDbContext paymentsDbContext,
    WebhooksDbContext webhooksDbContext,
    ReportingDbContext reportingDbContext)
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        var sharedConnection = subscriptionsDbContext.Database.GetDbConnection();
        billingDbContext.Database.SetDbConnection(sharedConnection, contextOwnsConnection: false);
        paymentsDbContext.Database.SetDbConnection(sharedConnection, contextOwnsConnection: false);
        webhooksDbContext.Database.SetDbConnection(sharedConnection, contextOwnsConnection: false);
        reportingDbContext.Database.SetDbConnection(sharedConnection, contextOwnsConnection: false);

        await using var transaction =
            await subscriptionsDbContext.Database.BeginTransactionAsync(cancellationToken);
        await using var billingTransaction = await billingDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(),
            cancellationToken);
        await using var paymentsTransaction = await paymentsDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(),
            cancellationToken);
        await using var webhooksTransaction = await webhooksDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(),
            cancellationToken);
        await using var reportingTransaction = await reportingDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(), cancellationToken);

        try
        {
            var result = await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            subscriptionsDbContext.ChangeTracker.Clear();
            billingDbContext.ChangeTracker.Clear();
            paymentsDbContext.ChangeTracker.Clear();
            webhooksDbContext.ChangeTracker.Clear();
            reportingDbContext.ChangeTracker.Clear();
            throw;
        }
    }
}
