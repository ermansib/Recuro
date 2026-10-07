import { pipelineColumns } from '../../domain/stateMachines'
import type { ApplicationStage, PipelineCard } from '../../domain/types'

/** Rejected / held / withdrawn cards stay visible in the column they left (as in the prototype). */
export function boardColumn(card: PipelineCard): ApplicationStage {
  const { stage, stageHistory } = card.application
  if (pipelineColumns.includes(stage)) return stage
  if (stage === 'PreBoarding' || stage === 'Onboarded' || stage === 'Confirmed') return 'Offer'
  const last = [...stageHistory].reverse().find((h) => h.to === stage)
  return last?.from && pipelineColumns.includes(last.from) ? last.from : 'Sourced'
}
