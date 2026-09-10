import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowUpDown, Ban, Pause, Play, Repeat, Users } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { Button, type ButtonProps } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { ErrorState } from '@/components/ui/ErrorState'
import { FormField } from '@/components/ui/FormField'
import { Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { CardSkeleton } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { listPrices, listProducts } from '@/features/catalog/api'
import type { Price, Product } from '@/features/catalog/types'
import { getCustomer } from '@/features/customers/api'
import { cancelSubscription, getSubscription, pauseSubscription, resumeSubscription } from '@/features/subscriptions/api'
import { formatPlanLabel } from '@/features/subscriptions/planLabel'
import { ProrationPreviewModal } from '@/features/subscriptions/ProrationPreviewModal'
import { SUBSCRIPTION_STATUS_LABEL, SUBSCRIPTION_STATUS_TONE } from '@/features/subscriptions/statusTone'
import type { ChangePreviewRequest, Subscription } from '@/features/subscriptions/types'
import { ApiError } from '@/lib/api/client'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })
const selectClassName = 'h-9 w-full rounded-md border border-border bg-surface px-3 text-sm text-foreground'

interface ProrationContext {
  title: string
  request: ChangePreviewRequest
  currentLabel: string
  newLabel: string
  currentSeatCount?: number
  newSeatCount?: number
}

const EMPTY_PRORATION_CONTEXT: ProrationContext = { title: '', request: {}, currentLabel: '', newLabel: '' }

function PlanPickerModal({
  open,
  onClose,
  options,
  currentPriceId,
  onPreview,
}: {
  open: boolean
  onClose: () => void
  options: { id: string; label: string }[]
  currentPriceId: string
  onPreview: (priceId: string) => void
}) {
  const [selected, setSelected] = useState(currentPriceId)

  useEffect(() => {
    if (open) setSelected(currentPriceId)
  }, [open, currentPriceId])

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Upgrade or downgrade plan"
      description="Choose a new plan — we'll preview the prorated charge before anything is applied."
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" onClick={() => onPreview(selected)} disabled={selected === currentPriceId}>
            Preview change
          </Button>
        </>
      }
    >
      <FormField id="plan-picker" label="New plan">
        <select
          id="plan-picker"
          className={selectClassName}
          value={selected}
          onChange={(event) => setSelected(event.target.value)}
        >
          {options.map((option) => (
            <option key={option.id} value={option.id}>
              {option.label}
            </option>
          ))}
        </select>
      </FormField>
    </Modal>
  )
}

function SeatCountModal({
  open,
  onClose,
  currentSeatCount,
  onPreview,
}: {
  open: boolean
  onClose: () => void
  currentSeatCount: number
  onPreview: (seatCount: number) => void
}) {
  const [value, setValue] = useState(String(currentSeatCount))

  useEffect(() => {
    if (open) setValue(String(currentSeatCount))
  }, [open, currentSeatCount])

  const parsed = Number(value)
  const isValid = Number.isInteger(parsed) && parsed > 0 && parsed !== currentSeatCount

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Change seat count"
      description="Choose the new seat count — we'll preview the prorated charge before anything is applied."
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" onClick={() => onPreview(parsed)} disabled={!isValid}>
            Preview change
          </Button>
        </>
      }
    >
      <FormField id="seat-count" label="New seat count">
        <Input
          id="seat-count"
          type="number"
          min={1}
          step={1}
          inputMode="numeric"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
      </FormField>
    </Modal>
  )
}

interface ConfirmActionModalProps {
  open: boolean
  onClose: () => void
  onConfirm: () => void
  title: string
  description?: string
  confirmLabel: string
  pendingLabel: string
  confirmVariant?: ButtonProps['variant']
  isPending: boolean
  errorMessage?: string
  children?: ReactNode
}

/** Shared by Pause, Resume, and Cancel — all three are "confirm a single irreversible-or-not status change" dialogs with the same shape. */
function ConfirmActionModal({
  open,
  onClose,
  onConfirm,
  title,
  description,
  confirmLabel,
  pendingLabel,
  confirmVariant = 'primary',
  isPending,
  errorMessage,
  children,
}: ConfirmActionModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      footer={
        <>
          <Button type="button" variant="secondary" onClick={onClose} disabled={isPending}>
            Back
          </Button>
          <Button type="button" variant={confirmVariant} onClick={onConfirm} disabled={isPending}>
            {isPending ? pendingLabel : confirmLabel}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 text-sm">
        {errorMessage && (
          <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
            {errorMessage}
          </p>
        )}
        {children}
      </div>
    </Modal>
  )
}

