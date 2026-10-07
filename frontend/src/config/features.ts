// Build-time switches. Set VITE_DEMO_PERSONAS=true to keep the persona switcher in a production
// build (e.g. a sales demo); it is always on in `npm run dev` and tests.
export const features = {
  demoPersonas: import.meta.env.DEV || import.meta.env.VITE_DEMO_PERSONAS === 'true',
}
