using BillingPlatform.Billing.Application;
using BillingPlatform.Billing.Domain;
using BillingPlatform.Billing.Infrastructure.Persistence;
using BillingPlatform.Organizations.Application;
using BillingPlatform.SimulationClock.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BillingPlatform.Billing.Infrastructure;

internal sealed class InvoiceService(
    BillingDbContext dbContext,
    IOrganizationBillingReader organizationReader,
    IVirtualClock clock) : IInvoiceService
{
    public async Task<InvoiceSummary?> CreateOpenAsync(
        Guid organizationId,
        CreateOpenInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Invoices
            .Include(invoice => invoice.LineItems)
            .Where(invoice => invoice.OrganizationId == organizationId)
            .Where(invoice => invoice.SourceOperationKey == command.SourceOperationKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return ToSummary(existing);
        }

        var organization = await organizationReader.GetBillingDetailsAsync(organizationId, cancellationToken)
            ?? throw new ArgumentException("The organization was not found.", nameof(organizationId));
        if (!string.Equals(organization.DefaultCurrency, command.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invoice currency must match the organization currency.", nameof(command));
        }

        var now = clock.Now.ToUniversalTime();
        await using var ownedTransaction = await BeginOwnedTransactionAsync(cancellationToken);
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("An invoice transaction is required.");
        try
        {
            var sequenceNumber = await NextInvoiceNumberAsync(organizationId, transaction, cancellationToken);
            var invoice = Invoice.Create(
                Guid.NewGuid(),
                organizationId,
                command.CustomerId,
                command.SubscriptionId,
                $"{organization.InvoicePrefix}-{sequenceNumber:D4}",
                command.Currency,
                now,
                command.DueDate ?? now,
                now,
                command.SourceOperationKey);
            foreach (var item in command.LineItems)
            {
                invoice.AddLineItem(item.Description, item.AmountCents, item.LineType);
            }

            invoice.Open();
            dbContext.Invoices.Add(invoice);
            await dbContext.SaveChangesAsync(cancellationToken);
            await CommitOwnedAsync(ownedTransaction, cancellationToken);
            return ToSummary(invoice);
        }
        catch (DbUpdateException exception) when (IsDuplicateSource(exception))
        {
            await RollbackOwnedAsync(ownedTransaction, cancellationToken);
            dbContext.ChangeTracker.Clear();
            var replay = await dbContext.Invoices
                .AsNoTracking()
                .Include(invoice => invoice.LineItems)
                .Where(invoice => invoice.OrganizationId == organizationId)
                .Where(invoice => invoice.SourceOperationKey == command.SourceOperationKey)
                .SingleOrDefaultAsync(cancellationToken);
            if (replay is null)
            {
                throw new InvalidOperationException("The duplicate invoice could not be reloaded.");
            }

            return ToSummary(replay);
        }
    }

    public async Task<IReadOnlyList<InvoiceSummary>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        (await dbContext.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.LineItems)
            .Where(invoice => invoice.OrganizationId == organizationId)
            .OrderByDescending(invoice => invoice.IssueDate)
            .ThenBy(invoice => invoice.Id)
            .ToListAsync(cancellationToken))
        .Select(ToSummary)
        .ToList();

    public async Task<InvoiceSummary?> GetAsync(
        Guid organizationId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await dbContext.Invoices
            .AsNoTracking()
            .Include(item => item.LineItems)
            .Where(item => item.OrganizationId == organizationId)
            .Where(item => item.Id == invoiceId)
            .SingleOrDefaultAsync(cancellationToken);
        return invoice is null ? null : ToSummary(invoice);
    }

    public Task<InvoiceSummary?> VoidAsync(
        Guid organizationId,
        Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(organizationId, invoiceId, invoice => invoice.MarkVoid(), cancellationToken);

    public Task<InvoiceSummary?> MarkPaidAsync(
        Guid organizationId,
        Guid invoiceId,
        DateTimeOffset paidAt,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(organizationId, invoiceId, invoice => invoice.MarkPaid(paidAt), cancellationToken);

    public Task<InvoiceSummary?> RecordPaymentFailureAsync(
        Guid organizationId,
        Guid invoiceId,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(organizationId, invoiceId, invoice => invoice.RecordPaymentFailure(attemptedAt), cancellationToken);

    private async Task<InvoiceSummary?> UpdateAsync(
        Guid organizationId,
        Guid invoiceId,
        Action<Invoice> update,
        CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices
            .Include(item => item.LineItems)
            .Where(item => item.OrganizationId == organizationId)
            .Where(item => item.Id == invoiceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        update(invoice);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            throw new InvoiceConcurrencyException();
        }

        return ToSummary(invoice);
    }

    private async Task<long> NextInvoiceNumberAsync(
        Guid organizationId,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO billing.invoice_number_sequences (organization_id, next_number)
            VALUES (@organization_id, 2)
            ON CONFLICT (organization_id) DO UPDATE
            SET next_number = billing.invoice_number_sequences.next_number + 1
            RETURNING next_number - 1;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@organization_id";
        parameter.Value = organizationId;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<IDbContextTransaction?> BeginOwnedTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static Task CommitOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private static Task RollbackOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.RollbackAsync(cancellationToken);

    private static bool IsDuplicateSource(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgresException.ConstraintName == "ix_invoices_organization_id_source_operation_key";

    private static InvoiceSummary ToSummary(Invoice invoice) =>
        new(
            invoice.Id,
            invoice.OrganizationId,
            invoice.CustomerId,
            invoice.SubscriptionId,
            invoice.Status,
            invoice.InvoiceNumber,
            invoice.Currency,
            invoice.LineItems.Select(item => new InvoiceLineItemSummary(
                item.Id, item.Description, item.AmountCents, item.LineType)).ToList(),
            invoice.Subtotal,
            invoice.Total,
            invoice.IssueDate,
            invoice.DueDate,
            invoice.PaidAt,
            invoice.CreatedAt,
            invoice.DunningAttemptCount,
            invoice.NextRetryAt);
}
