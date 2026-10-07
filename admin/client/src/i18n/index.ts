// All user-facing strings live in the JSON files under ./en, one per area (NFR-07).
import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import branding from './en/branding.json'
import common from './en/common.json'
import screens from './en/screens.json'
import signin from './en/signin.json'
import tenantThemes from './en/tenantThemes.json'
import tenants from './en/tenants.json'
import themes from './en/themes.json'

export const resources = {
  en: { translation: { common, signin, tenants, tenantThemes, themes, branding, screens } },
} as const

void i18n.use(initReactI18next).init({
  resources,
  lng: 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnNull: false,
})

export default i18n
