import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '@/mocks/server'
import { renderWithProviders } from '@/test/test-utils'
import { PriceBuilder } from './PriceBuilder'

describe('PriceBuilder', () => {
  it('shows a form-level alert when the server rejects with a validation key that has no matching field', async () => {
    server.use(
      http.post('/api/catalog/prices', () =>
        HttpResponse.json(
          { status: 400, title: 'Validation failed.', errors: { catalog: ['Tiered pricing must start at unit 1.'] } },
          { status: 400 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<PriceBuilder productId="prod_1" />)

    await user.type(screen.getByLabelText('Amount (USD)'), '29.00')
    await user.click(screen.getByRole('button', { name: /add price/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Tiered pricing must start at unit 1.')
  })
})
