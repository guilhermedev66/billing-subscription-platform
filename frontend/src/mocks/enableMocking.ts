/**
 * Starts the MSW worker so auth (and later, other modules) work end-to-end
 * before Codex — Backend's API exists. Set VITE_API_MOCKING=disabled once a
 * real API is wired up for local dev against it.
 */
export async function enableMocking(): Promise<void> {
  if (!import.meta.env.DEV) return
  if (import.meta.env.VITE_API_MOCKING === 'disabled') return

  const { worker } = await import('./browser')
  await worker.start({ onUnhandledRequest: 'bypass' })
}
