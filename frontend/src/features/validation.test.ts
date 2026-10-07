import type { JobPosting, PublicApplicationInput, RequisitionInput } from '../domain/types'
import { filterPostings, validateApplication } from './careers/validation'
import { validateMrf } from './mrf/validation'

describe('MRF wizard validation', () => {
  const empty: RequisitionInput = {
    department: '', designation: '', grade: '', location: '', positions: 1, reportingManager: '', employmentType: 'Permanent',
    nature: 'Replacement', replacementReason: '', joiningDate: '', band: '', outOfBudget: true, oobJustification: '', qualifications: '', sourcingChannels: [],
  }
  it('flags replacement reason and out-of-budget justification', () => {
    const r = validateMrf(empty)
    expect(r[1]).toContain('replacementReason')
    expect(r[2]).toContain('oobJustification')
  })
})

describe('careers', () => {
  const input: PublicApplicationInput = {
    postingId: 'p', name: 'A', email: 'a@e.x', phone: '1', experienceYears: 1, currentCtc: null, expectedCtc: null, noticeDays: null,
    resumeFileName: 'cv.pdf', privacyConsent: true, coiDeclaration: false,
  }
  it('requires the COI declaration', () => {
    expect(validateApplication(input)).toBe('careers.errors.coi')
    expect(validateApplication({ ...input, coiDeclaration: true })).toBeNull()
  })
  it('filters postings by text and location', () => {
    const postings: JobPosting[] = [
      { id: '1', reqId: 'r', title: 'Credit Analyst', location: 'Mumbai', locationFilter: 'mumbai', experience: '', qualification: '', tags: [] },
      { id: '2', reqId: 'r', title: 'Branch Manager', location: 'Pune', locationFilter: 'pune', experience: '', qualification: '', tags: [] },
    ]
    expect(filterPostings(postings, 'credit', '').map((p) => p.id)).toEqual(['1'])
    expect(filterPostings(postings, '', 'pune').map((p) => p.id)).toEqual(['2'])
  })
})
