import { useQuery } from '@tanstack/react-query'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { Fragment, useState } from 'react'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { decodeEventPayload, eventDisplayStatus, listWebhookDeliveries } from './api'
import { deliveryAttemptTone, WEBHOOK_EVENT_STATUS_LABEL, WEBHOOK_EVENT_STATUS_TONE } from './webhookStatusTone'
import type { WebhookEvent } from './types'

const dateTimeFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })

interface EventTimelineProps {
  events: WebhookEvent[]
}

export function EventTimeline({ events }: EventTimelineProps) {
  const [expandedId, setExpandedId] = useState<string | null>(null)

  return (
    <div className="overflow-x-auto rounded-lg border border-border">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-border bg-surface-muted">
          <tr>
            <th scope="col" className="w-8 px-2 py-2">
              <span className="sr-only">Expand</span>
            </th>
            <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
              Event
            </th>
            <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
              Status
            </th>
            <th scope="col" className="px-4 py-2 text-right font-medium text-muted-foreground">
              Attempts
            </th>
            <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
              Occurred
            </th>
          </tr>
        </thead>
        <tbody>
          {events.map((event) => {
            const expanded = expandedId === event.id
            const status = eventDisplayStatus(event)

            return (
              <Fragment key={event.id}>
                <tr className="border-b border-border last:border-0 hover:bg-surface-muted">
                  <td className="px-2 py-3">
                    <button
                      type="button"
                      aria-expanded={expanded}
                      aria-label={`${expanded ? 'Collapse' : 'Expand'} delivery details for ${event.eventType} event`}
                      onClick={() => setExpandedId(expanded ? null : event.id)}
                      className="flex size-6 items-center justify-center rounded text-muted-foreground hover:bg-surface-muted"
                    >
                      {expanded ? <ChevronDown className="size-4" aria-hidden="true" /> : <ChevronRight className="size-4" aria-hidden="true" />}
                    </button>
                  </td>
                  <td className="px-4 py-3 font-medium text-foreground">{event.eventType}</td>
                  <td className="px-4 py-3">
                    <StatusBadge tone={WEBHOOK_EVENT_STATUS_TONE[status]}>{WEBHOOK_EVENT_STATUS_LABEL[status]}</StatusBadge>
                  </td>
                  <td className="px-4 py-3 text-right font-mono tabular-nums text-foreground">{event.deliveryAttemptCount}</td>
                  <td className="px-4 py-3 text-muted-foreground">{dateTimeFormatter.format(new Date(event.occurredAt))}</td>
                </tr>
                {expanded && <EventDetailRow event={event} />}
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function EventDetailRow({ event }: { event: WebhookEvent }) {
  const { data: deliveries, isPending, isError } = useQuery({
    queryKey: ['webhooks', 'events', event.id, 'deliveries'],
    queryFn: () => listWebhookDeliveries(event.id),
  })

  const payload = decodeEventPayload(event.rawBody)

  return (
    <tr className="border-b border-border bg-surface-muted last:border-0">
      <td />
      <td colSpan={4} className="px-4 py-4">
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <div className="flex flex-col gap-1">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Event payload</p>
            <pre className="max-h-64 overflow-auto rounded-md border border-border bg-surface p-3 font-mono text-xs text-foreground">
              {payload.text}
            </pre>
          </div>
          <div className="flex flex-col gap-1">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Delivery attempts</p>
            {isPending && (
              <p role="status" aria-live="polite" className="text-sm text-muted-foreground">
                Loading attempts…
              </p>
            )}
            {isError && (
              <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
                Couldn't load delivery attempts.
              </p>
            )}
            {!isPending && !isError && deliveries && deliveries.length === 0 && (
              <p className="text-sm text-muted-foreground">No delivery attempts yet.</p>
            )}
            {!isPending && !isError && deliveries && deliveries.length > 0 && (
              <ol className="flex flex-col gap-2">
                {deliveries.map((attempt) => (
                  <li key={attempt.id} className="rounded-md border border-border bg-surface p-2 text-xs">
                    <div className="flex items-center justify-between gap-2">
                      <StatusBadge tone={deliveryAttemptTone(attempt)}>
                        {attempt.statusCode ?? 'No response'}
                      </StatusBadge>
                      <span className="text-muted-foreground">{dateTimeFormatter.format(new Date(attempt.attemptedAt))}</span>
                    </div>
                    <p className="mt-1 text-muted-foreground">
                      Attempt {attempt.attemptNumber} · {attempt.durationMilliseconds}ms
                    </p>
                    {attempt.error && <p className="mt-1 text-rose-600 dark:text-rose-400">{attempt.error}</p>}
                    {attempt.responseBody && (
                      <pre className="mt-1 overflow-auto rounded bg-surface-muted p-1.5 font-mono text-[11px] text-foreground">
                        {attempt.responseBody}
                      </pre>
                    )}
                  </li>
                ))}
              </ol>
            )}
          </div>
        </div>
      </td>
    </tr>
  )
}
