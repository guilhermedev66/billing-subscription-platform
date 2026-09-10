import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '@/components/ui/Button'
import { FormField } from '@/components/ui/FormField'
import { Input } from '@/components/ui/Input'
import { ApiError, splitFieldErrors } from '@/lib/api/client'
import { createPrice } from './api'
import { validateTierOrder } from './schemas'
import type { AggregationType, BillingInterval, CreatePriceInput, PricingModel } from './types'

const selectClassName = 'h-9 w-full rounded-md border border-border bg-surface px-3 text-sm text-foreground'

const PRICING_MODELS: { value: PricingModel; label: string }[] = [
  { value: 'flat', label: 'Flat fee' },
  { value: 'per_seat', label: 'Per seat' },
  { value: 'tiered', label: 'Tiered / graduated' },
  { value: 'metered', label: 'Metered / usage-based' },
]

const AGGREGATIONS: { value: AggregationType; label: string }[] = [
  { value: 'sum', label: 'Sum' },
  { value: 'max', label: 'Max' },
  { value: 'last', label: 'Last' },
]

interface TierRowValues {
  upToUnits: string
  openEnded: boolean
  unitAmount: string
}

interface PriceBuilderFormValues {
  model: PricingModel
  interval: BillingInterval
  unitAmount: string
  aggregation: AggregationType
  tiers: TierRowValues[]
}

/** Users type dollars here; the float is converted to integer cents once, at submit — never before. */
const dollarAmountSchema = z
  .string()
  .trim()
  .min(1, 'Amount is required')
  .refine((value) => /^\d+(\.\d{1,2})?$/.test(value), 'Enter a valid amount (e.g. 29.00)')
  .refine((value) => Number(value) > 0, 'Amount must be greater than 0')

/**
 * Form-shape schema (dollar strings, not cents) so field-level errors show up
 * as the user edits. Tier ordering reuses `validateTierOrder` from schemas.ts —
 * the same rule the canonical cents-based `priceSchema` enforces — since
 * ordering only depends on unit counts, never on money.
 */
const priceBuilderSchema = z
  .object({
    model: z.enum(['flat', 'per_seat', 'tiered', 'metered']),
    interval: z.enum(['month', 'year']),
    unitAmount: z.string(),
    aggregation: z.enum(['sum', 'max', 'last']),
    tiers: z.array(
      z.object({
        upToUnits: z.string(),
        openEnded: z.boolean(),
        unitAmount: z.string(),
      }),
    ),
  })
  .superRefine((values, ctx) => {
    if (values.model === 'flat' || values.model === 'per_seat' || values.model === 'metered') {
      const result = dollarAmountSchema.safeParse(values.unitAmount)
      if (!result.success) {
        for (const issue of result.error.issues) {
          ctx.addIssue({ ...issue, path: ['unitAmount'] })
        }
      }
    }

    if (values.model === 'tiered') {
      values.tiers.forEach((tier, index) => {
        const amountResult = dollarAmountSchema.safeParse(tier.unitAmount)
        if (!amountResult.success) {
          for (const issue of amountResult.error.issues) {
            ctx.addIssue({ ...issue, path: ['tiers', index, 'unitAmount'] })
          }
        }

        if (!tier.openEnded) {
          const trimmed = tier.upToUnits.trim()
          if (!/^\d+$/.test(trimmed) || Number(trimmed) <= 0) {
            ctx.addIssue({
              code: z.ZodIssueCode.custom,
              message: 'Enter a whole number of units',
              path: ['tiers', index, 'upToUnits'],
            })
          }
        }
      })

      const boundaries = values.tiers.map((tier) => ({
        upTo: tier.openEnded ? null : Number(tier.upToUnits.trim()),
      }))
      validateTierOrder(boundaries, ctx, ['tiers'])
    }
  })

interface PriceBuilderProps {
  productId: string
  onCreated?: () => void
}

