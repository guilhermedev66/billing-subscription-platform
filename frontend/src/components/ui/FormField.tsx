import { cloneElement, isValidElement, type ReactElement } from 'react'
import { cn } from '@/lib/cn'

interface FormFieldProps {
  id: string
  label: string
  error?: string
  hint?: string
  className?: string
  children: ReactElement<{ id?: string; 'aria-invalid'?: boolean; 'aria-describedby'?: string }>
}

/**
 * Renders the error message as a SIBLING of <label>, never nested inside it —
 * a label's descendant text all contributes to its input's accessible name,
 * so an error nested inside the label gets announced twice (name + description).
 */
export function FormField({ id, label, error, hint, className, children }: FormFieldProps) {
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined

  const field = isValidElement(children)
    ? cloneElement(children, {
        id,
        'aria-invalid': Boolean(error) || undefined,
        'aria-describedby': describedBy,
      })
    : children

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <label htmlFor={id} className="text-sm font-medium text-foreground">
        {label}
      </label>
      {field}
      {!error && hint && (
        <p id={`${id}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} role="alert" className="text-xs text-rose-600 dark:text-rose-400">
          {error}
        </p>
      )}
    </div>
  )
}
