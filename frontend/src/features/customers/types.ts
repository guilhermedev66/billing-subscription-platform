/**
 * Customer entity — field names/casing mirror BillingPlatform.Customers.Application's
 * CustomerSummary record exactly (System.Text.Json Web defaults camelCase it:
 * DelinquentFlag -> delinquentFlag). balanceCents is ALWAYS integer cents (money
 * invariant #1): positive is customer credit, negative is outstanding debt.
 * delinquentFlag is set automatically by the backend when any invoice enters
 * dunning/past_due — never set from the UI.
 */
export interface Customer {
  id: string
  name: string
  email: string
  balanceCents: number
  delinquentFlag: boolean
  createdAt: string
}

export interface CreateCustomerInput {
  name: string
  email: string
}

export interface UpdateCustomerInput {
  name: string
  email: string
}
