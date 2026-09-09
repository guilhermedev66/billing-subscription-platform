import {
  CreditCard,
  FileText,
  LayoutDashboard,
  Package,
  Repeat,
  Settings,
  Users,
  Webhook,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export interface NavItem {
  label: string
  to: string
  icon: LucideIcon
}

/** Mirrors the module map in docs/research/M0-Billing-Platform-Research-Brief.md §5. */
export const navItems: NavItem[] = [
  { label: 'Overview', to: '/', icon: LayoutDashboard },
  { label: 'Customers', to: '/customers', icon: Users },
  { label: 'Products', to: '/products', icon: Package },
  { label: 'Subscriptions', to: '/subscriptions', icon: Repeat },
  { label: 'Invoices', to: '/invoices', icon: FileText },
  { label: 'Payments', to: '/payments', icon: CreditCard },
  { label: 'Developers', to: '/developers', icon: Webhook },
  { label: 'Settings', to: '/settings', icon: Settings },
]
