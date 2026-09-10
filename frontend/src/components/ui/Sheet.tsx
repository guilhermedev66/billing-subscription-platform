import { useEffect, useRef, type ReactNode } from 'react'
import { X } from 'lucide-react'
import { cn } from '@/lib/cn'

interface SheetProps {
  open: boolean
  onClose: () => void
  title: string
  description?: string
  children: ReactNode
  footer?: ReactNode
  widthClassName?: string
}

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * Stripe-style right-hand slide-over drawer (M0 research brief §3, Reference Point 1).
 * Traps focus while open, restores it to the trigger on close, and closes on Escape
 * or backdrop click — the table underneath stays mounted and keeps its scroll position.
 */
export function Sheet({ open, onClose, title, description, children, footer, widthClassName }: SheetProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const closeButtonRef = useRef<HTMLButtonElement>(null)
  const previouslyFocused = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (open) {
      previouslyFocused.current = document.activeElement as HTMLElement | null
      closeButtonRef.current?.focus()
    } else {
      previouslyFocused.current?.focus()
    }
  }, [open])

  useEffect(() => {
    if (!open) return

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
        return
      }
      if (event.key !== 'Tab' || !panelRef.current) return

      const focusable = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
      if (focusable.length === 0) return

      const first = focusable[0]
      const last = focusable[focusable.length - 1]

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [open, onClose])

  if (!open) return null

  return (
    <>
      <div className="fixed inset-0 z-40 bg-black/40" aria-hidden="true" onClick={onClose} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="sheet-title"
        aria-describedby={description ? 'sheet-description' : undefined}
        className={cn(
          'fixed inset-y-0 right-0 z-50 flex w-full max-w-[540px] flex-col border-l border-border bg-surface shadow-xl',
          widthClassName,
        )}
      >
        <div className="flex items-start justify-between gap-4 border-b border-border p-4">
          <div className="flex flex-col gap-1">
            <h2 id="sheet-title" className="text-base font-semibold tracking-tight text-foreground">
              {title}
            </h2>
            {description && (
              <p id="sheet-description" className="text-sm text-muted-foreground">
                {description}
              </p>
            )}
          </div>
          <button
            ref={closeButtonRef}
            type="button"
            onClick={onClose}
            className="shrink-0 rounded-md p-1.5 text-muted-foreground hover:bg-surface-muted"
            aria-label="Close panel"
          >
            <X className="size-4" aria-hidden="true" />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-4">{children}</div>
        {footer && <div className="flex items-center justify-end gap-2 border-t border-border p-4">{footer}</div>}
      </div>
    </>
  )
}
