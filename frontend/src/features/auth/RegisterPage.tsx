import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { FormField } from '@/components/ui/FormField'
import { Input } from '@/components/ui/Input'
import { ApiError } from '@/lib/api/client'
import { useAuth } from './AuthContext'
import { AuthLayout } from './AuthLayout'
import { registerSchema, type RegisterValues } from './schemas'

export function RegisterPage() {
  const { register: registerAccount } = useAuth()
  const navigate = useNavigate()
  const [fallbackError, setFallbackError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: { organizationName: '', email: '', password: '', confirmPassword: '' },
  })

  const onSubmit = async (values: RegisterValues) => {
    setFallbackError(null)
    try {
      await registerAccount(values)
      navigate('/', { replace: true })
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setFallbackError('Something went wrong. Please try again.')
        return
      }
      if (error.kind === 'validation' && error.fieldErrors) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field in values) {
            setError(field as keyof RegisterValues, { message: messages[0] })
          }
        }
        return
      }
      if (error.kind === 'conflict') {
        setError('email', { message: 'An account with this email already exists.' })
        return
      }
      setFallbackError(error.message)
    }
  }

  return (
    <AuthLayout
      title="Create your organization"
      subtitle="Simulator mode — no real billing data."
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-foreground underline-offset-4 hover:underline">
            Sign in
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
        <FormField id="organizationName" label="Organization name" error={errors.organizationName?.message}>
          <Input autoComplete="organization" {...register('organizationName')} />
        </FormField>
        <FormField id="email" label="Email" error={errors.email?.message}>
          <Input type="email" autoComplete="email" {...register('email')} />
        </FormField>
        <FormField id="password" label="Password" error={errors.password?.message}>
          <Input type="password" autoComplete="new-password" {...register('password')} />
        </FormField>
        <FormField id="confirmPassword" label="Confirm password" error={errors.confirmPassword?.message}>
          <Input type="password" autoComplete="new-password" {...register('confirmPassword')} />
        </FormField>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Creating account…' : 'Create account'}
        </Button>
      </form>
    </AuthLayout>
  )
}
