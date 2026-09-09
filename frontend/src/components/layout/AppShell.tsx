import { useRef, useState, type ReactNode } from 'react'
import { SimulationBar } from './SimulationBar'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'

export function AppShell({ children }: { children: ReactNode }) {
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const sidebarTriggerRef = useRef<HTMLButtonElement>(null)

  return (
    <div className="flex min-h-screen flex-col bg-background">
      <SimulationBar />
      <div className="flex flex-1">
        <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} triggerRef={sidebarTriggerRef} />
        <div className="flex min-h-0 flex-1 flex-col">
          <Topbar onOpenSidebar={() => setSidebarOpen(true)} sidebarTriggerRef={sidebarTriggerRef} />
          <main className="flex-1 p-6">{children}</main>
        </div>
      </div>
    </div>
  )
}
