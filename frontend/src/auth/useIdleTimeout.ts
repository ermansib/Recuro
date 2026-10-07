import { useEffect, useRef } from 'react'

const ACTIVITY_EVENTS = ['pointerdown', 'keydown', 'wheel', 'touchstart'] as const
const CHECK_EVERY_MS = 15_000
const MINUTE_MS = 60_000

/** Calls `onIdle` once nobody has used the page for `minutes` (NFR-01). Pass null to disable. */
export function useIdleTimeout(minutes: number | null, onIdle: () => void): void {
  const lastActivity = useRef(0)
  const callback = useRef(onIdle)

  useEffect(() => {
    callback.current = onIdle
  }, [onIdle])

  useEffect(() => {
    if (minutes === null) return
    lastActivity.current = Date.now()
    const touch = () => {
      lastActivity.current = Date.now()
    }
    for (const e of ACTIVITY_EVENTS) window.addEventListener(e, touch, { passive: true })
    const timer = setInterval(() => {
      if (Date.now() - lastActivity.current >= minutes * MINUTE_MS) callback.current()
    }, CHECK_EVERY_MS)
    return () => {
      for (const e of ACTIVITY_EVENTS) window.removeEventListener(e, touch)
      clearInterval(timer)
    }
  }, [minutes])
}
