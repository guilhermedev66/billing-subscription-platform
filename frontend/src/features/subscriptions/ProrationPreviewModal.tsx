import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Skeleton } from '@/components/ui/Skeleton'
import { applyChange, previewChange } from '@/features/subscriptions/api'
import type { ChangePreviewRequest, Subscription } from '@/features/subscriptions/types'
import { ApiError } from '@/lib/api/client'
import { formatCents } from '@/lib/money'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })
const DAY_MS = 24 * 60 * 60 * 1000

interface ProrationPreviewModalProps {
  open: boolean
  onClose: () => void
  subscriptionId: string
  request: ChangePreviewRequest
  title: string
  currentPlanLabel: string
  newPlanLabel: string
  currentSeatCount?: number
  newSeatCount?: number
  /** The subscription's current cycle — the cycle anchor is maintained across a proration, so this doesn't change. */
  periodStart: string
  periodEnd: string
  onApplied: (subscription: Subscription) => void
}

/**
 * The Chargebee "Preview Proration & Upcoming Charges" modal (M0 research
 * brief §3, Reference Point 3). `previewChange` is a fetch-on-demand action,
 * not data tied to a mount, so it's triggered as a mutation when the modal
 * opens rather than as a useQuery — the modal itself opens immediately and
 * renders its own loading state instead of waiting on the network first.
 */
export function ProrationPreviewModal({
  open,
  onClose,
  subscriptionId,
  request,
  title,
  currentPlanLabel,
  newPlanLabel,
  currentSeatCount,
  newSeatCount,
  periodStart,
  periodEnd,
  onApplied,
}: ProrationPreviewModalProps) {
  const queryClient = useQueryClient()

  const previewMutation = useMutation({
    mutationFn: () => previewChange(subscriptionId, request),
  })
  const { mutate: triggerPreview, reset: resetPreview } = previewMutation

  useEffect(() => {
    if (open) {
      triggerPreview()
    } else {
      resetPreview()
    }
  }, [open, subscriptionId, request.newPriceId, request.newSeatCount, triggerPreview, resetPreview])

  const applyMutation = useMutation({
    mutationFn: () => applyChange(subscriptionId, request),
    onSuccess: (receipt) => {
      queryClient.invalidateQueries({ queryKey: ['subscriptions'] })
      queryClient.invalidateQueries({ queryKey: ['subscriptions', subscriptionId] })
      onApplied(receipt.subscription)
      onClose()
    },
  })

  const receipt = previewMutation.data
  const proration = receipt?.proration
  const seatCountChanged =
    currentSeatCount !== undefined && newSeatCount !== undefined && currentSeatCount !== newSeatCount
  const totalDaysInPeriod = Math.round((new Date(periodEnd).getTime() - new Date(periodStart).getTime()) / DAY_MS)
  const isCredit = (proration?.amountDueImmediatelyCents ?? 0) < 0

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      widthClassName="max-w-xl"
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose} disabled={applyMutation.isPending}>
            Cancel
          </Button>
          <Button
            type="button"
            onClick={() => applyMutation.mutate()}
            disabled={!previewMutation.isSuccess || applyMutation.isPending}
          >
            {applyMutation.isPending ? (isCredit ? 'Issuing credit…' : 'Applying…') : isCredit ? 'Confirm & Issue Credit' : 'Confirm & Charge'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {previewMutation.isPending && (
          <div className="flex flex-col gap-3" role="status" aria-live="polite" aria-label="Loading proration preview">
            <Skeleton className="h-5 w-full" />
            <Skeleton className="h-4 w-2/3" />
            <Skeleton className="h-4 w-1/2" />
            <Skeleton className="h-16 w-full" />
          </div>
        )}

        {previewMutation.isError && (
          <div role="alert" className="flex flex-col gap-3">
            <p className="text-sm text-rose-600 dark:text-rose-400">
              {previewMutation.error instanceof ApiError
                ? previewMutation.error.message
                : "Couldn't load the proration preview."}
            </p>
            <div>
              <Button type="button" variant="secondary" size="sm" onClick={() => triggerPreview()}>
                Try again
              </Button>
            </div>
          </div>
        )}

        {previewMutation.isSuccess && proration && (
          <div className="flex flex-col gap-4 text-sm">
            <div className="flex items-center justify-between gap-3 rounded-md bg-surface-muted px-3 py-2">
              <span className="text-foreground">{currentPlanLabel}</span>
              <span className="text-muted-foreground" aria-hidden="true">
                &rarr;
              </span>
              <span className="font-medium text-foreground">{newPlanLabel}</span>
            </div>

            {seatCountChanged && (
              <div className="flex items-center justify-between text-muted-foreground">
                <span>Seats</span>
                <span className="font-mono tabular-nums text-foreground">
                  {currentSeatCount} &rarr; {newSeatCount}
                </span>
              </div>
            )}

            <div className="flex items-center justify-between text-muted-foreground">
              <span>Effective date</span>
              <span className="text-foreground">Immediate ({dateFormatter.format(new Date())})</span>
            </div>

            <div className="flex items-center justify-between text-muted-foreground">
              <span>Remaining in cycle</span>
              <span className="text-foreground">
                {proration.remainingCycleDays} days of {totalDaysInPeriod} days ({proration.remainingCyclePercentage}%)
              </span>
            </div>

            <div className="flex flex-col gap-1.5 border-t border-border pt-3">
              <div className="flex items-center justify-between">
                <span className="text-muted-foreground">Prorated credit (unused current plan)</span>
                <span className="font-mono tabular-nums text-rose-600 dark:text-rose-400">
                  {formatCents(proration.proratedCreditCents)}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-muted-foreground">Prorated charge (new plan)</span>
                <span className="font-mono tabular-nums text-foreground">+{formatCents(proration.proratedChargeCents)}</span>
              </div>
            </div>

            <div className="flex flex-col gap-1.5 border-t border-border pt-3">
              {isCredit ? (
                <div className="flex flex-col gap-1 rounded-md border border-emerald-200 bg-emerald-50 px-3 py-2 dark:border-emerald-800 dark:bg-emerald-950/40">
                  <span className="font-medium text-emerald-700 dark:text-emerald-300">
                    Account credit: {formatCents(Math.abs(proration.amountDueImmediatelyCents))}
                  </span>
                  <span className="text-xs text-emerald-700/80 dark:text-emerald-300/80">
                    This amount will be added to the customer's balance and applied toward future invoices.
                  </span>
                </div>
              ) : (
                <div className="flex items-center justify-between font-medium">
                  <span className="text-foreground">Amount due immediately</span>
                  <span className="font-mono tabular-nums text-foreground">
                    {formatCents(proration.amountDueImmediatelyCents)}
                  </span>
                </div>
              )}
              <div className="flex items-center justify-between text-muted-foreground">
                <span>Next renewal ({dateFormatter.format(new Date(periodEnd))})</span>
                <span className="font-mono tabular-nums text-foreground">
                  {formatCents(proration.nextRegularRenewalAmountCents)}
                </span>
              </div>
            </div>

            {applyMutation.isError && (
              <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
                {applyMutation.error instanceof ApiError
                  ? applyMutation.error.message
                  : "Couldn't apply this change. Please try again."}
              </p>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
