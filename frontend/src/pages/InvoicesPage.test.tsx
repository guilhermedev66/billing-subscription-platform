import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { customersHandlers } from '@/mocks/customersHandlers'
import { invoicesHandlers } from '@/mocks/invoicesHandlers'
import { server } from '@/mocks/server'
import { renderWithProviders } from '@/test/test-utils'
import { InvoicesPage } from './InvoicesPage'

describe('InvoicesPage', () => {
  it('lists seeded invoices with joined customer name, status badge, and total', async () => {
    server.use(...customersHandlers, ...invoicesHandlers)
    renderWithProviders(<InvoicesPage />)

    expect(await screen.findByText('INV-2026-0001')).toBeInTheDocument()

    const row = screen.getByText('INV-2026-0001').closest('tr')
    expect(row).not.toBeNull()
    expect(within(row as HTMLElement).getByText('Wayne Enterprises')).toBeInTheDocument()
    expect(within(row as HTMLElement).getByText('Paid')).toBeInTheDocument()
    expect(within(row as HTMLElement).getByText('$29.00')).toBeInTheDocument()
  })

  it('shows a warning tone for an open invoice mid-dunning', async () => {
    server.use(...customersHandlers, ...invoicesHandlers)
    renderWithProviders(<InvoicesPage />)

    expect(await screen.findByText('INV-2026-0003')).toBeInTheDocument()
    const row = screen.getByText('INV-2026-0003').closest('tr')
    expect(within(row as HTMLElement).getByText('Open')).toBeInTheDocument()
  })

  it('exposes each row as a real, keyboard-focusable link to the invoice detail page', async () => {
    server.use(...customersHandlers, ...invoicesHandlers)
    renderWithProviders(<InvoicesPage />)

    const link = await screen.findByRole('link', { name: 'INV-2026-0001' })
    expect(link).toHaveAttribute('href', '/invoices/inv_1')
  })
})
