import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../api/client'
import { useApiMutation, useOffers, useRules } from '../../api/hooks'
import { can } from '../../auth/permissions'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Card, Chip, ErrorBox, Loading, PageHead, Sensitive } from '../../components/ui'
import { offerRouting } from '../../domain/rules'
import type { ChipTone, Offer, OfferState, RuleConfig } from '../../domain/types'
import { formatDate } from '../../utils/format'

const STATE_TONE: Record<OfferState, ChipTone> = {
  Draft: 'slate', PendingApproval: 'amber', Approved: 'green', Sent: 'gold', Accepted: 'green', Declined: 'red', Withdrawn: 'red', Expired: 'slate',
}

export function OfferPage() {
  const offers = useOffers()
  const rules = useRules()
  const [selected, setSelected] = useState('off-0388')
  if (offers.isPending || rules.isPending) return <Loading />
  if (offers.isError) return <ErrorBox error={offers.error} />
  if (rules.isError) return <ErrorBox error={rules.error} />
  const offer = offers.data.find((o) => o.id === selected) ?? offers.data[0]
  if (!offer) return <ErrorBox error={new Error('No offers')} />
  return <OfferDesk key={offer.id} offer={offer} offers={offers.data} rules={rules.data} onSelect={setSelected} />
}

function OfferDesk({ offer, offers, rules, onSelect }: { offer: Offer; offers: Offer[]; rules: RuleConfig; onSelect: (id: string) => void }) {
  const { t } = useTranslation()
  const { user, actor } = useSession()
  const toast = useToast()
  const onError = useErrorToast()
  // Local edits; saved on blur. The desk remounts per offer (key), so no resync is needed.
  const [comp, setComp] = useState(offer.components)

  const save = useApiMutation((c: Offer['components']) => api.updateOfferComponents(actor, offer.id, c))
  const submit = useApiMutation(() => api.submitOffer(actor, offer.id))
  const approve = useApiMutation(() => api.approveOffer(actor, offer.id))
  const send = useApiMutation(() => api.sendOffer(actor, offer.id))
  const outcome = useApiMutation((o: 'Accepted' | 'Declined') => api.setOfferOutcome(actor, offer.id, o))

  const routing = offerRouting(rules, offer.grade, comp, offer.band)
  const editable = can(user.role, 'offer.edit') && (offer.state === 'Draft' || offer.state === 'PendingApproval')
  const approverName = t(`common.roles.${routing.approverRole}`)
  const isApprover = user.role === routing.approverRole && offer.state === 'PendingApproval'
  const scaleMin = offer.band.min - 3
  const scaleMax = offer.band.max + 3
  const markLeft = Math.max(0, Math.min(100, ((routing.total - scaleMin) / (scaleMax - scaleMin)) * 100))
  const bandLeft = ((offer.band.min - scaleMin) / (scaleMax - scaleMin)) * 100
  const bandWidth = ((offer.band.max - offer.band.min) / (scaleMax - scaleMin)) * 100
  const fmt = (n: number) => n.toFixed(1)

  const persist = () => {
    if (comp.fixed === offer.components.fixed && comp.variable === offer.components.variable && comp.benefits === offer.components.benefits) return
    save.mutate(comp, { onError })
  }
  const setPart = (k: keyof Offer['components'], v: string) => setComp((c) => ({ ...c, [k]: Number(v) || 0 }))

  return (
    <div className="screen">
      <PageHead title={t('offer.title')} policy={t('offer.policy')} crumb={t('offer.crumb', { reqId: offer.reqId, name: offer.candidateName })}>
        <label className="sr-only" htmlFor="offer-select">{t('offer.select')}</label>
        <select id="offer-select" className="btn btn-ghost" value={offer.id} onChange={(e) => onSelect(e.target.value)}>
          {offers.map((o) => <option key={o.id} value={o.id}>{o.candidateName} · {o.designation}</option>)}
        </select>
        {!can(user.role, 'offer.edit') && <Chip tone="slate">{t('offer.readOnly')}</Chip>}
        <Chip tone={STATE_TONE[offer.state]}>{t(`offer.state.${offer.state}`, { approver: approverName })}</Chip>
      </PageHead>
      <div className="offer-grid">
        <Card title={t('offer.ctcTitle')} aside={<Chip tone="navy">{t('offer.gradeBand', { grade: offer.grade, min: offer.band.min, max: offer.band.max })}</Chip>}>
          <div style={{ padding: '18px 20px' }}>
            <fieldset disabled={!editable} style={{ border: 'none' }}>
              <div className="ctc-inputs">
                {(['fixed', 'variable', 'benefits'] as const).map((k) => (
                  <div key={k} className="field">
                    <label htmlFor={`ctc-${k}`}>{t(`offer.${k}`)}</label>
                    {can(user.role, 'candidate.viewSensitive') ? (
                      <input id={`ctc-${k}`} type="number" step={0.1} min={0} value={comp[k]} onChange={(e) => setPart(k, e.target.value)} onBlur={persist} />
                    ) : (
                      <input id={`ctc-${k}`} type="text" value="🔒" readOnly />
                    )}
                  </div>
                ))}
              </div>
            </fieldset>
            <div className="total-strip">
              <div>
                <span style={{ fontSize: 11, color: 'var(--muted)', fontWeight: 700 }}>{t('offer.total')}</span>
                <div className="big" aria-live="polite"><Sensitive mask="₹ •• L 🔒">₹{fmt(routing.total)}L</Sensitive></div>
              </div>
              <Chip tone={routing.withinBand ? 'green' : 'red'}>
                {routing.withinBand ? t('offer.within') : t('offer.deviation', { amount: fmt(routing.deviation) })}
              </Chip>
              <span style={{ fontSize: 11, color: 'var(--muted)' }}>{t('offer.visibility')}</span>
            </div>
            <div className="band-track" role="img" aria-label={t('offer.band', { min: offer.band.min, max: offer.band.max, total: fmt(routing.total) })}>
              <div className="band-seg" style={{ left: `${bandLeft}%`, width: `${bandWidth}%` }} />
              <div className={`band-mark ${routing.withinBand ? '' : 'dev'}`} style={{ left: `${markLeft}%` }} />
            </div>
            <div className="band-scale">
              {[scaleMin, offer.band.min, (offer.band.min + offer.band.max) / 2, offer.band.max, scaleMax].map((n) => <span key={n}>₹{n}L</span>)}
            </div>
            <div className="route-line">
              <b>{t('offer.routing')}</b>
              <Chip tone={routing.withinBand ? 'green' : 'red'}>{routing.label}</Chip>
            </div>
            {editable && <div style={{ marginTop: 12, fontSize: 11.5, color: 'var(--muted)' }}>{t('offer.tryIt')}</div>}
          </div>
        </Card>
        <Card title={t('offer.letterTitle')} aside={<Chip tone="slate">{t('offer.letterVersion', { version: offer.letterVersion })}</Chip>}>
          <div style={{ padding: '16px 20px' }}>
            <div className="fact"><span>{t('offer.facts.designation')}</span><b>{offer.designation}</b></div>
            <div className="fact"><span>{t('offer.facts.grade')}</span><b>{offer.grade}</b></div>
            <div className="fact"><span>{t('offer.facts.location')}</span><b>{offer.location}</b></div>
            <div className="fact"><span>{t('offer.facts.manager')}</span><b>{offer.reportingManager}</b></div>
            <div className="fact"><span>{t('offer.facts.joining')}</span><b>{formatDate(offer.joiningDate)}</b></div>
            <div className="fact"><span>{t('offer.facts.probation')}</span><b>{t('offer.facts.probationMonths', { count: offer.probationMonths })}</b></div>
            <div className="fact">
              <span>{t('offer.facts.breakup')}</span>
              <b>
                <Sensitive>{t('offer.facts.breakupValue', { total: fmt(routing.total), fixed: comp.fixed, variable: comp.variable, benefits: comp.benefits })}</Sensitive>
              </b>
            </div>
            <div className="fact"><span>{t('offer.facts.bgvClause')}</span><Chip tone="green">{t('offer.facts.included')}</Chip></div>
            <div className="fact"><span>{t('offer.facts.coi')}</span><Chip tone="green">{t('offer.facts.attached')}</Chip></div>
            <div style={{ display: 'flex', gap: 9, flexWrap: 'wrap', marginTop: 16 }}>
              {can(user.role, 'offer.edit') && offer.state === 'Draft' && (
                <button type="button" className="btn btn-primary" disabled={submit.isPending} onClick={() => submit.mutate(undefined, { onSuccess: () => toast(t('offer.sentForApproval', { label: routing.label }), 'move'), onError })}>
                  {t('offer.sendForApproval')}
                </button>
              )}
              {can(user.role, 'offer.release') && offer.state === 'Approved' && (
                <button type="button" className="btn btn-primary" disabled={send.isPending} onClick={() => send.mutate(undefined, { onSuccess: () => toast(t('offer.released'), 'move'), onError })}>
                  {t('offer.release')}
                </button>
              )}
              {can(user.role, 'offer.edit') && offer.state === 'Sent' && (
                <>
                  <button type="button" className="btn btn-primary" onClick={() => outcome.mutate('Accepted', { onSuccess: () => toast(t('offer.outcomeToast', { outcome: t('offer.accepted') }), 'move'), onError })}>{t('offer.accepted')}</button>
                  <button type="button" className="btn btn-danger" onClick={() => outcome.mutate('Declined', { onSuccess: () => toast(t('offer.outcomeToast', { outcome: t('offer.declined') }), 'warn'), onError })}>{t('offer.declined')}</button>
                </>
              )}
              <button type="button" className="btn btn-ghost" onClick={() => toast(t('offer.pdfToast'))}>{t('offer.pdf')}</button>
              {can(user.role, 'offer.edit') && <button type="button" className="btn btn-ghost" onClick={() => toast(t('offer.verbalToast'))}>{t('offer.verbal')}</button>}
            </div>
          </div>
        </Card>
      </div>
      <Card
        style={{ marginTop: 18 }}
        title={t('offer.trail')}
        aside={
          <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
            <span style={{ fontSize: 11.5, color: 'var(--muted)' }}>
              {offer.state === 'PendingApproval' && (isApprover ? t('offer.finalAuthority') : t('offer.awaiting', { approver: approverName, you: t(`common.roles.${user.role}`) }))}
            </span>
            <button
              type="button"
              className="btn btn-gold"
              disabled={!isApprover || approve.isPending}
              style={offer.state === 'Approved' ? { background: 'var(--green)' } : undefined}
              onClick={() => approve.mutate(undefined, { onSuccess: () => toast(t('offer.approvedToast'), 'move'), onError })}
            >
              {offer.state === 'Approved' ? t('offer.approved') : isApprover ? t('offer.approveAmount', { total: fmt(routing.total) }) : t('offer.approve')}
            </button>
          </div>
        }
      >
        <div className="trail">
          {offer.trail.map((e, i) => (
            <div key={i} className={`tl-entry ${e.approved ? 'approved' : ''}`}>
              <span className="te-dot" />
              <div>
                <b>{e.title}</b>
                <span>{e.detail}</span>
              </div>
            </div>
          ))}
        </div>
      </Card>
    </div>
  )
}
