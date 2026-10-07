import { useTranslation } from 'react-i18next'
import type { EmailMessage } from '../domain/types'
import { relativeTime } from '../utils/format'
import { Chip, Modal } from './ui'

export function EmailModal({ email, onClose }: { email: EmailMessage; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <Modal
      title={<span style={{ fontSize: 14 }}>{email.subject}</span>}
      tone="navy"
      className="em-modal"
      onClose={onClose}
      footer={
        <button type="button" className="btn btn-primary" onClick={onClose}>
          {t('common.close')}
        </button>
      }
    >
      <div className="em-meta">
        <div className="em-tagrow">
          <Chip tone="navy">{email.tag}</Chip>
          <Chip tone="slate">{relativeTime(email.createdAt)}</Chip>
        </div>
        <div className="row">
          <span className="k">{t('common.drawer.from')}</span>
          <b>{email.from}</b>
        </div>
        <div className="row">
          <span className="k">{t('common.drawer.to')}</span>
          <b>{email.to}</b>
        </div>
        <div className="row">
          <span className="k">{t('common.drawer.subject')}</span>
          <b>{email.subject}</b>
        </div>
      </div>
      <div className="em-body">
        {email.paragraphs.map((p, i) => (
          <p key={i}>{p}</p>
        ))}
        {email.cta && <span className="cta">{email.cta}</span>}
        <div className="sig">{email.signature}</div>
      </div>
    </Modal>
  )
}
