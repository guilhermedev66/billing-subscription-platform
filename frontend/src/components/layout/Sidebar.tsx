import { useEffect, useRef, type RefObject } from 'react'
import { NavLink } from 'react-router-dom'
import { X, Zap } from 'lucide-react'
import { cn } from '@/lib/cn'
import { navItems } from './nav'

interface SidebarProps {
  open: boolean
  onClose: () => void
  triggerRef: RefObject<HTMLButtonElement | null>
}

export function Sidebar({ open, onClose, triggerRef }: SidebarProps) {
  const closeButtonRef = useRef<HTMLButtonElement>(null)
  const wasOpen = useRef(false)

  useEffect(() => {
    if (open) {
      closeButtonRef.current?.focus()
    } else if (wasOpen.current) {
      triggerRef.current?.focus()
    }
    wasOpen.current = open
  }, [open, triggerRef])

  useEffect(() => {
    if (!open) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [open, onClose])

  return (
    <>
      {open && (
        <div
          className="fixed inset-0 z-30 bg-black/40 lg:hidden"
          aria-hidden="true"
          onClick={onClose}
        />
      )}
      <aside
        className={cn(
          'fixed inset-y-0 left-0 z-40 flex w-60 flex-col bg-sidebar text-sidebar-foreground transition-transform duration-150 ease-out',
          open ? 'translate-x-0' : '-translate-x-full',
          'lg:static lg:translate-x-0',
        )}
        aria-label="Primary navigation"
      >
        <div className="flex h-14 items-center justify-between border-b border-sidebar-border px-4">
          <span className="flex items-center gap-2 text-sm font-semibold tracking-tight">
            <Zap className="size-4" aria-hidden="true" />
            Billing Platform
          </span>
          <button
            ref={closeButtonRef}
            type="button"
            onClick={onClose}
            className="rounded-md p-1 text-sidebar-muted-foreground hover:bg-sidebar-active lg:hidden"
            aria-label="Close navigation"
          >
            <X className="size-4" aria-hidden="true" />
          </button>
        </div>
        <nav className="flex flex-1 flex-col gap-0.5 p-2">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              onClick={onClose}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-2.5 rounded-md px-3 py-2 text-sm font-medium transition-colors duration-150 ease-out',
                  isActive
                    ? 'bg-sidebar-active text-sidebar-foreground'
                    : 'text-sidebar-muted-foreground hover:bg-sidebar-active hover:text-sidebar-foreground',
                )
              }
            >
              <item.icon className="size-4" aria-hidden="true" />
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>
    </>
  )
}
