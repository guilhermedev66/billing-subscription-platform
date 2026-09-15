import type { LucideIcon } from 'lucide-react'
import { EmptyState } from '@/components/ui/EmptyState'

interface ModulePlaceholderProps {
  title: string
  description: string
  icon: LucideIcon
  /** Omit when the module is deliberately out of scope, not just "not yet built" — renders honest out-of-scope copy instead of a milestone promise that goes stale once that milestone ships. */
  milestone?: string
}

/** Every module route not yet built lands here, keyed by milestone; a route that's permanently out of scope by design omits `milestone` and gets honest scope copy instead. Keeps routing/nav honest from day one. */
export function ModulePlaceholder({ title, description, icon, milestone }: ModulePlaceholderProps) {
  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-semibold tracking-tight text-foreground">{title}</h1>
      <EmptyState
        icon={icon}
        title={milestone ? `${title} lands in ${milestone}` : `${title} isn't part of this project's scope`}
        description={description}
      />
    </div>
  )
}
