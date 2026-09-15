import { formatCents } from '@/lib/money'

interface SignedAmountProps {
  cents: number
  currency: string
}

/**
 * Screen readers commonly drop a leading +/- glyph sitting right before a
 * currency symbol, so e.g. "-$50.00" can be announced as if it were
 * positive — worse than usual in a billing app, where that misread is a loss
 * heard as a gain. Renders a visually-hidden "positive "/"negative " word
 * ahead of the glyph so the sign is always spoken, regardless of how a given
 * screen reader's punctuation-verbosity setting treats the symbol itself.
 */
export function SignedAmount({ cents, currency }: SignedAmountProps) {
  return (
    <>
      {cents > 0 && <span className="sr-only">positive </span>}
      {cents < 0 && <span className="sr-only">negative </span>}
      {cents > 0 ? '+' : ''}
      {formatCents(cents, currency)}
    </>
  )
}
