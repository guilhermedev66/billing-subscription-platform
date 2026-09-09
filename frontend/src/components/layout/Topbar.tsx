import type { RefObject } from 'react'
import { LogOut, Menu, Moon, Sun } from 'lucide-react'
import { useTheme } from '@/lib/theme/ThemeProvider'
import { useAuth } from '@/features/auth/AuthContext'

interface TopbarProps {
  onOpenSidebar: () => void
  sidebarTriggerRef: RefObject<HTMLButtonElement | null>
}

export function Topbar({ onOpenSidebar, sidebarTriggerRef }: TopbarProps) {
  const { theme, toggleTheme } = useTheme()
  const { user, logout } = useAuth()

  return (
    <header className="flex h-14 items-center justify-between border-b border-border bg-surface px-4">
      <button
        ref={sidebarTriggerRef}
        type="button"
        onClick={onOpenSidebar}
        className="rounded-md p-1.5 text-muted-foreground hover:bg-surface-muted lg:hidden"
        aria-label="Open navigation"
      >
        <Menu className="size-5" aria-hidden="true" />
      </button>
      <div className="hidden lg:block" />
      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={toggleTheme}
          className="rounded-md p-1.5 text-muted-foreground hover:bg-surface-muted"
          aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
        >
          {theme === 'dark' ? <Sun className="size-4" aria-hidden="true" /> : <Moon className="size-4" aria-hidden="true" />}
        </button>
        {user && (
          <div className="flex items-center gap-2 border-l border-border pl-3">
            <span className="text-sm text-foreground">{user.email}</span>
            <button
              type="button"
              onClick={logout}
              className="rounded-md p-1.5 text-muted-foreground hover:bg-surface-muted"
              aria-label="Sign out"
            >
              <LogOut className="size-4" aria-hidden="true" />
            </button>
          </div>
        )}
      </div>
    </header>
  )
}
