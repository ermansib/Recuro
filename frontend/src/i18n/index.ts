// All user-facing strings live in the JSON files under ./en, one per area (NFR-07).
// Add a locale by adding a sibling folder and registering it here.
import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import approvals from './en/approvals.json'
import auth from './en/auth.json'
import assessment from './en/assessment.json'
import bgv from './en/bgv.json'
import careers from './en/careers.json'
import common from './en/common.json'
import dashboard from './en/dashboard.json'
import jd from './en/jd.json'
import mrf from './en/mrf.json'
import offer from './en/offer.json'
import pipeline from './en/pipeline.json'

export const resources = {
  en: { translation: { common, auth, dashboard, mrf, approvals, jd, pipeline, assessment, bgv, offer, careers } },
} as const

void i18n.use(initReactI18next).init({
  resources,
  lng: 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnNull: false,
})

export default i18n
