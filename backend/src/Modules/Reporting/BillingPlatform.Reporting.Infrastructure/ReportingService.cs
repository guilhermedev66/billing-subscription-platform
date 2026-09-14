using System.Text.Json;
using BillingPlatform.Catalog.Application;
using BillingPlatform.Reporting.Application;
using BillingPlatform.Reporting.Domain;
using BillingPlatform.Reporting.Infrastructure.Persistence;
using BillingPlatform.SimulationClock.Application;
using BillingPlatform.Subscriptions.Application;
using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Reporting.Infrastructure;

internal sealed class ReportingService(ReportingDbContext dbContext, IVirtualClock clock)
    : IReportingService, ISubscriptionRevenueEventWriter, IPriceMutationEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task AppendAsync(
        SubscriptionRevenueEvent sourceEvent,
        CancellationToken cancellationToken = default) =>
        AppendSourceAsync(
            sourceEvent.SourceEventId, sourceEvent.OrganizationId,
            "subscription.transition", sourceEvent.SubscriptionId,
            sourceEvent.After.PriceId, sourceEvent.After.SubscriptionVersion,
            sourceEvent.After.Price.PriceVersion, sourceEvent.OccurredAt,
            JsonSerializer.Serialize(sourceEvent, JsonOptions), cancellationToken);

    public Task AppendAsync(
        CatalogPriceMutationEvent sourceEvent,
        CancellationToken cancellationToken = default) =>
        AppendSourceAsync(
            sourceEvent.SourceEventId, sourceEvent.OrganizationId,
            "catalog.price_updated", null, sourceEvent.PriceId,
            null, sourceEvent.After.PriceVersion, sourceEvent.OccurredAt,
            JsonSerializer.Serialize(sourceEvent, JsonOptions), cancellationToken);

    private async Task AppendSourceAsync(
        Guid sourceEventId,
        Guid organizationId,
        string sourceType,
        Guid? subscriptionId,
        Guid? priceId,
        int? subscriptionVersion,
        int? priceVersion,
        DateTimeOffset occurredAt,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sourceEventId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        await using var ownedTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Mirrors the inbox shape: a durable tenant + source ID guard. ON CONFLICT lets a
        // concurrent duplicate become a no-op without poisoning an outer Postgres transaction.
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO reporting.source_events
                (id, organization_id, source_event_id, source_type, subscription_id,
                 price_id, subscription_version, price_version, occurred_at, recorded_at, payload_json)
            VALUES
                ({Guid.NewGuid()}, {organizationId}, {sourceEventId}, {sourceType}, {subscriptionId},
                 {priceId}, {subscriptionVersion}, {priceVersion}, {occurredAt.ToUniversalTime()},
                 {clock.Now.ToUniversalTime()}, CAST({payloadJson} AS jsonb))
            ON CONFLICT (organization_id, source_event_id) DO NOTHING
            """, cancellationToken);
        if (inserted != 0)
        {
            await RebuildCoreAsync(organizationId, cancellationToken);
        }

        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }
    }

    public async Task RebuildAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var ownedTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await RebuildCoreAsync(organizationId, cancellationToken);
        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }
    }

    private async Task RebuildCoreAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // An org-scoped transaction lock serializes rebuilds and concurrent source writes.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({organizationId.ToString("D")}, 0))",
            cancellationToken);
        var events = await dbContext.SourceEvents
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .OrderBy(item => item.IngestOrder)
            .ToListAsync(cancellationToken);

        await dbContext.MrrMovements
            .Where(item => item.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.SubscriptionSnapshots
            .Where(item => item.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        var snapshots = new Dictionary<Guid, SubscriptionRevenueSnapshot>();
        var latestPrices = new Dictionary<Guid, CatalogPriceTerms>();
        var seenSubscriptionVersions = new HashSet<(Guid SubscriptionId, int Version)>();
        var movements = new List<MrrMovement>();
        foreach (var source in events)
        {
            if (source.SourceType == "subscription.transition")
            {
                var fact = JsonSerializer.Deserialize<SubscriptionRevenueEvent>(source.PayloadJson, JsonOptions)
                    ?? throw new InvalidOperationException("A subscription source fact is invalid.");
                ApplySubscription(source, fact, latestPrices, seenSubscriptionVersions,
                    snapshots, movements);
            }
            else if (source.SourceType == "catalog.price_updated")
            {
                var fact = JsonSerializer.Deserialize<CatalogPriceMutationEvent>(source.PayloadJson, JsonOptions)
                    ?? throw new InvalidOperationException("A Catalog source fact is invalid.");
                ApplyPrice(source, fact, latestPrices, snapshots, movements);
            }
            else
            {
                throw new InvalidOperationException($"Unknown reporting source type {source.SourceType}.");
            }
        }

        dbContext.SubscriptionSnapshots.AddRange(snapshots.Values);
        dbContext.MrrMovements.AddRange(movements);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplySubscription(
        ReportingSourceEvent source,
        SubscriptionRevenueEvent fact,
        Dictionary<Guid, CatalogPriceTerms> latestPrices,
        HashSet<(Guid SubscriptionId, int Version)> seenSubscriptionVersions,
        Dictionary<Guid, SubscriptionRevenueSnapshot> snapshots,
        List<MrrMovement> movements)
    {
        if (fact.OrganizationId != source.OrganizationId ||
            fact.SourceEventId != source.SourceEventId ||
            fact.SubscriptionId != source.SubscriptionId ||
            fact.After.SubscriptionVersion != source.SubscriptionVersion ||
            fact.After.Price.PriceVersion != source.PriceVersion)
        {
            throw new InvalidOperationException("Subscription event metadata and payload disagree.");
        }

        if (!seenSubscriptionVersions.Add((fact.SubscriptionId, fact.After.SubscriptionVersion)))
        {
            return;
        }

        snapshots.TryGetValue(fact.SubscriptionId, out var previous);
        // A late older transition still supplies its own historical movement, but cannot
        // replace the newer live snapshot. This keeps ledger reconciliation after backfill.
        if (previous is not null && fact.After.SubscriptionVersion <= previous.SubscriptionVersion)
        {
            var historicalBefore = fact.Before is null ? 0 :
                RevenueCalculation.AnnualizedFixedCents(
                    fact.Before.Status, fact.Before.SeatCount, ToPrice(fact.Before.Price));
            var historicalAfter = RevenueCalculation.AnnualizedFixedCents(
                fact.After.Status, fact.After.SeatCount, ToPrice(fact.After.Price));
            AddMovement(source, fact.SubscriptionId,
                fact.Before?.Price.Currency ?? fact.After.Price.Currency,
                fact.After.Price.Currency, historicalBefore, historicalAfter,
                historicalBefore > 0 || fact.Reason is "resume" or "recovered",
                fact.After.Status, fact.After.Price.PricingModel, movements);
            return;
        }

        var afterPrice = ToPrice(fact.After.Price);
        if (latestPrices.TryGetValue(fact.After.PriceId, out var latestPrice) &&
            latestPrice.PriceVersion > afterPrice.Version)
        {
            // A later Catalog fact already revalued this price. The subscription fact remains
            // immutable evidence of the terms it saw, but cannot regress the current price.
            afterPrice = ToPrice(latestPrice);
        }
        var afterStatus = fact.After.Status.ToString();
        var afterAmount = RevenueCalculation.AnnualizedFixedCents(
            afterStatus, fact.After.SeatCount, afterPrice);
        // The fact's own Before/After terms are immutable evidence captured at mutation time and
        // must drive this movement, even when a version gap means `previous` (the replay cache)
        // reflects a different, non-adjacent transition. Trusting `previous` here double-counted
        // or dropped delta across an out-of-order gap (QA finding, ingest order v1,v3,v2).
        var beforeAmount = fact.Before is null ? 0 : RevenueCalculation.AnnualizedFixedCents(
            fact.Before.Status.ToString(), fact.Before.SeatCount, ToPrice(fact.Before.Price));
        var beforeCurrency = fact.Before?.Price.Currency ?? afterPrice.Currency;
        var everRevenueBearing = previous?.EverRevenueBearing == true ||
            (fact.Before is not null && beforeAmount > 0);
        AddMovement(source, fact.SubscriptionId, beforeCurrency, afterPrice.Currency,
            beforeAmount, afterAmount, everRevenueBearing, afterStatus,
            afterPrice.PricingModel, movements);

        snapshots[fact.SubscriptionId] = new SubscriptionRevenueSnapshot
        {
            OrganizationId = fact.OrganizationId,
            SubscriptionId = fact.SubscriptionId,
            PriceId = fact.After.PriceId,
            PriceVersion = afterPrice.Version,
            SubscriptionVersion = fact.After.SubscriptionVersion,
            Currency = afterPrice.Currency,
            Status = afterStatus,
            PricingModel = afterPrice.PricingModel,
            SeatCount = fact.After.SeatCount,
            AnnualizedFixedCents = afterAmount,
            MeteredExcluded = afterPrice.PricingModel == "Metered",
            EverRevenueBearing = everRevenueBearing || afterAmount > 0,
            EffectiveAt = source.OccurredAt,
            LastSourceEventId = source.SourceEventId
        };
    }

    private static void ApplyPrice(
        ReportingSourceEvent source,
        CatalogPriceMutationEvent fact,
        Dictionary<Guid, CatalogPriceTerms> latestPrices,
        Dictionary<Guid, SubscriptionRevenueSnapshot> snapshots,
        List<MrrMovement> movements)
    {
        if (fact.OrganizationId != source.OrganizationId ||
            fact.SourceEventId != source.SourceEventId ||
            fact.PriceId != source.PriceId ||
            fact.After.PriceVersion != source.PriceVersion)
        {
            throw new InvalidOperationException("Catalog event metadata and payload disagree.");
        }

        var afterPrice = ToPrice(fact.After);
        if (latestPrices.TryGetValue(fact.PriceId, out var known) &&
            known.PriceVersion >= afterPrice.Version)
        {
            return;
        }

        latestPrices[fact.PriceId] = fact.After;
        foreach (var snapshot in snapshots.Values.Where(item =>
                     item.PriceId == fact.PriceId && item.PriceVersion < afterPrice.Version))
        {
            var afterAmount = RevenueCalculation.AnnualizedFixedCents(
                snapshot.Status, snapshot.SeatCount, afterPrice);
            AddMovement(source, snapshot.SubscriptionId, snapshot.Currency, afterPrice.Currency,
                snapshot.AnnualizedFixedCents, afterAmount, snapshot.EverRevenueBearing,
                snapshot.Status, afterPrice.PricingModel, movements);
            snapshot.PriceVersion = afterPrice.Version;
            snapshot.Currency = afterPrice.Currency;
            snapshot.PricingModel = afterPrice.PricingModel;
            snapshot.AnnualizedFixedCents = afterAmount;
            snapshot.MeteredExcluded = afterPrice.PricingModel == "Metered";
            snapshot.EverRevenueBearing |= afterAmount > 0;
            snapshot.EffectiveAt = source.OccurredAt;
            snapshot.LastSourceEventId = source.SourceEventId;
        }
    }

    private static void AddMovement(
        ReportingSourceEvent source,
        Guid subscriptionId,
        string beforeCurrency,
        string afterCurrency,
        long beforeAmount,
        long afterAmount,
        bool everRevenueBearing,
        string afterStatus,
        string afterPricingModel,
        List<MrrMovement> movements)
    {
        if (beforeAmount > 0 && afterAmount > 0 && beforeCurrency != afterCurrency)
        {
            throw new InvalidOperationException("A revenue transition cannot cross currencies.");
        }

        var attribution = RevenueCalculation.Movement(
            beforeAmount, afterAmount, everRevenueBearing, afterStatus, afterPricingModel);
        if (attribution is null)
        {
            return;
        }

        movements.Add(new MrrMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = source.OrganizationId,
            SubscriptionId = subscriptionId,
            SourceEventId = source.SourceEventId,
            Currency = beforeAmount > 0 ? beforeCurrency : afterCurrency,
            OccurredAt = source.OccurredAt,
            Kind = attribution.Value.Kind,
            Reason = attribution.Value.Reason,
            BeforeAnnualizedCents = beforeAmount,
            AfterAnnualizedCents = afterAmount,
            DeltaAnnualizedCents = checked(afterAmount - beforeAmount)
        });
    }

    private static RevenuePrice ToPrice(RevenuePriceTerms terms) =>
        new(terms.PriceId, terms.PriceVersion, terms.Currency,
            terms.PricingModel, terms.BillingInterval,
            terms.FlatUnitAmountCents, terms.PerSeatUnitAmountCents,
            terms.Tiers.Select(tier => new RevenueTier(
                tier.StartingUnit, tier.EndingUnit, tier.UnitAmountCents)).ToList());

    private static RevenuePrice ToPrice(CatalogPriceTerms terms) =>
        new(terms.PriceId, terms.PriceVersion, terms.Currency,
            terms.PricingModel, terms.BillingInterval,
            terms.FlatUnitAmountCents, terms.PerSeatUnitAmountCents,
            terms.Tiers.Select(tier => new RevenueTier(
                tier.StartingUnit, tier.EndingUnit, tier.UnitAmountCents)).ToList());

    public async Task<IReadOnlyList<RevenueTotals>> CurrentAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await dbContext.SubscriptionSnapshots.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        return snapshots.GroupBy(item => item.Currency)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var arr = group.Sum(item => item.AnnualizedFixedCents);
                var atRiskArr = group.Where(item => item.Status == "PastDue")
                    .Sum(item => item.AnnualizedFixedCents);
                return new RevenueTotals(
                    group.Key, arr, RevenueCalculation.RoundedMonthlyCents(arr),
                    atRiskArr, RevenueCalculation.RoundedMonthlyCents(atRiskArr),
                    group.Count(item => item.AnnualizedFixedCents > 0),
                    group.Count(item => item.MeteredExcluded));
            }).ToList();
    }

    public async Task<IReadOnlyList<WaterfallTotals>> WaterfallAsync(
        Guid organizationId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var movements = await dbContext.MrrMovements.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.OccurredAt < to)
            .ToListAsync(cancellationToken);
        return movements.GroupBy(item => item.Currency)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var starting = group.Where(item => item.OccurredAt < from)
                    .Sum(item => item.DeltaAnnualizedCents);
                var within = group.Where(item => item.OccurredAt >= from).ToList();
                var added = within.Where(item => item.Kind == "New")
                    .Sum(item => item.DeltaAnnualizedCents);
                var expanded = within.Where(item => item.Kind == "Expansion")
                    .Sum(item => item.DeltaAnnualizedCents);
                var reactivated = within.Where(item => item.Reason == "reactivation")
                    .Sum(item => item.DeltaAnnualizedCents);
                var contracted = -within.Where(item => item.Kind == "Contraction")
                    .Sum(item => item.DeltaAnnualizedCents);
                var churned = -within.Where(item => item.Kind == "Churn")
                    .Sum(item => item.DeltaAnnualizedCents);
                var ending = checked(starting + added + expanded - contracted - churned);
                return new WaterfallTotals(
                    group.Key, starting, added, expanded, reactivated,
                    contracted, churned, ending,
                    RevenueCalculation.RoundedMonthlyCents(starting),
                    RevenueCalculation.RoundedMonthlyCents(added),
                    RevenueCalculation.RoundedMonthlyCents(expanded),
                    RevenueCalculation.RoundedMonthlyCents(reactivated),
                    RevenueCalculation.RoundedMonthlyCents(contracted),
                    RevenueCalculation.RoundedMonthlyCents(churned),
                    RevenueCalculation.RoundedMonthlyCents(ending));
            }).ToList();
    }

    public async Task<ReportingEventDetail?> GetEventAsync(
        Guid organizationId,
        Guid sourceEventId,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.SourceEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.SourceEventId == sourceEventId)
            .SingleOrDefaultAsync(cancellationToken);
        return source is null ? null : new ReportingEventDetail(
            source.SourceEventId, source.SourceType, source.SubscriptionId,
            source.PriceId, source.SubscriptionVersion, source.PriceVersion,
            source.OccurredAt, source.RecordedAt,
            JsonSerializer.Deserialize<JsonElement>(source.PayloadJson, JsonOptions));
    }
}
