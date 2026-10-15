// In-browser stand-in for the Employee portal API, used for sessions that aren't backed by the server
// (the demo workspace and tests). Internal jobs come from the mock careers postings. It mirrors the
// service's rules: the COI declaration is mandatory (400) and a referral needs a real opening.
import type { InternalJob, JobPosting, MyReferral } from '../../domain/types'
import { ApiError, type EmployeeApi } from '../contract'

const DAY_MS = 86_400_000
const IJP_WINDOW_DAYS = 5

export function createMockEmployeeApi(postings: () => Promise<JobPosting[]>, now: () => Date = () => new Date()): EmployeeApi {
  const referrals: MyReferral[] = []

  async function jobs(): Promise<InternalJob[]> {
    const opens = now()
    return (await postings()).map((p) => ({
      reqId: p.reqId,
      title: p.title,
      location: p.location,
      department: '',
      grade: '',
      eligibleGrades: [],
      minTenureMonths: 0,
      summary: p.qualification,
      opensAt: opens.toISOString(),
      closesAt: new Date(opens.getTime() + IJP_WINDOW_DAYS * DAY_MS).toISOString(),
    }))
  }

  return {
    listInternalJobs: jobs,
    listMyReferrals: async () => [...referrals].reverse(),
    async submitReferral(_token, input) {
      if (!input.coiAccepted) throw new ApiError(400, 'Accept the conflict-of-interest declaration to refer someone (FRD §7.1).')
      if (!input.name.trim() || !input.email.trim() || !input.relationship.trim()) throw new ApiError(400, 'Name, email and your relationship to them are required.')
      const job = (await jobs()).find((j) => j.reqId === input.reqId)
      if (!job) throw new ApiError(404, 'That opening isn’t open for referrals.')
      const referral: MyReferral = {
        id: `ref-${referrals.length + 1}`,
        reqId: job.reqId,
        candidateName: input.name.trim(),
        relationship: input.relationship.trim(),
        bonusEligible: true,
        appId: null,
        status: 'Submitted',
        progress: 'Received',
        submittedAt: now().toISOString(),
      }
      referrals.push(referral)
      return referral
    },
  }
}
