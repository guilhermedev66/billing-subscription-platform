/**
 * Normalizes ASP.NET Core's ProblemDetails/ValidationProblemDetails (RFC 7807)
 * into one shape components can branch on without ever touching a raw Response.
 */

export type ApiErrorKind =
  | 'validation'
  | 'conflict'
  | 'unauthorized'
  | 'forbidden'
  | 'not_found'
  | 'rate_limited'
  | 'network'
  | 'server'
  | 'unknown'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
  [key: string]: unknown
}

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number
  readonly fieldErrors?: Record<string, string[]>
  readonly problem?: ProblemDetails

  constructor(
    kind: ApiErrorKind,
    status: number,
    message: string,
    opts?: { fieldErrors?: Record<string, string[]>; problem?: ProblemDetails },
  ) {
    super(message)
    this.name = 'ApiError'
    this.kind = kind
    this.status = status
    this.fieldErrors = opts?.fieldErrors
    this.problem = opts?.problem
  }
}

function kindFromStatus(status: number): ApiErrorKind {
  switch (status) {
    case 400:
      return 'validation'
    case 401:
      return 'unauthorized'
    case 403:
      return 'forbidden'
    case 404:
      return 'not_found'
    case 409:
      return 'conflict'
    case 429:
      return 'rate_limited'
    default:
      return status >= 500 ? 'server' : 'unknown'
  }
}

function defaultMessageFor(kind: ApiErrorKind): string {
  switch (kind) {
    case 'validation':
      return 'The submitted data is invalid.'
    case 'unauthorized':
      return 'You need to sign in to continue.'
    case 'forbidden':
      return "You don't have permission to do that."
    case 'not_found':
      return "We couldn't find what you were looking for."
    case 'conflict':
      return 'This record changed since you loaded it — reload and try again.'
    case 'rate_limited':
      return 'Too many attempts — please wait and try again.'
    case 'server':
      return 'Something went wrong on our end.'
    case 'network':
      return 'Could not reach the server — check your connection and try again.'
    default:
      return 'Something went wrong.'
  }
}

/** ValidationProblemDetails keys are PascalCase; RHF/zod field names are camelCase. */
function normalizeFieldErrors(errors: ProblemDetails['errors']): Record<string, string[]> | undefined {
  if (!errors) return undefined
  const normalized: Record<string, string[]> = {}
  for (const [key, messages] of Object.entries(errors)) {
    const camelKey = key.charAt(0).toLowerCase() + key.slice(1)
    normalized[camelKey] = messages
  }
  return normalized
}

export async function apiErrorFromResponse(response: Response): Promise<ApiError> {
  const status = response.status
  const kind = kindFromStatus(status)

  let problem: ProblemDetails | undefined
  try {
    const text = await response.text()
    if (text) {
      const parsed = JSON.parse(text)
      if (parsed && typeof parsed === 'object') problem = parsed as ProblemDetails
    }
  } catch {
    // Body wasn't JSON (or was empty) — proceed with no parsed problem.
  }

  const message = problem?.detail ?? problem?.title ?? defaultMessageFor(kind)
  return new ApiError(kind, status, message, { fieldErrors: normalizeFieldErrors(problem?.errors), problem })
}

export function networkError(): ApiError {
  return new ApiError('network', 0, defaultMessageFor('network'))
}
