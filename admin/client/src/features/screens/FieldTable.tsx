import { useTranslation } from 'react-i18next'
import { moveField, setVisible, type FieldDraft } from './screenDraft'

const LABEL_MAX_LENGTH = 80

interface FieldTableProps {
  fields: FieldDraft[]
  onChange: (fields: FieldDraft[]) => void
}

/** Per-field label, visibility, required flag and order. Locked fields keep visible and required. */
export function FieldTable({ fields, onChange }: FieldTableProps) {
  const { t } = useTranslation()

  const replace = (index: number, field: FieldDraft) => onChange(fields.map((current, i) => (i === index ? field : current)))

  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th scope="col">{t('screens.field.order')}</th>
            <th scope="col">{t('screens.field.label')}</th>
            <th scope="col">{t('screens.field.type')}</th>
            <th scope="col">{t('screens.field.visible')}</th>
            <th scope="col">{t('screens.field.required')}</th>
          </tr>
        </thead>
        <tbody>
          {fields.map((field, index) => {
            const name = field.label || field.defaultLabel
            return (
              <tr key={field.key}>
                <td>
                  <button
                    type="button"
                    className="btn icon"
                    disabled={index === 0}
                    aria-label={t('screens.field.moveUp', { field: name })}
                    onClick={() => onChange(moveField(fields, index, -1))}
                  >
                    ↑
                  </button>{' '}
                  <button
                    type="button"
                    className="btn icon"
                    disabled={index === fields.length - 1}
                    aria-label={t('screens.field.moveDown', { field: name })}
                    onClick={() => onChange(moveField(fields, index, 1))}
                  >
                    ↓
                  </button>
                </td>
                <td>
                  <input
                    className="input"
                    maxLength={LABEL_MAX_LENGTH}
                    placeholder={field.defaultLabel}
                    aria-label={t('screens.field.labelFor', { field: field.defaultLabel })}
                    value={field.label}
                    onChange={(event) => replace(index, { ...field, label: event.target.value })}
                  />
                  {field.isLocked && <div className="lock">🔒 {t('screens.field.locked')}</div>}
                </td>
                <td>{t(`screens.dataType.${field.dataType}`)}</td>
                <td>
                  <input
                    type="checkbox"
                    aria-label={t('screens.field.visibleFor', { field: name })}
                    checked={field.isVisible}
                    disabled={field.isLocked}
                    onChange={(event) => replace(index, setVisible(field, event.target.checked))}
                  />
                </td>
                <td>
                  <input
                    type="checkbox"
                    aria-label={t('screens.field.requiredFor', { field: name })}
                    checked={field.isRequired}
                    disabled={field.isLocked || !field.isVisible}
                    onChange={(event) => replace(index, { ...field, isRequired: event.target.checked })}
                  />
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
