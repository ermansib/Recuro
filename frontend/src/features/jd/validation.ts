import type { JobDescription } from '../../domain/types'

/** Client-side mirror of the server checks (RCU-JD-001/002/003). Returns an i18n key or null. */
export function validateJd(jd: JobDescription): string | null {
  if (!jd.purpose.trim()) return 'jd.errors.purpose'
  if (jd.responsibilities.filter((r) => r.trim()).length < 2) return 'jd.errors.responsibilities'
  if (jd.competencies.length < 3) return 'jd.errors.competencies'
  if (jd.assessments.length < 1) return 'jd.errors.assessments'
  return null
}
