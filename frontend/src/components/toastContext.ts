import { createContext, useCallback, useContext } from 'react'

export type ToastType = 'info' | 'move' | 'warn'
export type PushToast = (message: string, type?: ToastType) => void

export const ToastContext = createContext<PushToast>(() => {})

export const useToast = () => useContext(ToastContext)

/** Turns an API error into a warning toast. */
export function useErrorToast() {
  const toast = useToast()
  return useCallback((e: unknown) => toast(e instanceof Error ? e.message : String(e), 'warn'), [toast])
}
