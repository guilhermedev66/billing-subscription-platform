import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Link, useLocation, useNavigate, type Location } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { FormField } from '@/components/ui/FormField'
import { Input } from '@/components/ui/Input'
import { ApiError } from '@/lib/api/client'
import { useAuth } from './AuthContext'
import { AuthLayout } from './AuthLayout'
import { loginSchema, type LoginValues } from './schemas'

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [fallbackError, setFallbackError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginValues>({ resolver: zodResolver(loginSchema), defaultValues: { email: '', password: '' } })

  const onSubmit = async (values: LoginValues) => {
    setFallbackError(null)
    try {
      await login(values)
      const from = (location.state as { from?: Location } | null)?.from
      navigate(from ?? '/', { replace: true })
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setFallbackError('Something went wrong. Please try again.')
        return
      }
      if (error.kind === 'validation' && error.fieldErrors) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field === 'email' || field === 'password') {
            setError(field, { message: messages[0] })
          }
        }
        return
      }
      // The Identity module deliberately collapses "wrong password" and
      // "locked out" into one 401 to avoid a lockout oracle — surface our
      // own copy rather than trusting whatever the backend's title says.
      setFallbackError(error.kind === 'unauthorized' ? 'Invalid email or password.' : error.message)
    }
  }

  return (
    <AuthLayout
      title="Sign in"
      subtitle="Simulator mode — no real billing data."
      footer={
        <>
          No account?{' '}
          <Link to="/register" className="font-medium text-foreground underline-offset-4 hover:underline">
            Create one
          </Link>
        </>
      }
    >
      <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4" noValidate>
        {fallbackError && (
          <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
            {fallbackError}
          </p>
        )}
        <FormField id="email" label="Email" error={errors.email?.message}>
          <Input type="email" autoComplete="email" {...register('email')} />
        </FormField>
        <FormField id="password" label="Password" error={errors.password?.message}>
          <Input type="password" autoComplete="current-password" {...register('password')} />
        </FormField>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Signing in…' : 'Sign in'}
        </Button>
      </form>
    </AuthLayout>
  )
}
