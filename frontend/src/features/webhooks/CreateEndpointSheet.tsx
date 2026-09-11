import { useEffect, useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Check, Copy } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { FormField } from '@/components/ui/FormField'
import { Input } from '@/components/ui/Input'
import { Sheet } from '@/components/ui/Sheet'
import { ApiError, splitFieldErrors } from '@/lib/api/client'
import { createWebhookEndpoint } from './api'
import { KNOWN_WEBHOOK_EVENT_TYPES } from './eventTypes'
import { createEndpointFormSchema, type CreateEndpointFormValues } from './schemas'
import type { WebhookEndpoint } from './types'

interface CreateEndpointSheetProps {
  open: boolean
  onClose: () => void
}

function generateSecret(): string {
  return `whsec_${crypto.randomUUID().replace(/-/g, '')}`
}

/**
 * The real backend only ever returns an endpoint's secret in the create
 * response (see features/webhooks/api.ts) — there's no reveal-secret
 * endpoint. So this sheet, unlike CustomerSheet, does NOT auto-close on
 * success: it shows the secret once in a copyable callout and requires an
 * explicit "Done" to dismiss, so a fast auto-close can't silently lose it.
 */
export function CreateEndpointSheet({ open, onClose }: CreateEndpointSheetProps) {
  const queryClient = useQueryClient()
  const [fallbackError, setFallbackError] = useState<string | null>(null)
  const [created, setCreated] = useState<WebhookEndpoint | null>(null)
  const [copied, setCopied] = useState(false)
  const [copyError, setCopyError] = useState(false)

  const {
    register,
    handleSubmit,
    setValue,
    setError,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateEndpointFormValues>({
    resolver: zodResolver(createEndpointFormSchema),
    defaultValues: { url: '', secret: '', eventTypes: [] },
  })

  useEffect(() => {
    if (!open) return
    setFallbackError(null)
    setCreated(null)
    setCopied(false)
    setCopyError(false)
    reset({ url: '', secret: '', eventTypes: [] })
  }, [open, reset])

  const createMutation = useMutation({
    mutationFn: createWebhookEndpoint,
    onSuccess: (endpoint) => {
      queryClient.invalidateQueries({ queryKey: ['webhooks', 'endpoints'] })
      setCreated(endpoint)
    },
  })

  const onSubmit = async (values: CreateEndpointFormValues) => {
    setFallbackError(null)
    try {
      await createMutation.mutateAsync(values)
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setFallbackError('Something went wrong. Please try again.')
        return
      }
      if (error.kind === 'validation' && error.fieldErrors) {
        const { mapped, unmapped } = splitFieldErrors(error.fieldErrors, ['url', 'secret', 'eventTypes'] as const)
        for (const [field, message] of Object.entries(mapped)) {
          setError(field as 'url' | 'secret' | 'eventTypes', { message })
        }
        if (unmapped.length > 0) setFallbackError(unmapped.join(' '))
        return
      }
      setFallbackError(error.message)
    }
  }

  const handleCopy = async () => {
    if (!created?.secret) return
    try {
      await navigator.clipboard.writeText(created.secret)
      setCopied(true)
      setCopyError(false)
    } catch {
      setCopied(false)
      setCopyError(true)
    }
  }

  const handleDone = () => {
    onClose()
  }

  return (
    <Sheet
      open={open}
      onClose={created ? handleDone : onClose}
      title="New webhook endpoint"
      description={created ? undefined : 'Register a URL to receive signed event deliveries.'}
      footer={
        created ? (
          <Button onClick={handleDone}>Done</Button>
        ) : (
          <>
            <Button type="button" variant="secondary" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" form="create-endpoint-form" disabled={isSubmitting}>
              {isSubmitting ? 'Creating…' : 'Create endpoint'}
            </Button>
          </>
        )
      }
    >
      {created ? (
        <div className="flex flex-col gap-4">
          <p className="text-sm text-foreground">
            <span className="font-medium">{created.url}</span> is registered and active.
          </p>
          <div
            role="status"
            aria-live="polite"
            className="flex flex-col gap-2 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 dark:border-amber-800 dark:bg-amber-950/40"
          >
            <p className="text-sm font-medium text-amber-800 dark:text-amber-300">
              Copy this secret now — it won't be shown again.
            </p>
            <div className="flex items-center gap-2">
              <code className="flex-1 overflow-x-auto rounded-md border border-amber-300 bg-surface px-2 py-1.5 text-xs text-foreground dark:border-amber-800">
                {created.secret}
              </code>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={handleCopy}
                aria-label={copied ? 'Secret copied to clipboard' : 'Copy webhook secret'}
              >
                {copied ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}
              </Button>
            </div>
            {copyError && (
              <p role="alert" className="text-xs text-rose-600 dark:text-rose-400">
                Couldn't copy automatically — select and copy the secret above manually.
              </p>
            )}
          </div>
        </div>
      ) : (
        <form id="create-endpoint-form" onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4" noValidate>
          {fallbackError && (
            <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
              {fallbackError}
            </p>
          )}
          <FormField id="endpoint-url" label="URL" error={errors.url?.message}>
            <Input type="url" placeholder="https://example.com/webhooks" autoComplete="off" {...register('url')} />
          </FormField>
          <div className="flex items-end gap-2">
            {/*
              FormField clones `id`/aria-* onto its DIRECT child, so that child must be the
              real form control — wrapping the Input in a div here (to sit it beside the
              Generate button) previously gave both the div AND the Input the same
              "endpoint-secret" id, leaving the label's htmlFor pointing at a non-labelable
              wrapper for screen readers. Keeping Input as FormField's only child and moving
              the button outside fixes the id collision.
            */}
            <FormField id="endpoint-secret" label="Signing secret" error={errors.secret?.message} className="flex-1">
              <Input autoComplete="off" {...register('secret')} />
            </FormField>
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => setValue('secret', generateSecret(), { shouldValidate: true })}
            >
              Generate
            </Button>
          </div>
          <fieldset className="flex flex-col gap-1.5">
            <legend className="text-sm font-medium text-foreground">Event types</legend>
            <div className="flex flex-col gap-2 rounded-md border border-border p-3">
              {KNOWN_WEBHOOK_EVENT_TYPES.map((eventType) => (
                <label key={eventType} className="flex items-center gap-2 text-sm text-foreground">
                  <input type="checkbox" value={eventType} className="size-4 rounded border-border" {...register('eventTypes')} />
                  {eventType}
                </label>
              ))}
            </div>
            {errors.eventTypes && (
              <p role="alert" className="text-xs text-rose-600 dark:text-rose-400">
                {errors.eventTypes.message}
              </p>
            )}
          </fieldset>
        </form>
      )}
    </Sheet>
  )
}
