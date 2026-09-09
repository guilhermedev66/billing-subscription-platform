import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { AuthProvider } from '@/features/auth/AuthContext'
import { ThemeProvider } from '@/lib/theme/ThemeProvider'

export function renderWithProviders(ui: ReactElement, { route = '/' }: { route?: string } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <AuthProvider>
          <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
        </AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>,
  )
}
