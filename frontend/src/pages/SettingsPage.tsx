import { Settings } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function SettingsPage() {
  return (
    <ModulePlaceholder
      title="Settings"
      icon={Settings}
      description="Organization settings such as currency, invoice numbering, and API keys were never an explicit roadmap deliverable — see the README for what's implemented."
    />
  )
}
