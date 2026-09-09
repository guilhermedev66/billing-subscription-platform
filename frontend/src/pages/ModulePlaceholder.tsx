import type { LucideIcon } from 'lucide-react'
import { EmptyState } from '@/components/ui/EmptyState'

interface ModulePlaceholderProps {
  title: string
  description: string
  icon: LucideIcon
  milestone: string
}

/** Every module route lands here until its milestone builds the real screen — keeps routing/nav honest from day one. */
export function ModulePlaceholder({ title, description, icon, milestone }: ModulePlaceholderProps) {
  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-semibold tracking-tight text-foreground">{title}</h1>
      <EmptyState icon={icon} title={`${title} lands in ${milestone}`} description={description} />
    </div>
  )
}
