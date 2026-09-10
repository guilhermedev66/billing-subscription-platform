import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { catalogHandlers } from '@/mocks/catalogHandlers'
import { customersHandlers } from '@/mocks/customersHandlers'
import { server } from '@/mocks/server'
import { subscriptionsHandlers } from '@/mocks/subscriptionsHandlers'
import { renderWithProviders } from '@/test/test-utils'
import { SubscriptionsPage } from './SubscriptionsPage'

describe('SubscriptionsPage', () => {
  it('lists seeded subscriptions with joined customer/plan names and a status badge', async () => {
    server.use(...customersHandlers, ...catalogHandlers, ...subscriptionsHandlers)
    renderWithProviders(<SubscriptionsPage />)

    expect(await screen.findByText('Wayne Enterprises')).toBeInTheDocument()

    const row = screen.getByText('Wayne Enterprises').closest('tr')
    expect(row).not.toBeNull()
    expect(within(row as HTMLElement).getByText('Cloud Analytics Platform — $29.00/mo')).toBeInTheDocument()
    expect(within(row as HTMLElement).getByText('Active')).toBeInTheDocument()
    expect(within(row as HTMLElement).getByText('—')).toBeInTheDocument()
  })

  it('shows the seat count only for per-seat plans', async () => {
    server.use(...customersHandlers, ...catalogHandlers, ...subscriptionsHandlers)
    renderWithProviders(<SubscriptionsPage />)

    expect(await screen.findByText('Stark Industries')).toBeInTheDocument()
    const perSeatRow = screen.getByText('Stark Industries').closest('tr')
    expect(within(perSeatRow as HTMLElement).getByText('5')).toBeInTheDocument()
    expect(within(perSeatRow as HTMLElement).getByText('Team Workspace — $12.00/seat/mo')).toBeInTheDocument()
  })

  it('exposes each row as a real, keyboard-focusable link to the subscription detail page', async () => {
    server.use(...customersHandlers, ...catalogHandlers, ...subscriptionsHandlers)
    renderWithProviders(<SubscriptionsPage />)

    const link = await screen.findByRole('link', { name: 'Wayne Enterprises' })
    expect(link).toHaveAttribute('href', '/subscriptions/sub_1')
  })
})
