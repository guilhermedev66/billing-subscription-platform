import { Repeat } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function SubscriptionsPage() {
  return (
    <ModulePlaceholder
      title="Subscriptions"
      icon={Repeat}
      milestone="M3"
      description="Subscription state machine, the upgrade/downgrade flow, and the Preview Proration modal."
    />
  )
}
