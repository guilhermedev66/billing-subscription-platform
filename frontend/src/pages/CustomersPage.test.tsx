import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { customersHandlers } from '@/mocks/customersHandlers'
import { server } from '@/mocks/server'
import { renderWithProviders } from '@/test/test-utils'
import { CustomersPage } from './CustomersPage'

describe('CustomersPage', () => {
  it('renders the customer list from the API', async () => {
    server.use(...customersHandlers)
    renderWithProviders(<CustomersPage />)

    expect(await screen.findByRole('cell', { name: 'Aperture Science' })).toBeInTheDocument()

    const row = screen.getByRole('cell', { name: 'Aperture Science' }).closest('tr')!
    expect(within(row).getByText('-$48.00 owed')).toBeInTheDocument()
    expect(within(row).getByText('Delinquent')).toBeInTheDocument()

    const cleanRow = screen.getByRole('cell', { name: 'Globex Corporation' }).closest('tr')!
    expect(within(cleanRow).getByText('+$12.50 credit')).toBeInTheDocument()
    expect(within(cleanRow).getByText('Good standing')).toBeInTheDocument()
  })

  it('exposes each row as a real, keyboard-activatable button (not an ambiguous row click target)', async () => {
    server.use(...customersHandlers)
    renderWithProviders(<CustomersPage />)

    const rowButton = await screen.findByRole('button', { name: 'Globex Corporation' })
    expect(rowButton.tagName).toBe('BUTTON')
  })

  it('opens the detail sheet for a customer when its row is clicked', async () => {
    server.use(...customersHandlers)
    const user = userEvent.setup()
    renderWithProviders(<CustomersPage />)

    await user.click(await screen.findByRole('button', { name: 'Globex Corporation' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getAllByText('ap@globex.example').length).toBeGreaterThan(0)
    expect(within(dialog).getByText('+$12.50 credit')).toBeInTheDocument()
  })

  it('opens the detail sheet via keyboard (Space) on a focused row', async () => {
    server.use(...customersHandlers)
    const user = userEvent.setup()
    renderWithProviders(<CustomersPage />)

    const rowButton = await screen.findByRole('button', { name: 'Globex Corporation' })
    rowButton.focus()
    await user.keyboard(' ')

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getAllByText('ap@globex.example').length).toBeGreaterThan(0)
  })

  it('creates a new customer and shows it in the list', async () => {
    server.use(...customersHandlers)
    const user = userEvent.setup()
    renderWithProviders(<CustomersPage />)

    await screen.findByRole('cell', { name: 'Aperture Science' })

    await user.click(screen.getByRole('button', { name: /new customer/i }))

    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Name'), 'Cyberdyne Systems')
    await user.type(within(dialog).getByLabelText('Email'), 'billing@cyberdyne.example')
    await user.click(within(dialog).getByRole('button', { name: /create customer/i }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByRole('cell', { name: 'Cyberdyne Systems' })).toBeInTheDocument()
  })

  it('edits an existing customer from the detail sheet', async () => {
    server.use(...customersHandlers)
    const user = userEvent.setup()
    renderWithProviders(<CustomersPage />)

    await user.click(await screen.findByRole('button', { name: 'Initech' }))
    const dialog = await screen.findByRole('dialog')

    await user.click(within(dialog).getByRole('button', { name: /edit/i }))
    const nameInput = within(dialog).getByLabelText('Name')
    await user.clear(nameInput)
    await user.type(nameInput, 'Initech LLC')
    await user.click(within(dialog).getByRole('button', { name: /save changes/i }))

    await waitFor(() => expect(within(dialog).getByRole('heading', { name: 'Initech LLC' })).toBeInTheDocument())
    expect(await screen.findByRole('cell', { name: 'Initech LLC' })).toBeInTheDocument()
  })

  it('shows a form-level alert when the server rejects with a validation key that has no matching field', async () => {
    server.use(...customersHandlers)
    server.use(
      http.post('/api/customers', () =>
        HttpResponse.json(
          { status: 400, title: 'Validation failed.', errors: { customer: ['Organization customer limit reached.'] } },
          { status: 400 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<CustomersPage />)

    await user.click(screen.getByRole('button', { name: /new customer/i }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Name'), 'Blocked Co')
    await user.type(within(dialog).getByLabelText('Email'), 'blocked@example.com')
    await user.click(within(dialog).getByRole('button', { name: /create customer/i }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Organization customer limit reached.')
  })
})
