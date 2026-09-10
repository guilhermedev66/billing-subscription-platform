import { useEffect, useState } from 'react'

/** Ticks at `intervalMs` so relative-time displays (e.g. "in 3 days") don't go stale while a page stays open. */
export function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(id)
  }, [intervalMs])

  return now
}
