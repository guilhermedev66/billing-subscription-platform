import { CreditCard } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function PaymentsPage() {
  return (
    <ModulePlaceholder
      title="Payments"
      icon={CreditCard}
      milestone="M4"
      description="Test-card selector, payment attempt history, and the dunning cockpit."
    />
  )
}
