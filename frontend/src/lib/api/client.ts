import { ApiError, apiErrorFromResponse, networkError } from './errors'

const API_BASE_URL = import.meta.env.VITE_API_URL ?? '/api'

let authToken: string | null = null

/** Called by the auth module on login/logout — the client never reads storage itself. */
export function setAuthToken(token: string | null): void {
  authToken = token
}

export interface ApiFetchOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
}

export async function apiFetch<T>(path: string, options: ApiFetchOptions = {}): Promise<T> {
  const { body, headers, ...rest } = options

  const init: RequestInit = {
    ...rest,
    headers: {
      Accept: 'application/json',
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  }

  let response: Response
  try {
    response = await fetch(`${API_BASE_URL}${path}`, init)
  } catch {
    throw networkError()
  }

  if (!response.ok) {
    throw await apiErrorFromResponse(response)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string, options?: ApiFetchOptions) => apiFetch<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: ApiFetchOptions) =>
    apiFetch<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: ApiFetchOptions) =>
    apiFetch<T>(path, { ...options, method: 'PUT', body }),
  delete: <T>(path: string, options?: ApiFetchOptions) => apiFetch<T>(path, { ...options, method: 'DELETE' }),
}

export { ApiError }
export type { ApiErrorKind, ProblemDetails } from './errors'
