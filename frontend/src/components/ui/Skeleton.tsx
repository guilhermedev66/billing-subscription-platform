import type { HTMLAttributes } from 'react'
import { cn } from '@/lib/cn'

export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('animate-pulse rounded-md bg-surface-muted', className)} {...props} />
}

export function CardSkeleton() {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-border bg-surface p-4">
      <Skeleton className="h-3 w-24" />
      <Skeleton className="h-6 w-32" />
      <Skeleton className="h-3 w-16" />
    </div>
  )
}

/** Wrapper announces loading once; individual CardSkeletons stay presentational so a grid doesn't announce N times. */
export function CardSkeletonGrid({ count, label }: { count: number; label: string }) {
  return (
    <div role="status" aria-live="polite" aria-label={label} className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      {Array.from({ length: count }, (_, i) => (
        <CardSkeleton key={i} />
      ))}
    </div>
  )
}
