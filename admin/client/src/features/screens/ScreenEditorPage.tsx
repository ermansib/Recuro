import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router-dom'
import { useScreen, useUpdateScreen } from '../../api/hooks'
import type { EffectiveScreen } from '../../api/types'
import { ErrorMessage, PageHeader, QueryState, StatusMessage } from '../../components/ui'
import { FieldTable } from './FieldTable'
import { defaultDraft, draftFrom, toRequest, type ScreenDraft } from './screenDraft'

const TEXT_MAX_LENGTH = 80

export function ScreenEditorPage() {
  const { key = '' } = useParams()
  const screen = useScreen(key)
  return (
    <QueryState isLoading={screen.isLoading} error={screen.error}>
      {/* Keyed by screen so the draft resets when another screen is opened. */}
      {screen.data && <ScreenEditor key={screen.data.key} screen={screen.data} />}
    </QueryState>
  )
}

function ScreenEditor({ screen }: { screen: EffectiveScreen }) {
  const { t } = useTranslation()
  const update = useUpdateScreen(screen.key)
  const [draft, setDraft] = useState<ScreenDraft>(() => draftFrom(screen))
  const [saved, setSaved] = useState<string | null>(null)

  const change = (patch: Partial<ScreenDraft>) => {
    setSaved(null)
    setDraft((current) => ({ ...current, ...patch }))
  }

  const save = () =>
    update.mutate(toRequest(draft), {
      onSuccess: (result) => {
        setDraft(draftFrom(result))
        setSaved(t('screens.savedToast', { screen: result.title }))
      },
    })

  return (
    <>
      <p>
        <Link to="/tenant/screens">← {t('screens.back')}</Link>
      </p>
      <PageHeader title={`${screen.code} · ${draft.title || screen.defaultTitle}`} subtitle={screen.module} />
      <ErrorMessage error={update.error} />
      <StatusMessage message={saved} />

      <section className="card" aria-labelledby="header-heading">
        <h2 id="header-heading">{t('screens.header')}</h2>
        <label className="check">
          <input
            type="checkbox"
            checked={draft.isEnabled}
            disabled={!screen.canDisable}
            aria-describedby={screen.canDisable ? undefined : 'cannot-disable'}
            onChange={(event) => change({ isEnabled: event.target.checked })}
          />
          {t('screens.enabled')}
        </label>
        {!screen.canDisable && (
          <p id="cannot-disable" className="lock">
            {t('screens.cannotDisable')}
          </p>
        )}
        <div className="form-grid" style={{ marginTop: 14 }}>
          <div className="field">
            <label htmlFor="screen-title">{t('screens.screenTitle')}</label>
            <input
              id="screen-title"
              maxLength={TEXT_MAX_LENGTH}
              placeholder={screen.defaultTitle}
              value={draft.title}
              onChange={(event) => change({ title: event.target.value })}
            />
            <small>{t('screens.defaultValue', { value: screen.defaultTitle })}</small>
          </div>
          <div className="field">
            <label htmlFor="screen-subtitle">{t('screens.screenSubtitle')}</label>
            <input
              id="screen-subtitle"
              maxLength={TEXT_MAX_LENGTH}
              placeholder={screen.defaultSubtitle}
              value={draft.subtitle}
              onChange={(event) => change({ subtitle: event.target.value })}
            />
            <small>{t('screens.defaultValue', { value: screen.defaultSubtitle })}</small>
          </div>
        </div>
      </section>

      <section className="card" aria-labelledby="fields-heading">
        <h2 id="fields-heading">{t('screens.fieldsHeading')}</h2>
        {draft.fields.length === 0 ? (
          <p className="muted">{t('screens.noFields')}</p>
        ) : (
          <FieldTable fields={draft.fields} onChange={(fields) => change({ fields })} />
        )}
      </section>

      <div className="actions">
        <button type="button" className="btn" disabled={update.isPending} onClick={save}>
          {update.isPending ? t('common.saving') : t('common.save')}
        </button>
        <button type="button" className="btn secondary" onClick={() => change(defaultDraft(screen))}>
          {t('common.reset')}
        </button>
      </div>
    </>
  )
}
