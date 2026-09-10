/**
 * Money is integer cents everywhere in the domain (docs/ARCHITECTURE.md invariant #1).
 * This is the ONLY place cents get converted to a display string — never do float
 * math on money in components, only formatting here.
 */
export function formatCents(cents: number, currency = 'USD'): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(cents / 100)
}

/** Signed variant for customer balances — positive is credit, negative is debt. */
export function formatBalanceCents(cents: number, currency = 'USD'): string {
  const formatted = formatCents(Math.abs(cents), currency)
  if (cents > 0) return `+${formatted} credit`
  if (cents < 0) return `-${formatted} owed`
  return formatted
}
