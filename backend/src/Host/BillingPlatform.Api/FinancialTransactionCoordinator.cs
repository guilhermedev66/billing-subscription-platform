using BillingPlatform.Billing.Infrastructure.Persistence;
using BillingPlatform.Payments.Infrastructure.Persistence;
using BillingPlatform.Subscriptions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillingPlatform.Api;

internal sealed class FinancialTransactionCoordinator(
    SubscriptionsDbContext subscriptionsDbContext,
    BillingDbContext billingDbContext,
    PaymentsDbContext paymentsDbContext)
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        var sharedConnection = subscriptionsDbContext.Database.GetDbConnection();
        billingDbContext.Database.SetDbConnection(sharedConnection, contextOwnsConnection: false);
        paymentsDbContext.Database.SetDbConnection(sharedConnection, contextOwnsConnection: false);

        await using var transaction =
            await subscriptionsDbContext.Database.BeginTransactionAsync(cancellationToken);
        await using var billingTransaction = await billingDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(),
            cancellationToken);
        await using var paymentsTransaction = await paymentsDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(),
            cancellationToken);

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
            throw;
        }
    }
}
