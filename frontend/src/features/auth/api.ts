import { api } from '@/lib/api/client'
import type { LoginValues, RegisterValues } from './schemas'
import type { AuthSession } from './types'

/**
 * Real Identity module endpoints per docs/ARCHITECTURE.md — mocked via MSW
 * (src/mocks/handlers.ts) until Codex — Backend's Identity module is live.
 */
export function login(values: LoginValues) {
  return api.post<AuthSession>('/auth/login', values)
}

export function register(values: RegisterValues) {
  const payload = {
    organizationName: values.organizationName,
    email: values.email,
    password: values.password,
  }
  return api.post<AuthSession>('/auth/register', payload)
}
