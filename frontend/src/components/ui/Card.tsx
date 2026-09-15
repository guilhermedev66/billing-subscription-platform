import type { HTMLAttributes } from 'react'
import { cn } from '@/lib/cn'

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('rounded-lg border border-border bg-surface', className)} {...props} />
}

export function CardHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('flex flex-col gap-1 border-b border-border p-4', className)} {...props} />
}

interface CardTitleProps extends HTMLAttributes<HTMLHeadingElement> {
  /** Defaults to h3 — pass 'h2' when this card is a page's first/only heading level below its h1, to avoid skipping a level (WCAG 2.4.6 / 1.3.1). */
  as?: 'h2' | 'h3' | 'h4'
}

export function CardTitle({ as: Heading = 'h3', className, ...props }: CardTitleProps) {
  return <Heading className={cn('text-sm font-semibold tracking-tight text-foreground', className)} {...props} />
}

export function CardContent({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('p-4', className)} {...props} />
}