export function SubscriptionDetailPage() {
  const { id } = useParams<{ id: string }>()

  const {
    data: subscription,
    isPending: subscriptionPending,
    isError: subscriptionError,
    refetch: refetchSubscription,
  } = useQuery({
    queryKey: ['subscriptions', id],
    queryFn: () => getSubscription(id as string),
    enabled: Boolean(id),
  })

  const {
    data: products,
    isPending: productsPending,
    isError: productsError,
    refetch: refetchProducts,
  } = useQuery({ queryKey: ['products'], queryFn: listProducts })

  const {
    data: prices,
    isPending: pricesPending,
    isError: pricesError,
    refetch: refetchPrices,
  } = useQuery({ queryKey: ['prices'], queryFn: () => listPrices() })

  if (subscriptionPending || productsPending || pricesPending) {
    return (
      <div className="flex flex-col gap-6">
        <CardSkeleton />
        <CardSkeleton />
      </div>
    )
  }

  if (subscriptionError || productsError || pricesError || !subscription || !products || !prices) {
    return (
      <ErrorState
        message="Couldn't load this subscription."
        onRetry={() => {
          refetchSubscription()
          refetchProducts()
          refetchPrices()
        }}
      />
    )
  }

  return <SubscriptionDetailContent subscription={subscription} products={products} prices={prices} />
}

interface SubscriptionDetailContentProps {
  subscription: Subscription
  products: Product[]
  prices: Price[]
}

/**
 * Split from SubscriptionDetailPage so the loading/error early returns above
 * stay before any hook that depends on `subscription` actually existing —
 * everything here can assume subscription/products/prices are loaded.
 */
