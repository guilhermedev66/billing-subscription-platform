import { z } from 'zod'

export const createEndpointFormSchema = z.object({
  url: z.string().min(1, 'URL is required').url('Enter a valid https:// URL'),
  secret: z
    .string()
    .min(32, 'Secret must be at least 32 characters')
    .max(256, 'Secret must be at most 256 characters'),
  eventTypes: z.array(z.string()).min(1, 'Select at least one event type'),
})

export type CreateEndpointFormValues = z.infer<typeof createEndpointFormSchema>
