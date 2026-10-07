import type { RequisitionInput } from '../../domain/types'

export type Field = keyof RequisitionInput

/** Returns invalid fields per wizard step (RCU-MRF-001/003/007). */
export function validateMrf(v: RequisitionInput): Record<1 | 2, Field[]> {
  const s1: Field[] = []
  const s2: Field[] = []
  if (!v.department) s1.push('department')
  if (!v.designation) s1.push('designation')
  if (!v.grade) s1.push('grade')
  if (!v.location) s1.push('location')
  if (!v.positions || v.positions < 1) s1.push('positions')
  if (!v.reportingManager) s1.push('reportingManager')
  if (v.nature !== 'NewPosition' && !v.replacementReason.trim()) s1.push('replacementReason')
  if (!v.joiningDate) s1.push('joiningDate')
  if (!v.band) s2.push('band')
  if (v.outOfBudget && !v.oobJustification.trim()) s2.push('oobJustification')
  if (!v.qualifications.trim()) s2.push('qualifications')
  return { 1: s1, 2: s2 }
}
