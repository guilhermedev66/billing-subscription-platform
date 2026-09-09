import { FileText } from 'lucide-react'
import { ModulePlaceholder } from './ModulePlaceholder'

export function InvoicesPage() {
  return (
    <ModulePlaceholder
      title="Invoices"
      icon={FileText}
      milestone="M4"
      description="Invoice list and the split-pane paper-style invoice detail view."
    />
  )
}
