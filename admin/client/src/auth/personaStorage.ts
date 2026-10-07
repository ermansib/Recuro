import type { Persona } from '../api/types'

const STORAGE_KEY = 'recuro-admin.persona'

/** Persona is kept per browser tab, so signing out of one tab does not affect another. */
export function readPersona(): Persona | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Persona) : null
  } catch {
    return null
  }
}

export function writePersona(persona: Persona | null): void {
  try {
    if (persona) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(persona))
    else sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // Storage can be unavailable (private mode); the session then lasts until reload.
  }
}

export type AdminLevel = 'platform' | 'tenant'

export function levelOf(persona: Persona): AdminLevel {
  return persona.id === 'platform' ? 'platform' : 'tenant'
}
