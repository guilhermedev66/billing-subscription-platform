import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { customersHandlers } from '@/mocks/customersHandlers'
import { invoicesHandlers } from '@/mocks/invoicesHandlers'
import { paymentsHandlers } from '@/mocks/paymentsHandlers'
import { server } from '@/mocks/server'
import { renderWithProviders } from '@/test/test-utils'
import { InvoiceDetailPage } from './InvoiceDetailPage'

function renderDetail(id: string) {
  server.use(...customersHandlers, ...invoicesHandlers, ...paymentsHandlers)
  return renderWithProviders(
    <Routes>
      <Route path="/invoices/:id" element={<InvoiceDetailPage />} />
    </Routes>,
    { route: `/invoices/${id}` },
  )
}

describe('InvoiceDetailPage', () => {
  it('renders the bill for a paid invoice', async () => {
    renderDetail('inv_1')

    expect(await screen.findAllByText('INV-2026-0001')).not.toHaveLength(0)
    expect(await screen.findByText('Wayne Enterprises')).toBeInTheDocument()
    expect(screen.getByText('billing@wayne.example')).toBeInTheDocument()
    expect(screen.getByText('Cloud Analytics Platform — monthly')).toBeInTheDocument()
    expect(screen.getAllByText('$29.00').length).toBeGreaterThan(0)
    expect(screen.getByText(/paid on/i)).toBeInTheDocument()
  })

  it('renders dunning status and the charge action for an open, mid-dunning invoice', async () => {
    renderDetail('inv_3')

    expect(await screen.findAllByText('INV-2026-0003')).not.toHaveLength(0)
    expect(await screen.findByText('Initech')).toBeInTheDocument()
    expect(screen.getAllByText('Open').length).toBeGreaterThan(0)
    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /charge invoice/i })).toBeEnabled()
    expect(await screen.findByText('No payment attempts have been made against this invoice yet.')).toBeInTheDocument()
  })

  it('disables charging and explains why for a paid invoice', async () => {
    renderDetail('inv_1')

    expect(await screen.findAllByText('INV-2026-0001')).not.toHaveLength(0)
    expect(screen.getByRole('button', { name: /charge invoice/i })).toBeDisabled()
    expect(screen.getByText('This invoice is already paid.')).toBeInTheDocument()
  })

  it('charges an open invoice via the test-card selector and shows the result and recent activity', async () => {
    renderDetail('inv_8')
    const user = userEvent.setup()

    expect(await screen.findAllByText('INV-2026-0008')).not.toHaveLength(0)
    await user.click(screen.getByRole('button', { name: /charge invoice/i }))

    await waitFor(() => expect(screen.getByText('Result:')).toBeInTheDocument())
    expect(within(screen.getByText('Result:').parentElement as HTMLElement).getByText('Succeeded')).toBeInTheDocument()

    expect(await screen.findByText('Card ending in 4242')).toBeInTheDocument()
    expect(screen.queryByText('No payment attempts have been made against this invoice yet.')).not.toBeInTheDocument()
  })

  it('requires a second confirm to complete a 3D Secure challenge, without affecting dunning', async () => {
    // A dedicated, otherwise-untouched invoice — inv_8 is charged (mutated) by the
    // previous test, and the mock store is module-level state shared across tests
    // in this file, so reusing it here would race against that test's own charge.
    renderDetail('inv_9')
    const user = userEvent.setup()

    expect(await screen.findAllByText('INV-2026-0009')).not.toHaveLength(0)
    await user.selectOptions(screen.getByLabelText('Test card'), '4000000000003022')
    await user.click(screen.getByRole('button', { name: /charge invoice/i }))

    await waitFor(() => expect(screen.getByText('Result:')).toBeInTheDocument())
    expect(within(screen.getByText('Result:').parentElement as HTMLElement).getByText('Awaiting 3D Secure confirmation')).toBeInTheDocument()
    // A pending challenge is not a decline — no dunning effect yet.
    expect(screen.getAllByText('Open').length).toBeGreaterThan(0)
    expect(screen.getByText('No retry scheduled.')).toBeInTheDocument()

    const confirmButton = screen.getByRole('button', { name: /simulated challenge — click to confirm/i })
    await user.click(confirmButton)

    await waitFor(() => expect(within(screen.getByText('Result:').parentElement as HTMLElement).getByText('Succeeded')).toBeInTheDocument())
    expect(screen.getAllByText('Paid').length).toBeGreaterThan(0)
  })

  it('voids a draft invoice after confirmation', async () => {
    renderDetail('inv_6')
    const user = userEvent.setup()

    expect(await screen.findAllByText('INV-2026-0006')).not.toHaveLength(0)
    await user.click(screen.getByRole('button', { name: /void invoice/i }))

    expect(await screen.findByText(/real state transition/i)).toBeInTheDocument()
    await user.click(screen.getAllByRole('button', { name: /^void invoice$/i })[1])

    await waitFor(() => expect(screen.getAllByText('Void').length).toBeGreaterThan(0))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
