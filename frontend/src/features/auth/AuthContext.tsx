import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { setAuthToken } from '@/lib/api/client'
import * as authApi from './api'
import type { LoginValues, RegisterValues } from './schemas'
import type { AuthSession, AuthUser } from './types'

// localStorage is readable by any injected script, so this trades XSS-exfiltration
// risk for simplicity — an httpOnly cookie would close that gap but needs backend
// support (Set-Cookie session issuance) that's out of scope for M1.
const STORAGE_KEY = 'billing-platform.auth'

interface AuthContextValue {
  user: AuthUser | null
  login: (values: LoginValues) => Promise<void>
  register: (values: RegisterValues) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

function readStoredSession(): AuthSession | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as AuthSession) : null
  } catch {
    return null
  }
}

function persistSession(session: AuthSession | null) {
  if (session) {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  } else {
    window.localStorage.removeItem(STORAGE_KEY)
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // Reading storage is synchronous, so the initial session is known before
  // the first render — no bootstrapping/loading state needed.
  const [user, setUser] = useState<AuthUser | null>(() => {
    const stored = readStoredSession()
    if (stored) setAuthToken(stored.token)
    return stored?.user ?? null
  })

  const applySession = (session: AuthSession) => {
    setAuthToken(session.token)
    persistSession(session)
    setUser(session.user)
  }

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      login: async (values) => applySession(await authApi.login(values)),
      register: async (values) => applySession(await authApi.register(values)),
      logout: () => {
        setAuthToken(null)
        persistSession(null)
        setUser(null)
      },
    }),
    [user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used within an AuthProvider')
  return context
}
