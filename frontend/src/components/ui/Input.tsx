import { forwardRef, type InputHTMLAttributes } from 'react'
import { cn } from '@/lib/cn'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean
}

export const Input = forwardRef<HTMLInputElement, InputProps>(({ invalid, className, ...props }, ref) => (
  <input
    ref={ref}
    aria-invalid={invalid || undefined}
    className={cn(
      'h-9 w-full rounded-md border bg-surface px-3 text-sm text-foreground placeholder:text-muted-foreground',
      'border-border',
      invalid && 'border-rose-500',
      className,
    )}
    {...props}
  />
))
Input.displayName = 'Input'
