/** Splits a comma-separated field into trimmed, non-empty entries. */
export const split = (csv: string) =>
  csv
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)

export const join = (items: readonly string[] | undefined) => (items ?? []).join(', ')

/** The cron expression the server falls back to when neither the repository nor the global settings set one. */
export const BUILT_IN_SCHEDULE = '0 0 * * *'

/** Ids a control should reference with aria-describedby: its server error first, then its help text. */
export function describedBy(id: string, { help, error }: { help?: unknown; error?: string }) {
  const ids = [error ? `${id}-error` : null, help ? `${id}-help` : null].filter(Boolean)
  return ids.length > 0 ? ids.join(' ') : undefined
}
