import { Webhook } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function DevelopersPage() {
  return (
    <ModulePlaceholder
      title="Developers"
      icon={Webhook}
      milestone="M5"
      description="Webhook endpoint registration, the expandable JSON event timeline, and API keys."
    />
  )
}
