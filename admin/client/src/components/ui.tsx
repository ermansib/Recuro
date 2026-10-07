import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export function PageHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <header className="page-header">
      <h1>{title}</h1>
      {subtitle && <p>{subtitle}</p>}
    </header>
  )
}

interface QueryStateProps {
  isLoading: boolean
  error: Error | null
  children: ReactNode
}

/** Shows loading and error states for a query, and the content once data is there. */
export function QueryState({ isLoading, error, children }: QueryStateProps) {
  const { t } = useTranslation()
  if (isLoading) return <p role="status">{t('common.loading')}</p>
  if (error) return <ErrorMessage error={error} />
  return <>{children}</>
}

export function ErrorMessage({ error }: { error: Error | null }) {
  const { t } = useTranslation()
  if (!error) return null
  return (
    <p className="error" role="alert">
      {t('common.error', { message: error.message })}
    </p>
  )
}

export function StatusMessage({ message }: { message: string | null }) {
  return (
    <p className={message ? 'success' : 'sr-only'} role="status">
      {message}
    </p>
  )
}
