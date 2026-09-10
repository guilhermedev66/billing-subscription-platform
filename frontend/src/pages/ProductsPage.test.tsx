import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { catalogHandlers } from '@/mocks/catalogHandlers'
import { server } from '@/mocks/server'
import { renderWithProviders } from '@/test/test-utils'
import { ProductsPage } from './ProductsPage'

describe('ProductsPage', () => {
  it('lists seeded products with their price summary', async () => {
    server.use(...catalogHandlers)
    renderWithProviders(<ProductsPage />)

    expect(await screen.findByText('Cloud Analytics Platform')).toBeInTheDocument()
    expect(screen.getByText('Team Workspace')).toBeInTheDocument()
    expect(screen.getByText('API Gateway')).toBeInTheDocument()

    const row = screen.getByText('Cloud Analytics Platform').closest('tr')
    expect(row).not.toBeNull()
    expect(within(row as HTMLElement).getByText(/\$29\.00\/mo/)).toBeInTheDocument()
    expect(within(row as HTMLElement).getByText(/\+1 more/)).toBeInTheDocument()
  })

  it('exposes each row as a real, keyboard-focusable link to the product detail page', async () => {
    server.use(...catalogHandlers)
    renderWithProviders(<ProductsPage />)

    const link = await screen.findByRole('link', { name: 'Cloud Analytics Platform' })
    expect(link).toHaveAttribute('href', '/products/prod_1')
  })

  it('creates a new product through the inline form', async () => {
    server.use(...catalogHandlers)
    const user = userEvent.setup()
    renderWithProviders(<ProductsPage />)

    await screen.findByText('Cloud Analytics Platform')

    await user.click(screen.getByRole('button', { name: /new product/i }))
    await user.type(screen.getByLabelText('Name'), 'Support Add-on')
    await user.click(screen.getByRole('button', { name: /create product/i }))

    await waitFor(() => expect(screen.getByText('Support Add-on')).toBeInTheDocument())
  })

  it('rejects a product name shorter than 2 characters before hitting the network', async () => {
    server.use(...catalogHandlers)
    const user = userEvent.setup()
    renderWithProviders(<ProductsPage />)

    await screen.findByText('Cloud Analytics Platform')

    await user.click(screen.getByRole('button', { name: /new product/i }))
    await user.type(screen.getByLabelText('Name'), 'A')
    await user.click(screen.getByRole('button', { name: /create product/i }))

    expect(await screen.findByText('Name must be at least 2 characters')).toBeInTheDocument()
  })

  it('shows a form-level alert when the server rejects with a validation key that has no matching field', async () => {
    server.use(...catalogHandlers)
    server.use(
      http.post('/api/catalog/products', () =>
        HttpResponse.json(
          { status: 400, title: 'Validation failed.', errors: { catalog: ['Organization product limit reached.'] } },
          { status: 400 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<ProductsPage />)

    await screen.findByText('Cloud Analytics Platform')

    await user.click(screen.getByRole('button', { name: /new product/i }))
    await user.type(screen.getByLabelText('Name'), 'Blocked Product')
    await user.click(screen.getByRole('button', { name: /create product/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Organization product limit reached.')
  })
})
