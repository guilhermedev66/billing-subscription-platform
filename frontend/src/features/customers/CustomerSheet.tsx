import { useEffect, useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/Button'
import { FormField } from '@/components/ui/FormField'
import { Input } from '@/components/ui/Input'
import { Sheet } from '@/components/ui/Sheet'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { ApiError, splitFieldErrors } from '@/lib/api/client'
import { formatBalanceCents } from '@/lib/money'
import { createCustomer, updateCustomer } from './api'
import { customerFormSchema, type CustomerFormValues } from './schemas'
import type { Customer } from './types'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })

interface CustomerSheetProps {
  open: boolean
  onClose: () => void
  /** null means "New customer" — the sheet opens straight into the create form. */
  customer: Customer | null
}

/**
 * One Sheet, three jobs: read-only detail view for an existing customer,
 * an edit form for that same customer, and the create form when `customer`
 * is null. Mutations live here (not in the page) so the page only tracks
 * which customer, if any, the sheet is open for.
 */
export function CustomerSheet({ open, onClose, customer }: CustomerSheetProps) {
  const isCreate = customer === null
  const queryClient = useQueryClient()

  const [mode, setMode] = useState<'view' | 'edit'>(isCreate ? 'edit' : 'view')
  const [viewedCustomer, setViewedCustomer] = useState<Customer | null>(customer)
  const [fallbackError, setFallbackError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setError,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CustomerFormValues>({
    resolver: zodResolver(customerFormSchema),
    defaultValues: { name: customer?.name ?? '', email: customer?.email ?? '' },
  })

  useEffect(() => {
    if (!open) return
    setViewedCustomer(customer)
    setMode(customer ? 'view' : 'edit')
    setFallbackError(null)
    reset({ name: customer?.name ?? '', email: customer?.email ?? '' })
  }, [open, customer, reset])

  const createMutation = useMutation({
    mutationFn: createCustomer,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customers'] })
      onClose()
    },
  })

  const updateMutation = useMutation({
    mutationFn: (values: CustomerFormValues) => updateCustomer(customer!.id, values),
    onSuccess: (updated) => {
      queryClient.invalidateQueries({ queryKey: ['customers'] })
      queryClient.invalidateQueries({ queryKey: ['customers', updated.id] })
      setViewedCustomer(updated)
      setMode('view')
    },
  })

  const onSubmit = async (values: CustomerFormValues) => {
    setFallbackError(null)
    try {
      if (isCreate) {
        await createMutation.mutateAsync(values)
      } else {
        await updateMutation.mutateAsync(values)
      }
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setFallbackError('Something went wrong. Please try again.')
        return
      }
      if (error.kind === 'validation' && error.fieldErrors) {
        const { mapped, unmapped } = splitFieldErrors(error.fieldErrors, ['name', 'email'] as const)
        for (const [field, message] of Object.entries(mapped)) {
          setError(field as 'name' | 'email', { message })
        }
        if (unmapped.length > 0) setFallbackError(unmapped.join(' '))
        return
      }
      if (error.kind === 'conflict') {
        setError('email', { message: error.message })
        return
      }
      setFallbackError(error.message)
    }
  }

  const handleCancelEdit = () => {
    if (isCreate) {
      onClose()
      return
    }
    reset({ name: viewedCustomer?.name ?? '', email: viewedCustomer?.email ?? '' })
    setFallbackError(null)
    setMode('view')
  }

  const title = isCreate ? 'New customer' : mode === 'edit' ? `Edit ${viewedCustomer?.name ?? ''}` : (viewedCustomer?.name ?? '')
  const description = isCreate ? 'Add a customer to your organization.' : (viewedCustomer?.email ?? undefined)

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      footer={
        mode === 'view' ? (
          <>
            <Button variant="secondary" onClick={onClose}>
              Close
            </Button>
            <Button onClick={() => setMode('edit')}>Edit</Button>
          </>
        ) : (
          <>
            <Button type="button" variant="secondary" onClick={handleCancelEdit}>
              Cancel
            </Button>
            <Button type="submit" form="customer-form" disabled={isSubmitting}>
              {isSubmitting ? 'Saving…' : isCreate ? 'Create customer' : 'Save changes'}
            </Button>
          </>
        )
      }
    >
      {mode === 'view' && viewedCustomer && (
        <dl className="flex flex-col gap-4">
          <div className="flex flex-col gap-1">
            <dt className="text-xs font-medium text-muted-foreground">Email</dt>
            <dd className="text-sm text-foreground">{viewedCustomer.email}</dd>
          </div>
          <div className="flex flex-col gap-1">
            <dt className="text-xs font-medium text-muted-foreground">Balance</dt>
            <dd className="font-mono text-sm tabular-nums text-foreground">
              {formatBalanceCents(viewedCustomer.balanceCents)}
            </dd>
          </div>
          <div className="flex flex-col gap-1">
            <dt className="text-xs font-medium text-muted-foreground">Status</dt>
            <dd>
              <StatusBadge tone={viewedCustomer.delinquentFlag ? 'destructive' : 'success'}>
                {viewedCustomer.delinquentFlag ? 'Delinquent' : 'Good standing'}
              </StatusBadge>
            </dd>
          </div>
          <div className="flex flex-col gap-1">
            <dt className="text-xs font-medium text-muted-foreground">Customer since</dt>
            <dd className="text-sm text-foreground">{dateFormatter.format(new Date(viewedCustomer.createdAt))}</dd>
          </div>
        </dl>
      )}

      {mode === 'edit' && (
        <form id="customer-form" onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4" noValidate>
          {fallbackError && (
            <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
              {fallbackError}
            </p>
          )}
          <FormField id="customer-name" label="Name" error={errors.name?.message}>
            <Input autoComplete="name" {...register('name')} />
          </FormField>
          <FormField id="customer-email" label="Email" error={errors.email?.message}>
            <Input type="email" autoComplete="email" {...register('email')} />
          </FormField>
        </form>
      )}
    </Sheet>
  )
}
