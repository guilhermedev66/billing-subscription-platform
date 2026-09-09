import { Settings } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function SettingsPage() {
  return (
    <ModulePlaceholder
      title="Settings"
      icon={Settings}
      milestone="M5"
      description="Organization settings (currency, invoice numbering, API keys) and the simulation console."
    />
  )
}
