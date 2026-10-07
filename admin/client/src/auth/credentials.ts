/** Supplies the headers that identify the caller: a Keycloak bearer token, or the development persona. */
export type CredentialProvider = () => Promise<Record<string, string>>

let provider: CredentialProvider = () => Promise.resolve({})

export function setCredentialProvider(next: CredentialProvider): void {
  provider = next
}

export function credentialHeaders(): Promise<Record<string, string>> {
  return provider()
}
