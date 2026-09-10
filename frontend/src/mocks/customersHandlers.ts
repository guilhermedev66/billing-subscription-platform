import { http, HttpResponse } from 'msw'
import type { Customer } from '@/features/customers/types'

/**
 * Stands in for the Customers module until Codex — Backend's API is live
 * (docs/ROADMAP.md M2). Shape (ProblemDetails on error, Customer on success)
 * is meant to match the real contract so that swap is a delete, not a rewrite.
 */
const customers = new Map<string, Customer>(
  [
    {
      id: 'cus_aperture',
      name: 'Aperture Science',
      email: 'billing@aperture.example',
      balanceCents: -48_00,
      delinquentFlag: true,
      createdAt: '2025-11-03T09:15:00.000Z',
    },
    {
      id: 'cus_globex',
      name: 'Globex Corporation',
      email: 'ap@globex.example',
      balanceCents: 12_50,
      delinquentFlag: false,
      createdAt: '2025-12-18T14:02:00.000Z',
    },
    {
      id: 'cus_initech',
      name: 'Initech',
      email: 'accounts@initech.example',
      balanceCents: 0,
      delinquentFlag: false,
      createdAt: '2026-01-05T11:30:00.000Z',
    },
    {
      id: 'cus_umbrella',
      name: 'Umbrella Corp',
      email: 'finance@umbrella.example',
      balanceCents: -215_00,
      delinquentFlag: true,
      createdAt: '2026-01-20T08:45:00.000Z',
    },
    {
      id: 'cus_wayne',
      name: 'Wayne Enterprises',
      email: 'billing@wayne.example',
      balanceCents: 500_00,
      delinquentFlag: false,
      createdAt: '2026-02-02T16:10:00.000Z',
    },
    {
      id: 'cus_stark',
      name: 'Stark Industries',
      email: 'ar@stark.example',
      balanceCents: 0,
      delinquentFlag: false,
      createdAt: '2026-02-14T13:22:00.000Z',
    },
    {
      id: 'cus_soylent',
      name: 'Soylent Corp',
      email: 'billing@soylent.example',
      balanceCents: -9_99,
      delinquentFlag: true,
      createdAt: '2026-03-01T10:05:00.000Z',
    },
    {
      id: 'cus_hooli',
      name: 'Hooli',
      email: 'payables@hooli.example',
      balanceCents: 1_240_00,
      delinquentFlag: false,
      createdAt: '2026-03-19T17:40:00.000Z',
    },
  ].map((customer) => [customer.id, customer]),
)

function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
  return HttpResponse.json({ status, title, detail, errors }, { status })
}

function validate(body: { name?: unknown; email?: unknown }): Record<string, string[]> | undefined {
  const errors: Record<string, string[]> = {}
  if (typeof body.name !== 'string' || body.name.trim().length === 0) {
    errors.Name = ['Name is required.']
  }
  if (typeof body.email !== 'string' || body.email.trim().length === 0) {
    errors.Email = ['Email is required.']
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(body.email)) {
    errors.Email = ['Enter a valid email address.']
  }
  return Object.keys(errors).length > 0 ? errors : undefined
}

function findDuplicateEmail(email: string, excludeId?: string): boolean {
  for (const customer of customers.values()) {
    if (customer.id !== excludeId && customer.email.toLowerCase() === email.toLowerCase()) return true
  }
  return false
}

let nextId = customers.size + 1

export const customersHandlers = [
  http.get('/api/customers', () => {
    return HttpResponse.json(Array.from(customers.values()))
  }),

  http.get('/api/customers/:id', ({ params }) => {
    const customer = customers.get(params.id as string)
    if (!customer) return problem(404, 'Customer not found.')
    return HttpResponse.json(customer)
  }),

  http.post('/api/customers', async ({ request }) => {
    const body = (await request.json()) as { name?: unknown; email?: unknown }

    const errors = validate(body)
    if (errors) return problem(400, 'Validation failed.', undefined, errors)

    const name = body.name as string
    const email = body.email as string

    if (findDuplicateEmail(email)) {
      return problem(409, 'A customer with this email already exists.')
    }

    const customer: Customer = {
      id: `cus_${String(nextId++).padStart(3, '0')}`,
      name,
      email,
      balanceCents: 0,
      delinquentFlag: false,
      createdAt: new Date().toISOString(),
    }
    customers.set(customer.id, customer)

    return HttpResponse.json(customer, { status: 201 })
  }),

  http.put('/api/customers/:id', async ({ params, request }) => {
    const id = params.id as string
    const existing = customers.get(id)
    if (!existing) return problem(404, 'Customer not found.')

    const body = (await request.json()) as { name?: unknown; email?: unknown }

    const errors = validate(body)
    if (errors) return problem(400, 'Validation failed.', undefined, errors)

    const name = body.name as string
    const email = body.email as string

    if (findDuplicateEmail(email, id)) {
      return problem(409, 'A customer with this email already exists.')
    }

    const updated: Customer = { ...existing, name, email }
    customers.set(id, updated)

    return HttpResponse.json(updated)
  }),
]
