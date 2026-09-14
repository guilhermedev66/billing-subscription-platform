import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { invoicesHandlers } from '@/mocks/invoicesHandlers'
import { emptyReportingHandlers, noAtRiskReportingHandlers, reportingHandlers } from '@/mocks/reportingHandlers'
import { server } from '@/mocks/server'
import { simulationHandlers } from '@/mocks/simulationHandlers'
import { subscriptionsHandlers } from '@/mocks/subscriptionsHandlers'
import { renderWithProviders } from '@/test/test-utils'
import { DashboardPage } from './DashboardPage'

describe('DashboardPage', () => {
  it('renders the hero KPI grid, net growth, and waterfall from the real Reporting contract', async () => {
    server.use(...subscriptionsHandlers, ...invoicesHandlers, ...simulationHandlers, ...reportingHandlers)
    renderWithProviders(<DashboardPage />)

    expect(await screen.findByText('$147.00')).toBeInTheDocument() // MRR
    expect(screen.getByText('$1,764.00')).toBeInTheDocument() // ARR
    expect(screen.getByText('Active Subscriptions')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument() // sub_1, sub_2, sub_8 are Active
    expect(screen.getByText('1 trial · 1 past due')).toBeInTheDocument() // sub_3 trialing, sub_4 past_due

    expect(await screen.findAllByText('+$20.00')).toHaveLength(2) // Net MRR Growth (30d) card + waterfall Net Movement — same authoritative figure
    expect(screen.getByText('New $29.00 · Churn -$12.00')).toBeInTheDocument()

    expect(screen.getByText('MRR Waterfall (Last 30 Days)')).toBeInTheDocument()
    expect(screen.getByText('Net Movement')).toBeInTheDocument()
  })

  it('shows the At-Risk Revenue Alert banner with a link to the dunning cockpit when atRiskArrCents > 0', async () => {
    server.use(...subscriptionsHandlers, ...invoicesHandlers, ...simulationHandlers, ...reportingHandlers)
    renderWithProviders(<DashboardPage />)

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.getByText(/\$29\.00 At-Risk MRR across 2 past-due subscriptions in active dunning\./)).toBeInTheDocument()
    expect(screen.getByText(/INV-2026-0003/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Open Dunning Cockpit/ })).toHaveAttribute('href', '/payments')
  })

  it('hides the At-Risk Revenue Alert banner when the org has no past-due revenue', async () => {
    server.use(...subscriptionsHandlers, ...invoicesHandlers, ...simulationHandlers, ...noAtRiskReportingHandlers)
    renderWithProviders(<DashboardPage />)

    await screen.findByText('$147.00')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByText(/At-Risk MRR/)).not.toBeInTheDocument()
  })

  it('shows an empty state for an org with zero subscriptions', async () => {
    server.use(
      http.get('/api/subscriptions', () => HttpResponse.json([])),
      http.get('/api/invoices/', () => HttpResponse.json([])),
      ...simulationHandlers,
      ...emptyReportingHandlers,
    )
    renderWithProviders(<DashboardPage />)

    expect(await screen.findByText('No revenue yet')).toBeInTheDocument()
    expect(screen.queryByText('MRR')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
