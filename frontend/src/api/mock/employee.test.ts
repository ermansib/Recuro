import { describe, expect, it } from 'vitest'
import type { JobPosting } from '../../domain/types'
import { createMockEmployeeApi } from './employee'

const postings = async () => [{ id: 'p1', reqId: 'REQ-1', title: 'Analyst', location: 'Pune', locationFilter: 'Pune', experience: '', qualification: 'MBA', tags: [] }] as JobPosting[]
const input = { reqId: 'REQ-1', name: 'Ravi', email: 'r@x.example', phone: '', experienceYears: null, relationship: 'Friend', coiAccepted: true }

describe('mock employee api', () => {
  it('lists openings and records referrals, newest first', async () => {
    const api = createMockEmployeeApi(postings)
    expect(await api.listInternalJobs('t')).toHaveLength(1)
    await api.submitReferral('t', input)
    await api.submitReferral('t', { ...input, name: 'Asha' })
    expect((await api.listMyReferrals('t')).map((r) => r.candidateName)).toEqual(['Asha', 'Ravi'])
  })

  it('refuses a referral without the COI declaration or for an unknown job', async () => {
    const api = createMockEmployeeApi(postings)
    await expect(api.submitReferral('t', { ...input, coiAccepted: false })).rejects.toMatchObject({ status: 400 })
    await expect(api.submitReferral('t', { ...input, reqId: 'nope' })).rejects.toMatchObject({ status: 404 })
  })
})
