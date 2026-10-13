import type { RunDiagnostic } from './types'

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' })

/** A timestamp in the viewer's local time, short form. */
export function formatDate(iso: string): string {
  return dateTime.format(new Date(iso))
}

/** The last segment of an "org/project/repo" slug. */
export function shortSlug(slug: string): string {
  const i = slug.lastIndexOf('/')
  return i >= 0 ? slug.slice(i + 1) : slug
}

export function plural(count: number, word: string): string {
  return `${count} ${word}${count === 1 ? '' : 's'}`
}

export function location(d: RunDiagnostic): string {
  if (d.path === null) return '—'
  if (d.line === null) return d.path
  return `${d.path}:${d.line}${d.column === null ? '' : `:${d.column}`}`
}
