import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { renderWithProviders } from '@/test/test-utils'
import { useAuth } from './AuthContext'
import { LoginPage } from './LoginPage'

function AuthProbe() {
  const { user } = useAuth()
  return <div data-testid="user">{user ? user.email : 'anonymous'}</div>
}

describe('LoginPage', () => {
  it('signs in with valid credentials and updates the auth session', async () => {
    const user = userEvent.setup()
    renderWithProviders(
      <>
        <LoginPage />
        <AuthProbe />
      </>,
    )

    await user.type(screen.getByLabelText('Email'), 'demo@example.com')
    await user.type(screen.getByLabelText('Password'), 'password123')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('demo@example.com'))
  })

  it('shows one generic error for invalid credentials, without saying which field was wrong', async () => {
    const user = userEvent.setup()
    renderWithProviders(<LoginPage />)

    await user.type(screen.getByLabelText('Email'), 'nobody@example.com')
    await user.type(screen.getByLabelText('Password'), 'wrong-password')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.')
  })

  it('rejects an empty submission client-side before hitting the network', async () => {
    const user = userEvent.setup()
    renderWithProviders(<LoginPage />)

    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText('Email is required')).toBeInTheDocument()
  })
})
