import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../api/client'
import { useApiMutation, useInvitations, useTeam } from '../../api/hooks'
import { useSession } from '../../auth/sessionContext'
import { useErrorToast, useToast } from '../../components/toastContext'
import { Card, Chip, ErrorBox, Loading, PageHead } from '../../components/ui'
import { INVITABLE_ROLES, isValidEmail } from '../../domain/auth'
import type { ChipTone, Invitation, InviteInput } from '../../domain/types'
import { formatDate } from '../../utils/format'
import { AuthField } from '../auth/fields'

const STATUS_TONE: Record<Invitation['status'], ChipTone> = { Pending: 'amber', Accepted: 'green', Expired: 'slate' }

const emptyInvite = (): InviteInput => ({ name: '', email: '', role: 'hrta' })

/** Workspace administrators invite colleagues; nobody self-registers as staff. */
export function TeamPage() {
  const { t } = useTranslation()
  const { tenant, token } = useSession()
  const team = useTeam()
  const invitations = useInvitations()
  const toast = useToast()
  const onError = useErrorToast()
  const [form, setForm] = useState(emptyInvite)
  const [errors, setErrors] = useState<{ name?: string; email?: string }>({})
  const invite = useApiMutation((input: InviteInput) => api.inviteStaff(token, input))

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const next = {
      name: form.name.trim() ? undefined : t('auth.errors.name'),
      email: isValidEmail(form.email) ? undefined : t('auth.errors.email'),
    }
    setErrors(next)
    if (next.name || next.email) return
    invite.mutate(form, {
      onSuccess: (sent) => {
        toast(t('auth.team.inviteSent', { email: sent.email }), 'move')
        setForm(emptyInvite())
      },
      onError,
    })
  }

  const copy = (path: string) => {
    void navigator.clipboard?.writeText(`${window.location.origin}${path}`).then(() => toast(t('auth.team.copied')))
  }

  return (
    <div className="screen">
      <PageHead title={t('auth.team.title')} crumb={t('auth.team.crumb', { workspace: tenant.name })} />
      <div className="team-grid">
        <Card title={t('auth.team.inviteTitle')}>
          <form className="team-invite" onSubmit={submit} noValidate>
            <AuthField label={t('auth.fields.name')} value={form.name} onChange={(v) => setForm((f) => ({ ...f, name: v }))} error={errors.name} />
            <AuthField label={t('auth.fields.workEmail')} type="email" value={form.email} onChange={(v) => setForm((f) => ({ ...f, email: v }))} error={errors.email} />
            <div className="field">
              <label htmlFor="invite-role">{t('auth.fields.role')}</label>
              <select id="invite-role" value={form.role} onChange={(e) => setForm((f) => ({ ...f, role: e.target.value as InviteInput['role'] }))}>
                {INVITABLE_ROLES.map((r) => <option key={r} value={r}>{t(`common.roles.${r}`)}</option>)}
              </select>
            </div>
            <button type="submit" className="btn btn-primary" disabled={invite.isPending}>{t('auth.team.inviteSubmit')}</button>
          </form>
        </Card>
        <div className="col">
          <Card title={t('auth.team.members')}>
            {team.isPending ? <Loading /> : team.isError ? <ErrorBox error={team.error} /> : (
              <table className="mini">
                <thead>
                  <tr><th>{t('auth.team.cols.name')}</th><th>{t('auth.team.cols.email')}</th><th>{t('auth.team.cols.role')}</th></tr>
                </thead>
                <tbody>
                  {team.data.map((u) => (
                    <tr key={u.id}><td><b>{u.name}</b></td><td>{u.email}</td><td>{t(`common.roles.${u.role}`)}</td></tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>
          <Card title={t('auth.team.invitations')}>
            {invitations.isPending ? <Loading /> : invitations.isError ? <ErrorBox error={invitations.error} /> : invitations.data.length === 0 ? (
              <div className="empty">{t('auth.team.noInvitations')}</div>
            ) : (
              <table className="mini">
                <thead>
                  <tr>
                    <th>{t('auth.team.cols.name')}</th><th>{t('auth.team.cols.role')}</th><th>{t('auth.team.cols.status')}</th><th>{t('auth.team.cols.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {invitations.data.map((i) => (
                    <tr key={i.id}>
                      <td><b>{i.name}</b><div className="subnote">{i.email}</div></td>
                      <td>{t(`common.roles.${i.role}`)}</td>
                      <td>
                        <Chip tone={STATUS_TONE[i.status]}>{t(`auth.team.status.${i.status}`)}</Chip>
                        {i.status === 'Pending' && <div className="subnote">{t('auth.team.expires', { date: formatDate(i.expiresAt) })}</div>}
                      </td>
                      <td>
                        {i.status === 'Pending' && (
                          <button type="button" className="btn btn-ghost btn-sm" onClick={() => copy(i.acceptPath)}>{t('auth.team.copyLink')}</button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>
        </div>
      </div>
    </div>
  )
}
