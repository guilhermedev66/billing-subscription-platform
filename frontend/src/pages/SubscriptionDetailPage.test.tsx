import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { catalogHandlers } from '@/mocks/catalogHandlers'
import { customersHandlers } from '@/mocks/customersHandlers'
import { server } from '@/mocks/server'
import { subscriptionsHandlers } from '@/mocks/subscriptionsHandlers'
import { renderWithProviders } from '@/test/test-utils'
import { SubscriptionDetailPage } from './SubscriptionDetailPage'

function renderDetail(id: string) {
  server.use(...customersHandlers, ...catalogHandlers, ...subscriptionsHandlers)
  return renderWithProviders(
    <Routes>
      <Route path="/subscriptions/:id" element={<SubscriptionDetailPage />} />
    </Routes>,
    { route: `/subscriptions/${id}` },
  )
}

describe('SubscriptionDetailPage', () => {
  it('renders the customer, plan, status, and billing dates', async () => {
    renderDetail('sub_1')

    expect(await screen.findByRole('heading', { name: 'Wayne Enterprises' })).toBeInTheDocument()
    expect(screen.getByText('billing@wayne.example')).toBeInTheDocument()
    expect(screen.getByText('Cloud Analytics Platform — $29.00/mo')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(screen.getByText('Current period start')).toBeInTheDocument()
    expect(screen.getByText('Current period end')).toBeInTheDocument()
  })

  it('shows the seat count and the Change Seat Count action only for a per-seat plan', async () => {
    renderDetail('sub_2')

    expect(await screen.findByRole('heading', { name: 'Stark Industries' })).toBeInTheDocument()
    expect(screen.getByText('Seats')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /change seat count/i })).toBeInTheDocument()
  })

  it('shows a loading state in the Preview Proration modal while the preview is in flight', async () => {
    renderDetail('sub_1')
    server.use(
      http.post('/api/subscriptions/:id/preview-proration', async () => {
        await delay('infinite')
        return HttpResponse.json({})
      }),
    )
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /upgrade \/ downgrade plan/i }))
    await user.selectOptions(screen.getByLabelText('New plan'), 'price_2')
    await user.click(screen.getByRole('button', { name: /preview change/i }))

    expect(await screen.findByRole('status', { name: /loading proration preview/i })).toBeInTheDocument()
  })

  it('shows an error state in the Preview Proration modal when the preview fails, with a way to retry', async () => {
    renderDetail('sub_1')
    server.use(
      http.post('/api/subscriptions/:id/preview-proration', () =>
        HttpResponse.json({ status: 500, title: 'Proration engine is unavailable.' }, { status: 500 }),
      ),
    )
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /upgrade \/ downgrade plan/i }))
    await user.selectOptions(screen.getByLabelText('New plan'), 'price_2')
    await user.click(screen.getByRole('button', { name: /preview change/i }))

    expect(await screen.findByText('Proration engine is unavailable.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })

  it('previews and applies a plan upgrade end-to-end', async () => {
    renderDetail('sub_1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /upgrade \/ downgrade plan/i }))
    await user.selectOptions(screen.getByLabelText('New plan'), 'price_2')
    await user.click(screen.getByRole('button', { name: /preview change/i }))

    const dialog = await screen.findByRole('dialog', { name: 'Confirm subscription plan change' })
    expect(within(dialog).getByText(/amount due immediately/i)).toBeInTheDocument()
    expect(within(dialog).getByText(/prorated credit/i)).toBeInTheDocument()
    expect(within(dialog).getByText(/prorated charge/i)).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: /confirm & charge/i }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Cloud Analytics Platform — $290.00/yr')).toBeInTheDocument()
  })

  it('previews and applies a seat count change end-to-end', async () => {
    renderDetail('sub_2')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /change seat count/i }))
    const seatInput = screen.getByLabelText('New seat count')
    await user.clear(seatInput)
    await user.type(seatInput, '8')
    await user.click(screen.getByRole('button', { name: /preview change/i }))

    const dialog = await screen.findByRole('dialog', { name: 'Confirm seat count change' })
    expect(within(dialog).getByText('5 → 8')).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: /confirm & charge/i }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await waitFor(() => expect(screen.getAllByText('8').length).toBeGreaterThan(0))
  })

  it('pauses an active subscription via the Pause action', async () => {
    renderDetail('sub_1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /^pause$/i }))
    const dialog = await screen.findByRole('dialog', { name: 'Pause subscription' })
    await user.click(within(dialog).getByRole('button', { name: /pause subscription/i }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Paused')).toBeInTheDocument()
  })

  it('cancels a subscription immediately via the Cancel action, with no scheduling option', async () => {
    renderDetail('sub_1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /^cancel$/i }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancel subscription' })
    expect(within(dialog).queryByRole('radio')).not.toBeInTheDocument()
    expect(within(dialog).getByText(/immediately/i)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: /cancel subscription/i }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Canceled')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^cancel$/i })).not.toBeInTheDocument()
    expect(screen.getByText(/canceled on/i)).toBeInTheDocument()
  })

  it('resumes a paused subscription via the Resume action', async () => {
    renderDetail('sub_7')
    const user = userEvent.setup()

    expect(await screen.findByText('Paused')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /^resume$/i }))
    const dialog = await screen.findByRole('dialog', { name: 'Resume subscription' })
    await user.click(within(dialog).getByRole('button', { name: /resume subscription/i }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Active')).toBeInTheDocument()
  })

  it('shows an Account Credit notice, not a negative charge, when a change results in a credit', async () => {
    renderDetail('sub_2')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /change seat count/i }))
    const seatInput = screen.getByLabelText('New seat count')
    await user.clear(seatInput)
    await user.type(seatInput, '2')
    await user.click(screen.getByRole('button', { name: /preview change/i }))

    const dialog = await screen.findByRole('dialog', { name: 'Confirm seat count change' })
    expect(within(dialog).getByText(/account credit/i)).toBeInTheDocument()
    expect(within(dialog).queryByText(/amount due immediately/i)).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: /confirm & issue credit/i })).toBeInTheDocument()
  })
})
