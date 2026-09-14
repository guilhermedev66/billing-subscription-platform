# Reporting read-model design

The module owns the source-event audit log, rebuildable subscription snapshots, the
`reporting.mrr_movements` waterfall ledger, and current read endpoints. Subscription and Catalog
source contracts carry immutable effective price terms and source versions.

## Source facts and ownership

- Reporting owns an immutable, tenant-scoped event log and rebuildable analytics projections. A
  source event ID is the ingestion idempotency key; subscription version orders transitions for a
  subscription. Out-of-order events stay in the audit log but cannot overwrite a newer snapshot.
- Revenue facts must carry the price terms and pre/post subscription state effective at the event.
  Historical MRR must not be recomputed from today's mutable Catalog price.
- Subscription state and price terms define contracted recurring run rate. Billing invoices are
  supporting realized-billing facts; `ProrationDebit`, `ProrationCredit`, and `CreditNote` amounts
  are one-off adjustments and must never be added to MRR.
- Every projection and query is partitioned by `organization_id` and currency. There is no FX
  conversion, so amounts in different currencies are never summed.

## Canonical normalization

Keep integer cents throughout. Store a subscription's fixed run rate as annualized recurring cents:

| Pricing model | Recurring amount for one billing interval |
|---|---|
| Flat | `flat_unit_amount_cents` |
| PerSeat | `per_seat_unit_amount_cents * seat_count` |
| Tiered | Sum the graduated tier amounts for `seat_count`, matching `SubscriptionService` |
| Metered | No committed fixed amount: annualized fixed recurring cents is zero and the subscription is flagged `metered_excluded` |

For monthly prices, annualized cents is `interval amount * 12`; for yearly prices it is the interval
amount. Aggregate ARR is the sum of annualized cents. Aggregate MRR is ARR divided by 12 only after
summing, with display rounding at the API boundary. This avoids per-subscription fractional-cent
rounding drift. Metered usage revenue will be reported separately from invoice/usage facts once a
usage ledger exists; the metered unit rate alone is not MRR.

Trialing subscriptions contribute zero until activation. Active and PastDue subscriptions retain
their fixed run rate; Paused, Unpaid, and Canceled subscriptions contribute zero. Status/reason is
retained so involuntary loss and pauses can be separated later without changing the arithmetic.

## Waterfall attribution

For each effective subscription transition, compare the previous and next annualized fixed run
rate. Use the full next-cycle run rate at the transition time, never the prorated invoice amount:

- `0 -> positive`: New when this is the subscription's first revenue-bearing state; otherwise
  Expansion with a reactivation reason.
- `positive -> larger`: Expansion by the difference.
- `positive -> smaller positive`: Contraction by the difference.
- `positive -> 0`: Churn when the subscription leaves revenue eligibility. A still-active move to
  Metered is Contraction with a `moved_to_metered` reason, not logo churn.
- `0 -> 0`: no fixed-MRR movement; metered/status/audit facts may still change.

Each source event produces at most one movement row under a database uniqueness guard. Multiple
mid-cycle changes therefore telescope from starting to ending run rate instead of counting both
proration lines and contract changes. The reconciliation is:

`ending ARR = starting ARR + new + expansion - contraction - churn`.

`reactivation` is a reason on Expansion movements and a separately exposed subset of that bucket;
it is not added again in the reconciliation. PastDue run rate remains in current ARR/MRR and is
also exposed as at-risk ARR/MRR. The API rounds monthly cents only after summing annualized cents
for each organization and currency.

The source log is append-only under a unique `(organization_id, source_event_id)` guard. Rebuild
replays facts in durable ingestion order, checks subscription and price versions, and leaves late
older facts in the audit log without replacing newer snapshots. Catalog price updates revalue
matching snapshots from the captured after-terms. HTTP reads are `/api/reporting/summary`,
`/api/reporting/waterfall?from=...&to=...`, and `/api/reporting/events/{sourceEventId}`; a
simulation-operator can run `POST /api/reporting/rebuild` for their organization.

The source contracts for subscription creation/change/status and Catalog price updates live in
their respective Application projects. The current Catalog permits price mutation, so Reporting
receives an effective price-change fact and revalues affected subscriptions deliberately rather
than consulting the latest price during a historical rebuild.
