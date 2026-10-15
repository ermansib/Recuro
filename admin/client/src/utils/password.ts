// Temporary passwords an admin hands over themselves (workspace owners, people added from Roles & people).
const SUGGESTED_LENGTH = 14
const CHARACTER_SETS = ['ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnpqrstuvwxyz', '23456789', '!@#$%&*?'] as const

function randomIndex(size: number): number {
  const value = new Uint32Array(1)
  crypto.getRandomValues(value)
  return value[0]! % size
}

function pick(characters: string): string {
  return characters[randomIndex(characters.length)]!
}

/** A random password that meets every password rule, without look-alike characters (0/O, 1/l/I). */
export function suggestTemporaryPassword(): string {
  const all = CHARACTER_SETS.join('')
  const characters = CHARACTER_SETS.map(pick)
  while (characters.length < SUGGESTED_LENGTH) characters.push(pick(all))
  for (let i = characters.length - 1; i > 0; i--) {
    const j = randomIndex(i + 1)
    ;[characters[i], characters[j]] = [characters[j]!, characters[i]!]
  }
  return characters.join('')
}
