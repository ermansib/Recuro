import { createContext, useContext } from 'react'
import { readPersona } from '../auth/personaStorage'
import type { AdminApi } from './contract'
import { createHttpClient } from './httpClient'

/** The real client. Tests provide a fake through ApiContext instead. */
export const httpApi: AdminApi = createHttpClient(() => readPersona()?.id ?? null)

export const ApiContext = createContext<AdminApi>(httpApi)

export function useApi(): AdminApi {
  return useContext(ApiContext)
}
