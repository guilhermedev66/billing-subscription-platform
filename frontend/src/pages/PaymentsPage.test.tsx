import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { customersHandlers } from '@/mocks/customersHandlers'
import { invoicesHandlers } from '@/mocks/invoicesHandlers'
import { paymentsHandlers } from '@/mocks/paymentsHandlers'
import { server } from '@/mocks/server'
import { renderWithProviders } from '@/test/test-utils'
import { PaymentsPage } from './PaymentsPage'

describe('PaymentsPage', () => {
  it('surfaces at-risk invoices with the joined customer name and a risk badge', async () => {
    server.use(...customersHandlers, ...invoicesHandlers, ...paymentsHandlers)
    renderWithProviders(<PaymentsPage />)

    expect(await screen.findByText('Initech')).toBeInTheDocument()
    const initechRow = screen.getByText('Initech').closest('tr')
    expect(initechRow).not.toBeNull()
    expect(within(initechRow as HTMLElement).getByText('INV-2026-0003')).toBeInTheDocument()
    expect(within(initechRow as HTMLElement).getByText('Open')).toBeInTheDocument()

    expect(screen.getByText('Umbrella Corp')).toBeInTheDocument()
    const umbrellaRow = screen.getByText('Umbrella Corp').closest('tr')
    expect(umbrellaRow).not.toBeNull()
    expect(within(umbrellaRow as HTMLElement).getByText('INV-2026-0004')).toBeInTheDocument()
    expect(within(umbrellaRow as HTMLElement).getByText('Uncollectible')).toBeInTheDocument()
    expect(within(umbrellaRow as HTMLElement).getByText('Exhausted')).toBeInTheDocument()
  })

  it('does not list an invoice that is not in a dunning cycle', async () => {
    server.use(...customersHandlers, ...invoicesHandlers, ...paymentsHandlers)
    renderWithProviders(<PaymentsPage />)

    await screen.findByText('Initech')
    expect(screen.queryByText('Wayne Enterprises')).not.toBeInTheDocument()
  })

  it('shows an empty state for recent activity when nothing has been charged yet this session', async () => {
    server.use(...customersHandlers, ...invoicesHandlers, ...paymentsHandlers)
    renderWithProviders(<PaymentsPage />)

    await screen.findByText('Initech')
    expect(screen.getByText('No payment attempts yet this session')).toBeInTheDocument()
  })
})
