import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { FormField } from '@/components/ui/FormField'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { ApiError } from '@/lib/api/client'
import { attemptPayment, type PaymentAttemptResult } from './api'
import { PAYMENT_OUTCOME_LABEL, PAYMENT_OUTCOME_TONE } from './paymentOutcomeTone'
import { recordAttempt } from './recentAttempts'
import { TEST_CARDS } from './testCards'

const selectClassName = 'h-9 w-full rounded-md border border-border bg-surface px-3 text-sm text-foreground'

interface TestCardSelectorProps {
  invoiceId: string
  /** When false (invoice not `open`), the selector renders but charging is disabled. */
  chargeable: boolean
  disabledReason?: string
  onResult?: (result: PaymentAttemptResult) => void
}

/**
 * The test-card picker used to charge (or dunning-retry — same action) an
 * open invoice. Self-contained: on success it updates the shared invoice
 * query cache, invalidates the durable `['payments','attempts', invoiceId]`
 * ledger query (features/payments/api.ts's listPaymentAttempts — the source
 * of truth, survives a reload), and records into the session-local
 * recent-attempts store (features/payments/recentAttempts.ts, a transient
 * "just happened in this tab" supplement) — so callers on both the invoice
 * detail page and the dunning cockpit can just render this and read the
 * relevant query/hook afterward, no extra wiring required.
 *
 * The 3D Secure test card (4000000000003022) is stateful: charging it
 * returns `requires_3ds` and leaves the invoice untouched (not a decline,
 * not a success — a pending challenge). This component then shows a
 * "confirm challenge" step; confirming re-attempts with the SAME card (a
 * fresh Idempotency-Key is generated automatically inside attemptPayment)
 * and that second attempt is what actually resolves to `succeeded`.
 */
export function TestCardSelector({ invoiceId, chargeable, disabledReason, onResult }: TestCardSelectorProps) {
  const queryClient = useQueryClient()
  const [selectedNumber, setSelectedNumber] = useState(TEST_CARDS[0].number)
  const selectedCard = TEST_CARDS.find((card) => card.number === selectedNumber) ?? TEST_CARDS[0]

  const chargeMutation = useMutation({
    mutationFn: () => attemptPayment(invoiceId, selectedCard.number),
    onSuccess: (result) => {
      queryClient.setQueryData(['invoices', invoiceId], result.invoice)
      queryClient.invalidateQueries({ queryKey: ['invoices'] })
      queryClient.invalidateQueries({ queryKey: ['payments', 'attempts', invoiceId] })
      recordAttempt(result.attempt)
      onResult?.(result)
    },
  })

  const lastResult = chargeMutation.data
  // Only "awaiting" while the still-selected card is the one that raised the challenge —
  // switching to a different card in the dropdown quietly drops the pending confirmation,
  // same as the mock/real backend treating a different card as superseding it.
  const awaitingChallenge =
    lastResult?.attempt.outcome === 'requires_3ds' && lastResult.attempt.cardNumberLast4 === selectedCard.last4

  return (
    <div className="flex flex-col gap-3">
      <FormField id={`test-card-${invoiceId}`} label="Test card">
        <select
          id={`test-card-${invoiceId}`}
          className={selectClassName}
          value={selectedNumber}
          onChange={(event) => {
            setSelectedNumber(event.target.value)
            chargeMutation.reset()
          }}
          disabled={!chargeable || chargeMutation.isPending}
        >
          {TEST_CARDS.map((card) => (
            <option key={card.number} value={card.number}>
              {card.label} — {card.outcomeLabel}
            </option>
          ))}
        </select>
      </FormField>

      <p className="text-xs text-muted-foreground">{selectedCard.description}</p>

      {!chargeable && disabledReason && <p className="text-xs text-muted-foreground">{disabledReason}</p>}

      <div>
        <Button type="button" size="sm" disabled={!chargeable || chargeMutation.isPending} onClick={() => chargeMutation.mutate()}>
          {chargeMutation.isPending ? 'Charging…' : 'Charge invoice'}
        </Button>
      </div>

      {chargeMutation.isError && (
        <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
          {chargeMutation.error instanceof ApiError ? chargeMutation.error.message : "Couldn't charge this invoice."}
        </p>
      )}

      {lastResult && (
        <div role="status" aria-live="polite" className="flex flex-col gap-1 rounded-md border border-border bg-surface-muted px-3 py-2 text-sm">
          <div className="flex items-center gap-2">
            <span className="text-muted-foreground">Result:</span>
            <StatusBadge tone={PAYMENT_OUTCOME_TONE[lastResult.attempt.outcome]}>
              {PAYMENT_OUTCOME_LABEL[lastResult.attempt.outcome]}
            </StatusBadge>
          </div>
          {lastResult.attempt.outcome !== 'succeeded' &&
            lastResult.attempt.outcome !== 'requires_3ds' &&
            lastResult.invoice.status === 'open' &&
            lastResult.invoice.nextRetryAt && (
              <p className="text-xs text-muted-foreground">
                Next retry: {new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(lastResult.invoice.nextRetryAt))}
              </p>
            )}
          {lastResult.invoice.status === 'uncollectible' && (
            <p className="text-xs text-muted-foreground">Retries exhausted — this invoice is now uncollectible.</p>
          )}
        </div>
      )}

      {awaitingChallenge && (
        <div
          role="status"
          aria-live="polite"
          className="flex flex-col gap-2 rounded-md border border-sky-200 bg-sky-50 px-3 py-2 dark:border-sky-800 dark:bg-sky-950/40"
        >
          <p className="text-sm text-sky-700 dark:text-sky-300">
            Simulated 3D Secure challenge — no real verification happens in this environment.
          </p>
          <div>
            <Button type="button" size="sm" disabled={chargeMutation.isPending} onClick={() => chargeMutation.mutate()}>
              {chargeMutation.isPending ? 'Confirming…' : 'Simulated challenge — click to confirm'}
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