export function PriceBuilder({ productId, onCreated }: PriceBuilderProps) {
  const queryClient = useQueryClient()
  const [fallbackError, setFallbackError] = useState<string | null>(null)

  const {
    register,
    control,
    handleSubmit,
    watch,
    setValue,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<PriceBuilderFormValues>({
    resolver: zodResolver(priceBuilderSchema),
    defaultValues: {
      model: 'flat',
      interval: 'month',
      unitAmount: '',
      aggregation: 'sum',
      tiers: [{ upToUnits: '', openEnded: true, unitAmount: '' }],
    },
  })

  const { fields, append, remove } = useFieldArray({ control, name: 'tiers' })
  const model = watch('model')

  const mutation = useMutation({
    mutationFn: (input: CreatePriceInput) => createPrice(input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['prices'] })
      onCreated?.()
    },
    onError: (error) => {
      if (!(error instanceof ApiError)) {
        setFallbackError('Something went wrong. Please try again.')
        return
      }
      if (error.kind === 'validation' && error.fieldErrors) {
        const { mapped, unmapped } = splitFieldErrors(error.fieldErrors, [
          'unitAmount',
          'aggregation',
          'interval',
        ] as const)
        for (const [field, message] of Object.entries(mapped)) {
          setError(field as 'unitAmount' | 'aggregation' | 'interval', { message })
        }
        if (unmapped.length > 0) setFallbackError(unmapped.join(' '))
        return
      }
      setFallbackError(error.message)
    },
  })

  const onSubmit = (values: PriceBuilderFormValues) => {
    setFallbackError(null)
    const base = { productId, interval: values.interval, currency: 'USD' }
    let payload: CreatePriceInput

    if (values.model === 'tiered') {
      payload = {
        ...base,
        model: 'tiered',
        tiers: values.tiers.map((tier) => ({
          upTo: tier.openEnded ? null : Number(tier.upToUnits.trim()),
          unitAmountCents: Math.round(Number(tier.unitAmount) * 100),
        })),
      }
    } else if (values.model === 'metered') {
      payload = {
        ...base,
        model: 'metered',
        aggregation: values.aggregation,
        unitAmountCents: Math.round(Number(values.unitAmount) * 100),
      }
    } else {
      payload = {
        ...base,
        model: values.model,
        unitAmountCents: Math.round(Number(values.unitAmount) * 100),
      }
    }

    mutation.mutate(payload)
  }

  const addTier = () => {
    const lastIndex = fields.length - 1
    if (lastIndex >= 0) setValue(`tiers.${lastIndex}.openEnded`, false)
    append({ upToUnits: '', openEnded: true, unitAmount: '' })
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4" noValidate>
      {fallbackError && (
        <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
          {fallbackError}
        </p>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <FormField id="model" label="Pricing model">
          <select id="model" className={selectClassName} {...register('model')}>
            {PRICING_MODELS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FormField>

        <FormField id="interval" label="Billing interval" error={errors.interval?.message}>
          <select id="interval" className={selectClassName} {...register('interval')}>
            <option value="month">Monthly</option>
            <option value="year">Yearly</option>
          </select>
        </FormField>
      </div>

      {model === 'metered' && (
        <FormField id="aggregation" label="Usage aggregation" error={errors.aggregation?.message}>
          <select id="aggregation" className={selectClassName} {...register('aggregation')}>
            {AGGREGATIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </FormField>
      )}

      {(model === 'flat' || model === 'per_seat' || model === 'metered') && (
        <FormField
          id="unitAmount"
          label={
            model === 'per_seat' ? 'Amount per seat (USD)' : model === 'metered' ? 'Amount per unit (USD)' : 'Amount (USD)'
          }
          error={errors.unitAmount?.message}
        >
          <Input id="unitAmount" inputMode="decimal" placeholder="29.00" {...register('unitAmount')} />
        </FormField>
      )}

      {model === 'tiered' && (
        <div className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium text-foreground">Tiers</span>
            <Button type="button" variant="secondary" size="sm" onClick={addTier}>
              <Plus className="size-4" aria-hidden="true" />
              Add tier
            </Button>
          </div>

          <div className="flex flex-col gap-3">
            {fields.map((field, index) => {
              const isLast = index === fields.length - 1
              const rowErrors = errors.tiers?.[index]
              const openEnded = watch(`tiers.${index}.openEnded`)

              return (
                <div
                  key={field.id}
                  className="grid grid-cols-1 gap-3 rounded-md border border-border p-3 sm:grid-cols-[1fr_1fr_auto]"
                >
                  <FormField id={`tiers.${index}.upToUnits`} label="Up to (units)" error={rowErrors?.upToUnits?.message}>
                    <Input
                      id={`tiers.${index}.upToUnits`}
                      inputMode="numeric"
                      placeholder="5"
                      disabled={openEnded}
                      {...register(`tiers.${index}.upToUnits`)}
                    />
                  </FormField>

                  <FormField id={`tiers.${index}.unitAmount`} label="Unit amount (USD)" error={rowErrors?.unitAmount?.message}>
                    <Input
                      id={`tiers.${index}.unitAmount`}
                      inputMode="decimal"
                      placeholder="20.00"
                      {...register(`tiers.${index}.unitAmount`)}
                    />
                  </FormField>

                  <div className="flex items-end gap-3 pb-1.5">
                    {isLast && (
                      <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
                        <input type="checkbox" {...register(`tiers.${index}.openEnded`)} />
                        No upper limit
                      </label>
                    )}
                    {fields.length > 1 && (
                      <Button type="button" variant="ghost" size="sm" aria-label="Remove tier" onClick={() => remove(index)}>
                        <Trash2 className="size-4" aria-hidden="true" />
                      </Button>
                    )}
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      )}

      <div>
        <Button type="submit" disabled={isSubmitting || mutation.isPending}>
          {mutation.isPending ? 'Adding price…' : 'Add price'}
        </Button>
      </div>
    </form>
  )
}
