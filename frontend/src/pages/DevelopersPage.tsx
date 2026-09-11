import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, RadioTower, SendHorizontal, Webhook } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { ErrorState } from '@/components/ui/ErrorState'
import { CardSkeletonGrid } from '@/components/ui/Skeleton'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { CreateEndpointSheet } from '@/features/webhooks/CreateEndpointSheet'
import { dispatchPendingDeliveries, listWebhookEndpoints, listWebhookEvents } from '@/features/webhooks/api'
import { EventTimeline } from '@/features/webhooks/EventTimeline'
import { ApiError } from '@/lib/api/client'

const dateFormatter = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' })

export function DevelopersPage() {
  const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false)

  const {
    data: endpoints,
    isPending: endpointsPending,
    isError: endpointsError,
    refetch: refetchEndpoints,
  } = useQuery({ queryKey: ['webhooks', 'endpoints'], queryFn: listWebhookEndpoints })

  const {
    data: events,
    isPending: eventsPending,
    isError: eventsError,
    refetch: refetchEvents,
  } = useQuery({ queryKey: ['webhooks', 'events'], queryFn: listWebhookEvents })

  const dispatchMutation = useMutation({
    mutationFn: dispatchPendingDeliveries,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhooks', 'events'] })
    },
  })

  const isPending = endpointsPending || eventsPending
  const isError = endpointsError || eventsError
  const refetch = () => {
    refetchEndpoints()
    refetchEvents()
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="flex items-center gap-2 text-xl font-semibold tracking-tight text-foreground">
          <Webhook className="size-5 text-muted-foreground" aria-hidden="true" />
          Developers
        </h1>
      </div>

      {isPending && <CardSkeletonGrid count={4} label="Loading webhooks" />}

      {isError && <ErrorState message="Couldn't load webhooks." onRetry={refetch} />}

      {!isPending && !isError && endpoints && events && (
        <>
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between gap-4">
                <CardTitle>Endpoints</CardTitle>
                <Button size="sm" onClick={() => setCreateOpen(true)}>
                  <Plus className="size-4" aria-hidden="true" />
                  New endpoint
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              {endpoints.length === 0 ? (
                <EmptyState
                  icon={RadioTower}
                  title="No webhook endpoints yet"
                  description="Register a URL to receive signed event deliveries."
                  action={
                    <Button size="sm" onClick={() => setCreateOpen(true)}>
                      New endpoint
                    </Button>
                  }
                />
              ) : (
                <div className="overflow-x-auto rounded-lg border border-border">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-border bg-surface-muted">
                      <tr>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          URL
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Event types
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Status
                        </th>
                        <th scope="col" className="px-4 py-2 font-medium text-muted-foreground">
                          Created
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {endpoints.map((endpoint) => (
                        <tr key={endpoint.id} className="border-b border-border last:border-0">
                          <td className="max-w-xs truncate px-4 py-3 font-medium text-foreground" title={endpoint.url}>
                            {endpoint.url}
                          </td>
                          <td className="px-4 py-3">
                            <div className="flex flex-wrap gap-1">
                              {endpoint.eventTypes.map((eventType) => (
                                <span
                                  key={eventType}
                                  className="rounded-full border border-border bg-surface-muted px-2 py-0.5 text-xs text-muted-foreground"
                                >
                                  {eventType}
                                </span>
                              ))}
                            </div>
                          </td>
                          <td className="px-4 py-3">
                            <StatusBadge tone={endpoint.active ? 'success' : 'neutral'}>
                              {endpoint.active ? 'Active' : 'Inactive'}
                            </StatusBadge>
                          </td>
                          <td className="px-4 py-3 text-muted-foreground">{dateFormatter.format(new Date(endpoint.createdAt))}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <div className="flex items-center justify-between gap-4">
                <CardTitle>Event timeline</CardTitle>
                <Button
                  size="sm"
                  variant="secondary"
                  disabled={dispatchMutation.isPending}
                  onClick={() => dispatchMutation.mutate()}
                >
                  <SendHorizontal className="size-4" aria-hidden="true" />
                  {dispatchMutation.isPending ? 'Dispatching…' : 'Dispatch pending deliveries'}
                </Button>
              </div>
            </CardHeader>
            <CardContent className="flex flex-col gap-4">
              {dispatchMutation.isError && (
                <p role="alert" className="text-sm text-rose-600 dark:text-rose-400">
                  {dispatchMutation.error instanceof ApiError ? dispatchMutation.error.message : "Couldn't dispatch deliveries."}
                </p>
              )}
              {dispatchMutation.data && (
                <div role="status" aria-live="polite" className="rounded-md border border-border bg-surface-muted px-3 py-2 text-sm text-foreground">
                  {dispatchMutation.data.considered} considered · {dispatchMutation.data.delivered} delivered ·{' '}
                  {dispatchMutation.data.failed} failed · {dispatchMutation.data.skipped} skipped
                </div>
              )}

              {events.length === 0 ? (
                <EmptyState icon={Webhook} title="No events yet" description="Events will appear here as billing activity happens." />
              ) : (
                <EventTimeline events={events} />
              )}
            </CardContent>
          </Card>
        </>
      )}

      <CreateEndpointSheet open={createOpen} onClose={() => setCreateOpen(false)} />
    </div>
  )
}
