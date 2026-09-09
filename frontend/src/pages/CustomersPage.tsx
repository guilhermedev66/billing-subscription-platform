import { Users } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function CustomersPage() {
  return (
    <ModulePlaceholder
      title="Customers"
      icon={Users}
      milestone="M2"
      description="Customer list, credit balance, delinquency status, and the slide-over inspection sheet."
    />
  )
}
