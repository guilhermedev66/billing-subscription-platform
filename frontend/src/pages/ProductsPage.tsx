import { Package } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function ProductsPage() {
  return (
    <ModulePlaceholder
      title="Products"
      icon={Package}
      milestone="M2"
      description="Product catalog and the flat / per-seat / tiered / metered price builder."
    />
  )
}
