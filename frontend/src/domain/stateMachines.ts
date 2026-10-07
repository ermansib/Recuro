// Legal transitions from FRD §6, encoded as data so the .NET services can share the same tables.
import type { ApplicationStage, BgvCheckStatus, OfferState, RequisitionState } from './types'

type TransitionMap<S extends string> = Readonly<Record<S, readonly S[]>>

export const requisitionTransitions: TransitionMap<RequisitionState> = {
  Draft: ['PendingApproval', 'Cancelled'],
  PendingApproval: ['Approved', 'Rejected', 'Draft'],
  Approved: ['Sourcing', 'OnHold', 'Cancelled'],
  Sourcing: ['Interviewing', 'OnHold', 'Cancelled'],
  Interviewing: ['Selection', 'Sourcing', 'OnHold', 'Cancelled'],
  Selection: ['BGV', 'Interviewing', 'OnHold', 'Cancelled'],
  BGV: ['Offer', 'Selection', 'OnHold', 'Cancelled'],
  Offer: ['Filled', 'Sourcing', 'OnHold', 'Cancelled'],
  Filled: [],
  Rejected: ['Draft'],
  OnHold: ['Approved', 'Sourcing', 'Interviewing', 'Selection', 'BGV', 'Offer', 'Cancelled'],
  Cancelled: [],
}

const terminalExits: ApplicationStage[] = ['Rejected', 'Withdrawn', 'Hold']

export const applicationTransitions: TransitionMap<ApplicationStage> = {
  Sourced: ['Screened', ...terminalExits],
  Screened: ['Interview', ...terminalExits],
  Interview: ['Selection', ...terminalExits],
  Selection: ['BGV', ...terminalExits],
  BGV: ['Offer', ...terminalExits],
  Offer: ['PreBoarding', ...terminalExits],
  PreBoarding: ['Onboarded', 'Withdrawn'],
  Onboarded: ['Confirmed'],
  Confirmed: [],
  Rejected: [],
  Withdrawn: [],
  Hold: ['Sourced', 'Screened', 'Interview', 'Selection', 'BGV', 'Offer', 'Rejected', 'Withdrawn'],
}

export const bgvCheckTransitions: TransitionMap<BgvCheckStatus> = {
  Pending: ['InProgress', 'NotApplicable'],
  InProgress: ['Cleared', 'Flagged'],
  Flagged: ['Cleared'],
  Cleared: [],
  NotApplicable: [],
}

export const offerTransitions: TransitionMap<OfferState> = {
  Draft: ['PendingApproval'],
  PendingApproval: ['Approved', 'Draft'],
  Approved: ['Sent', 'Withdrawn'],
  Sent: ['Accepted', 'Declined', 'Withdrawn', 'Expired'],
  Accepted: ['Withdrawn'],
  Declined: [],
  Withdrawn: [],
  Expired: [],
}

export function canTransition<S extends string>(map: TransitionMap<S>, from: S, to: S): boolean {
  return map[from].includes(to)
}

/** Kanban columns shown on the pipeline board (S-06), in order. */
export const pipelineColumns: ApplicationStage[] = ['Sourced', 'Screened', 'Interview', 'Selection', 'BGV', 'Offer']
