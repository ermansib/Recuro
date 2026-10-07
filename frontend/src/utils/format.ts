import { tenant } from '../config/tenant'

/** Lakh amounts, as used across Indian CTC conventions in the prototype (e.g. ₹24.8L). */
export function formatLakh(value: number): string {
  return `₹${value.toFixed(1)}L`
}

export function formatDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleDateString(tenant.locale, { day: '2-digit', month: 'short', year: 'numeric' })
}

export function formatTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleTimeString(tenant.locale, { hour: '2-digit', minute: '2-digit' })
}

/** "just now", "18m ago", "3h ago", "Yesterday", or a date. */
export function relativeTime(iso: string, now: Date = new Date()): string {
  const d = new Date(iso)
  const mins = Math.round((now.getTime() - d.getTime()) / 60000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.round(mins / 60)
  if (hrs < 24) return `${hrs}h ago`
  if (hrs < 48) return 'Yesterday'
  return formatDate(iso)
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .map((p) => p[0]?.toUpperCase() ?? '')
    .slice(0, 2)
    .join('')
}
