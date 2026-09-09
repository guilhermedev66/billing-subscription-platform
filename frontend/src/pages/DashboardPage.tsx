import { LayoutDashboard } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function DashboardPage() {
  return (
    <ModulePlaceholder
      title="Overview"
      icon={LayoutDashboard}
      milestone="M6"
      description="MRR/ARR, churn, and the at-risk dunning banner land once the analytics read models exist."
    />
  )
}
