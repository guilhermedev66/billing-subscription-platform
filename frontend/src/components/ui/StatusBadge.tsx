import { cn } from '@/lib/cn'

/**
 * Verbatim from the M0 research brief §6.2 — don't reinvent per module.
 * success: Active/Paid/Succeeded · info: Trialing/Processing/Pending ·
 * warning: Past Due/Incomplete/Action Required · destructive: Unpaid/Uncollectible/Failed/Declined ·
 * neutral: Canceled/Voided/Paused/Draft
 */
export const billingStatusTokens = {
  success: {
    badge:
      'bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800',
    dot: 'bg-emerald-500',
  },
  info: {
    badge: 'bg-sky-50 text-sky-700 border-sky-200 dark:bg-sky-950/40 dark:text-sky-300 dark:border-sky-800',
    dot: 'bg-sky-500',
  },
  warning: {
    badge: 'bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-950/40 dark:text-amber-300 dark:border-amber-800',
    dot: 'bg-amber-500',
  },
  destructive: {
    badge: 'bg-rose-50 text-rose-700 border-rose-200 dark:bg-rose-950/40 dark:text-rose-300 dark:border-rose-800',
    dot: 'bg-rose-500',
  },
  neutral: {
    badge: 'bg-zinc-100 text-zinc-700 border-zinc-200 dark:bg-zinc-800 dark:text-zinc-300 dark:border-zinc-700',
    dot: 'bg-zinc-400',
  },
} as const

export type BillingStatusTone = keyof typeof billingStatusTokens

interface StatusBadgeProps {
  tone: BillingStatusTone
  children: React.ReactNode
  className?: string
}

export function StatusBadge({ tone, children, className }: StatusBadgeProps) {
  const tokens = billingStatusTokens[tone]
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-medium',
        tokens.badge,
        className,
      )}
    >
      <span className={cn('size-1.5 rounded-full', tokens.dot)} aria-hidden="true" />
      {children}
    </span>
  )
}
