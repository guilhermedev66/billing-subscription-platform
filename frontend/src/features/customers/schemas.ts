import { z } from 'zod'

export const customerFormSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  email: z.string().min(1, 'Email is required').email('Enter a valid email address'),
})

export type CustomerFormValues = z.infer<typeof customerFormSchema>
