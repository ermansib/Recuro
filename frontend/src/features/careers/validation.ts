import type { JobPosting, PublicApplicationInput } from '../../domain/types'

/** Client-side mirror of RCU-CAR-002 checks. Returns an i18n key or null. */
export function validateApplication(v: PublicApplicationInput): string | null {
  if (!v.postingId) return 'careers.errors.position'
  if (!v.name.trim() || !v.email.trim() || !v.phone.trim()) return 'careers.errors.required'
  if (!v.resumeFileName) return 'careers.errors.resume'
  if (!v.privacyConsent) return 'careers.errors.consent'
  if (!v.coiDeclaration) return 'careers.errors.coi'
  return null
}

export function filterPostings(postings: JobPosting[], query: string, location: string): JobPosting[] {
  const q = query.trim().toLowerCase()
  return postings.filter(
    (p) =>
      (!location || p.locationFilter === location) &&
      (!q || [p.title, p.location, p.experience, p.qualification, ...p.tags.map((x) => x.text)].join(' ').toLowerCase().includes(q)),
  )
}
