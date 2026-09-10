import { describe, expect, it } from 'vitest'
import { priceSchema } from './schemas'

function tieredPrice(tiers: { upTo: number | null; unitAmountCents: number }[]) {
  return {
    model: 'tiered' as const,
    interval: 'month' as const,
    currency: 'USD',
    active: true,
    tiers,
  }
}

describe('priceSchema — tiered validation', () => {
  it('accepts ascending tiers with only the last one open-ended', () => {
    const result = priceSchema.safeParse(
      tieredPrice([
        { upTo: 5, unitAmountCents: 2000 },
        { upTo: 20, unitAmountCents: 1500 },
        { upTo: null, unitAmountCents: 1000 },
      ]),
    )
    expect(result.success).toBe(true)
  })

  it('accepts a fully bounded tier list with no open-ended tier at all', () => {
    const result = priceSchema.safeParse(
      tieredPrice([
        { upTo: 5, unitAmountCents: 2000 },
        { upTo: 20, unitAmountCents: 1500 },
      ]),
    )
    expect(result.success).toBe(true)
  })

  it('rejects out-of-order tiers', () => {
    const result = priceSchema.safeParse(
      tieredPrice([
        { upTo: 20, unitAmountCents: 1500 },
        { upTo: 5, unitAmountCents: 2000 },
      ]),
    )
    expect(result.success).toBe(false)
  })

  it('rejects overlapping/duplicate tier boundaries', () => {
    const result = priceSchema.safeParse(
      tieredPrice([
        { upTo: 5, unitAmountCents: 2000 },
        { upTo: 5, unitAmountCents: 1500 },
      ]),
    )
    expect(result.success).toBe(false)
  })

  it('rejects a non-last tier with upTo: null', () => {
    const result = priceSchema.safeParse(
      tieredPrice([
        { upTo: null, unitAmountCents: 2000 },
        { upTo: 10, unitAmountCents: 1500 },
      ]),
    )
    expect(result.success).toBe(false)
  })

  it('rejects a tier list with no tiers at all', () => {
    const result = priceSchema.safeParse(tieredPrice([]))
    expect(result.success).toBe(false)
  })

  it('rejects a tiered price missing the interval', () => {
    const result = priceSchema.safeParse({
      model: 'tiered',
      currency: 'USD',
      active: true,
      tiers: [{ upTo: null, unitAmountCents: 1000 }],
    })
    expect(result.success).toBe(false)
  })
})

describe('priceSchema — other models', () => {
  it('requires a positive unitAmountCents for flat prices', () => {
    const result = priceSchema.safeParse({
      model: 'flat',
      interval: 'month',
      currency: 'USD',
      active: true,
      unitAmountCents: 0,
    })
    expect(result.success).toBe(false)
  })

  it('requires an aggregation type for metered prices', () => {
    const result = priceSchema.safeParse({
      model: 'metered',
      interval: 'month',
      currency: 'USD',
      active: true,
      unitAmountCents: 5,
    })
    expect(result.success).toBe(false)
  })

  it('accepts a valid per_seat price', () => {
    const result = priceSchema.safeParse({
      model: 'per_seat',
      interval: 'month',
      currency: 'USD',
      active: true,
      unitAmountCents: 1200,
    })
    expect(result.success).toBe(true)
  })
})
