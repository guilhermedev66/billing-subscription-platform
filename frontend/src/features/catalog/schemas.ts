import { z } from 'zod'
import type { PriceTier } from './types'

export const productSchema = z.object({
  name: z.string().min(2, 'Name must be at least 2 characters'),
  description: z.string().optional(),
})

export type ProductFormValues = z.infer<typeof productSchema>

export const priceTierSchema = z.object({
  upTo: z.number().int('Must be a whole number of units').positive('Must be greater than 0').nullable(),
  unitAmountCents: z.number().int('Must be a whole number of cents').positive('Amount must be greater than 0'),
})

/**
 * Milestone's explicit hard requirement: tiers sorted ascending by `upTo`, each
 * strictly greater than the previous, only the LAST tier may be open-ended
 * (`upTo === null`), and no duplicate/overlapping boundaries.
 *
 * Only inspects `upTo` (a unit count, never money) so it's reusable as-is by
 * PriceBuilder's dollar-denominated form schema before the dollars->cents
 * conversion happens at submit — the ordering rule never needs cents.
 */
export function validateTierOrder(
  tiers: Pick<PriceTier, 'upTo'>[],
  ctx: z.RefinementCtx,
  basePath: (string | number)[] = [],
) {
  tiers.forEach((tier, index) => {
    const isLast = index === tiers.length - 1

    if (!isLast && tier.upTo === null) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Only the last tier can be open-ended — set an upper bound.',
        path: [...basePath, index, 'upTo'],
      })
    }

    if (index === 0) return
    const previous = tiers[index - 1]

    if (previous.upTo === null) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'No tier can follow an open-ended tier.',
        path: [...basePath, index, 'upTo'],
      })
      return
    }

    if (tier.upTo !== null && tier.upTo <= previous.upTo) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Tiers must be strictly ascending — this tier's \"up to\" must be greater than the previous one.",
        path: [...basePath, index, 'upTo'],
      })
    }
  })
}

const intervalSchema = z.enum(['month', 'year'])
const currencySchema = z.string().length(3).default('USD')
const activeSchema = z.boolean().default(true)

const flatPriceSchema = z.object({
  model: z.literal('flat'),
  interval: intervalSchema,
  currency: currencySchema,
  active: activeSchema,
  unitAmountCents: z.number().int().positive('Amount must be greater than 0'),
})

const perSeatPriceSchema = z.object({
  model: z.literal('per_seat'),
  interval: intervalSchema,
  currency: currencySchema,
  active: activeSchema,
  unitAmountCents: z.number().int().positive('Per-seat amount must be greater than 0'),
})

const meteredPriceSchema = z.object({
  model: z.literal('metered'),
  interval: intervalSchema,
  currency: currencySchema,
  active: activeSchema,
  aggregation: z.enum(['sum', 'max', 'last']),
  unitAmountCents: z.number().int().positive('Per-unit amount must be greater than 0'),
})

const tieredPriceSchema = z.object({
  model: z.literal('tiered'),
  interval: intervalSchema,
  currency: currencySchema,
  active: activeSchema,
  tiers: z.array(priceTierSchema).min(1, 'Add at least one tier'),
})

export const priceSchema = z
  .discriminatedUnion('model', [flatPriceSchema, perSeatPriceSchema, meteredPriceSchema, tieredPriceSchema])
  .superRefine((data, ctx) => {
    if (data.model === 'tiered') {
      validateTierOrder(data.tiers, ctx, ['tiers'])
    }
  })

export type PriceFormValues = z.infer<typeof priceSchema>
