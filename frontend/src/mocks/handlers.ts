import { http, HttpResponse } from 'msw'
import type { AuthSession, AuthUser } from '@/features/auth/types'
import { catalogHandlers } from './catalogHandlers'
import { customersHandlers } from './customersHandlers'

/**
 * Stands in for the Identity + Organizations modules until Codex — Backend's
 * API is live (docs/ROADMAP.md M1). Swap for real endpoints once it lands —
 * shape (ProblemDetails on error, {token, user} on success) is meant to match
 * the real contract so that swap is a delete, not a rewrite.
 */
interface StoredAccount {
  password: string
  user: AuthUser
}

const accounts = new Map<string, StoredAccount>([
  [
    'demo@example.com',
    {
      password: 'password123',
      user: {
        id: 'usr_demo',
        email: 'demo@example.com',
        organizationId: 'org_demo',
        organizationName: 'Demo Org',
      },
    },
  ],
])

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

function issueSession(user: AuthUser): AuthSession {
  return { token: `mock.${user.id}.${Date.now()}`, user }
}

export const handlers = [
  http.post('/api/auth/login', async ({ request }) => {
    const body = (await request.json()) as { email?: string; password?: string }
    const account = body.email ? accounts.get(body.email) : undefined

    if (!account || account.password !== body.password) {
      // Same 401 for "no such user" and "wrong password" — avoids a lockout oracle.
      return problem(401, 'Invalid credentials.')
    }

    return HttpResponse.json(issueSession(account.user))
  }),

  http.post('/api/auth/register', async ({ request }) => {
    const body = (await request.json()) as {
      organizationName?: string
      email?: string
      password?: string
    }

    if (!body.email || !body.password || !body.organizationName) {
      const errors: Record<string, string[]> = {}
      if (!body.organizationName) errors.OrganizationName = ['Organization name is required.']
      if (!body.email) errors.Email = ['Email is required.']
      if (!body.password) errors.Password = ['Password is required.']
      return problem(400, 'Validation failed.', undefined, errors)
    }

    if (accounts.has(body.email)) {
      return problem(409, 'An account with this email already exists.')
    }

    const user: AuthUser = {
      id: `usr_${accounts.size + 1}`,
      email: body.email,
      organizationId: `org_${accounts.size + 1}`,
      organizationName: body.organizationName,
    }
    accounts.set(body.email, { password: body.password, user })

    return HttpResponse.json(issueSession(user), { status: 201 })
  }),

  ...customersHandlers,
  ...catalogHandlers,
]
