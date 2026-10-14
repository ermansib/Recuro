// Where the portal signs people in: Keycloak realm `recuro`, public client `recuro-portal` (authorization
// code + PKCE, see admin/keycloak/recuro-realm.json). 127.0.0.1 rather than localhost because Docker's
// IPv6 mapping breaks localhost:8080 on some machines. Override per environment with the VITE_ variables.
export const identityProvider = {
  authority: import.meta.env.VITE_OIDC_AUTHORITY || 'http://127.0.0.1:8080/realms/recuro',
  clientId: import.meta.env.VITE_OIDC_CLIENT_ID || 'recuro-portal',
  /** Route the identity provider sends people back to. Must match the client's redirect URIs. */
  callbackPath: '/auth/callback',
}