function SubscriptionDetailContent({ subscription, products, prices }: SubscriptionDetailContentProps) {
  const queryClient = useQueryClient()

  const [planPickerOpen, setPlanPickerOpen] = useState(false)
  const [seatCountOpen, setSeatCountOpen] = useState(false)
  const [pauseConfirmOpen, setPauseConfirmOpen] = useState(false)
  const [resumeConfirmOpen, setResumeConfirmOpen] = useState(false)
  const [cancelConfirmOpen, setCancelConfirmOpen] = useState(false)
  const [prorationOpen, setProrationOpen] = useState(false)
  const [prorationContext, setProrationContext] = useState<ProrationContext>(EMPTY_PRORATION_CONTEXT)

  const productById = new Map(products.map((product) => [product.id, product]))
  const priceById = new Map(prices.map((price) => [price.id, price]))

  const {
    data: customer,
    isPending: customerPending,
    isError: customerError,
    refetch: refetchCustomer,
  } = useQuery({
    queryKey: ['customers', subscription.customerId],
    queryFn: () => getCustomer(subscription.customerId),
  })

  const currentPrice = priceById.get(subscription.priceId)
  const currentProduct = currentPrice ? productById.get(currentPrice.productId) : undefined
  const currentPlanLabel = formatPlanLabel(currentProduct, currentPrice)

  const planOptions = prices
    .filter((price) => (price.model === 'flat' || price.model === 'per_seat') && price.id !== subscription.priceId)
    .map((price) => ({ id: price.id, label: formatPlanLabel(productById.get(price.productId), price) }))

  const canChangePlan = ['trialing', 'active', 'past_due'].includes(subscription.status) && planOptions.length > 0
  const canChangeSeats = canChangePlan && currentPrice?.model === 'per_seat'
  const canPause = ['trialing', 'active', 'past_due'].includes(subscription.status)
  const canResume = subscription.status === 'paused'
  const canCancel = subscription.status !== 'canceled'

  const pauseMutation = useMutation({
    mutationFn: () => pauseSubscription(subscription.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['subscriptions', subscription.id], updated)
      queryClient.invalidateQueries({ queryKey: ['subscriptions'] })
      setPauseConfirmOpen(false)
    },
  })

  const resumeMutation = useMutation({
    mutationFn: () => resumeSubscription(subscription.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['subscriptions', subscription.id], updated)
      queryClient.invalidateQueries({ queryKey: ['subscriptions'] })
      setResumeConfirmOpen(false)
    },
  })

  const cancelMutation = useMutation({
    mutationFn: () => cancelSubscription(subscription.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(['subscriptions', subscription.id], updated)
      queryClient.invalidateQueries({ queryKey: ['subscriptions'] })
      setCancelConfirmOpen(false)
    },
  })

  const openPlanPreview = (newPriceId: string) => {
    const newPrice = priceById.get(newPriceId)
    const newProduct = newPrice ? productById.get(newPrice.productId) : undefined
    setProrationContext({
      title: 'Confirm subscription plan change',
      request: { newPriceId },
      currentLabel: currentPlanLabel,
      newLabel: formatPlanLabel(newProduct, newPrice),
    })
    setPlanPickerOpen(false)
    setProrationOpen(true)
  }

  const openSeatPreview = (newSeatCount: number) => {
    setProrationContext({
      title: 'Confirm seat count change',
      request: { newSeatCount },
      currentLabel: currentPlanLabel,
      newLabel: currentPlanLabel,
      currentSeatCount: subscription.seatCount ?? undefined,
      newSeatCount,
    })
    setSeatCountOpen(false)
    setProrationOpen(true)
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <Repeat className="size-6 text-muted-foreground" aria-hidden="true" />
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <h1 className="text-xl font-semibold tracking-tight text-foreground">
                {customerPending ? 'Loading…' : customerError || !customer ? subscription.customerId : customer.name}
              </h1>
              <StatusBadge tone={SUBSCRIPTION_STATUS_TONE[subscription.status]}>
                {SUBSCRIPTION_STATUS_LABEL[subscription.status]}
              </StatusBadge>
            </div>
            {!customerPending && customer && <p className="text-sm text-muted-foreground">{customer.email}</p>}
            {!customerPending && (customerError || !customer) && (
              <button type="button" onClick={() => refetchCustomer()} className="text-left text-xs text-rose-600 dark:text-rose-400">
                Couldn't load the customer — retry
              </button>
            )}
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-end gap-2">
          {canChangePlan && (
            <Button variant="secondary" size="sm" onClick={() => setPlanPickerOpen(true)}>
              <ArrowUpDown className="size-4" aria-hidden="true" />
              Upgrade / downgrade plan
            </Button>
          )}
          {canChangeSeats && (
            <Button variant="secondary" size="sm" onClick={() => setSeatCountOpen(true)}>
              <Users className="size-4" aria-hidden="true" />
              Change seat count
            </Button>
          )}
          {canPause && (
            <Button variant="secondary" size="sm" onClick={() => setPauseConfirmOpen(true)}>
              <Pause className="size-4" aria-hidden="true" />
              Pause
            </Button>
          )}
          {canResume && (
            <Button variant="secondary" size="sm" onClick={() => setResumeConfirmOpen(true)}>
              <Play className="size-4" aria-hidden="true" />
              Resume
            </Button>
          )}
          {canCancel && (
            <Button variant="destructive" size="sm" onClick={() => setCancelConfirmOpen(true)}>
              <Ban className="size-4" aria-hidden="true" />
              Cancel
            </Button>
          )}
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Plan & billing</CardTitle>
        </CardHeader>
        <CardContent>
          <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1">
              <dt className="text-xs font-medium text-muted-foreground">Plan</dt>
              <dd className="text-sm text-foreground">{currentPlanLabel}</dd>
            </div>
            {currentPrice?.model === 'per_seat' && (
              <div className="flex flex-col gap-1">
                <dt className="text-xs font-medium text-muted-foreground">Seats</dt>
                <dd className="font-mono text-sm tabular-nums text-foreground">{subscription.seatCount}</dd>
              </div>
            )}
            <div className="flex flex-col gap-1">
              <dt className="text-xs font-medium text-muted-foreground">Current period start</dt>
              <dd className="text-sm text-foreground">{dateFormatter.format(new Date(subscription.currentPeriodStart))}</dd>
            </div>
            <div className="flex flex-col gap-1">
              <dt className="text-xs font-medium text-muted-foreground">Current period end</dt>
              <dd className="text-sm text-foreground">{dateFormatter.format(new Date(subscription.currentPeriodEnd))}</dd>
            </div>
            {subscription.trialEnd && (
              <div className="flex flex-col gap-1">
                <dt className="text-xs font-medium text-muted-foreground">Trial ends</dt>
                <dd className="text-sm text-foreground">{dateFormatter.format(new Date(subscription.trialEnd))}</dd>
              </div>
            )}
            <div className="flex flex-col gap-1">
              <dt className="text-xs font-medium text-muted-foreground">Customer since</dt>
              <dd className="text-sm text-foreground">{dateFormatter.format(new Date(subscription.createdAt))}</dd>
            </div>
          </dl>

          {subscription.canceledAt && (
            <p className="mt-4 rounded-md border border-border bg-surface-muted px-3 py-2 text-sm text-muted-foreground">
              Canceled on {dateFormatter.format(new Date(subscription.canceledAt))}
            </p>
          )}
        </CardContent>
      </Card>

      <PlanPickerModal
        open={planPickerOpen}
        onClose={() => setPlanPickerOpen(false)}
        options={planOptions}
        currentPriceId={subscription.priceId}
        onPreview={openPlanPreview}
      />

      {currentPrice?.model === 'per_seat' && (
        <SeatCountModal
          open={seatCountOpen}
          onClose={() => setSeatCountOpen(false)}
          currentSeatCount={subscription.seatCount ?? 1}
          onPreview={openSeatPreview}
        />
      )}

      <ConfirmActionModal
        open={pauseConfirmOpen}
        onClose={() => setPauseConfirmOpen(false)}
        onConfirm={() => pauseMutation.mutate()}
        title="Pause subscription"
        description="Billing and service access are held until this subscription is resumed."
        confirmLabel="Pause subscription"
        pendingLabel="Pausing…"
        isPending={pauseMutation.isPending}
        errorMessage={
          pauseMutation.isError
            ? pauseMutation.error instanceof ApiError
              ? pauseMutation.error.message
              : "Couldn't pause this subscription."
            : undefined
        }
      />

      <ConfirmActionModal
        open={resumeConfirmOpen}
        onClose={() => setResumeConfirmOpen(false)}
        onConfirm={() => resumeMutation.mutate()}
        title="Resume subscription"
        description="Billing and service access pick back up immediately."
        confirmLabel="Resume subscription"
        pendingLabel="Resuming…"
        isPending={resumeMutation.isPending}
        errorMessage={
          resumeMutation.isError
            ? resumeMutation.error instanceof ApiError
              ? resumeMutation.error.message
              : "Couldn't resume this subscription."
            : undefined
        }
      />

      <ConfirmActionModal
        open={cancelConfirmOpen}
        onClose={() => setCancelConfirmOpen(false)}
        onConfirm={() => cancelMutation.mutate()}
        title="Cancel subscription"
        confirmLabel="Cancel subscription"
        pendingLabel="Canceling…"
        confirmVariant="destructive"
        isPending={cancelMutation.isPending}
        errorMessage={
          cancelMutation.isError
            ? cancelMutation.error instanceof ApiError
              ? cancelMutation.error.message
              : "Couldn't cancel this subscription."
            : undefined
        }
      >
        <p className="text-foreground">
          This cancels the subscription <span className="font-medium">immediately</span> — there is no scheduled or
          reversible cancellation yet. The customer loses access right away and this cannot be undone from here.
        </p>
      </ConfirmActionModal>

      <ProrationPreviewModal
        open={prorationOpen}
        onClose={() => setProrationOpen(false)}
        subscriptionId={subscription.id}
        request={prorationContext.request}
        title={prorationContext.title}
        currentPlanLabel={prorationContext.currentLabel}
        newPlanLabel={prorationContext.newLabel}
        currentSeatCount={prorationContext.currentSeatCount}
        newSeatCount={prorationContext.newSeatCount}
        periodStart={subscription.currentPeriodStart}
        periodEnd={subscription.currentPeriodEnd}
        onApplied={() => setProrationOpen(false)}
      />
    </div>
  )
}
