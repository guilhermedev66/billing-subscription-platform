import { useEffect, useRef, type ReactNode } from 'react'
import { X } from 'lucide-react'
import { cn } from '@/lib/cn'

interface ModalProps {
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
 * Centered confirmation dialog — the Chargebee-style "Preview Proration"
 * pattern (M0 research brief §3, Reference Point 3) needs a boxed modal the
 * user must explicitly dismiss or confirm, not a slide-over that keeps
 * background content reachable. Same focus-trap/Escape/backdrop-close
 * behavior as Sheet, just centered instead of docked to the right edge —
 * kept as a separate small component rather than sharing Sheet's internals,
 * since the two have different enough layout needs to not be worth forcing
 * through one abstraction.
 */
export function Modal({ open, onClose, title, description, children, footer, widthClassName }: ModalProps) {
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
      <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div
          ref={panelRef}
          role="dialog"
          aria-modal="true"
          aria-labelledby="modal-title"
          aria-describedby={description ? 'modal-description' : undefined}
          className={cn(
            'flex w-full max-w-lg flex-col rounded-lg border border-border bg-surface shadow-xl',
            widthClassName,
          )}
        >
          <div className="flex items-start justify-between gap-4 border-b border-border p-4">
            <div className="flex flex-col gap-1">
              <h2 id="modal-title" className="text-base font-semibold tracking-tight text-foreground">
                {title}
              </h2>
              {description && (
                <p id="modal-description" className="text-sm text-muted-foreground">
                  {description}
                </p>
              )}
            </div>
            <button
              ref={closeButtonRef}
              type="button"
              onClick={onClose}
              className="shrink-0 rounded-md p-1.5 text-muted-foreground hover:bg-surface-muted"
              aria-label="Close dialog"
            >
              <X className="size-4" aria-hidden="true" />
            </button>
          </div>
          <div className="p-4">{children}</div>
          {footer && <div className="flex items-center justify-end gap-2 border-t border-border p-4">{footer}</div>}
        </div>
      </div>
    </>
  )
}
