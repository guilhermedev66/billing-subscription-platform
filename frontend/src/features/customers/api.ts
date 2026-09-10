import { api } from '@/lib/api/client'
import type { Customer, CreateCustomerInput, UpdateCustomerInput } from './types'

/**
 * Customers module endpoints per docs/ARCHITECTURE.md — mocked via MSW
 * (src/mocks/customersHandlers.ts) until Codex — Backend's Customers module is live.
 */
export function listCustomers() {
  return api.get<Customer[]>('/customers')
}

export function getCustomer(id: string) {
  return api.get<Customer>(`/customers/${id}`)
}

export function createCustomer(input: CreateCustomerInput) {
  return api.post<Customer>('/customers', input)
}

export function updateCustomer(id: string, input: UpdateCustomerInput) {
  return api.put<Customer>(`/customers/${id}`, input)
}
