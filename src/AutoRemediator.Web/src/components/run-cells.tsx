import { Pill } from '@/components/pill'
import { runStatusLabel, runStatusTone } from '@/lib/status'
import { formatDate } from '@/lib/format'

export function RunStatus({ status }: { status: string }) {
  return <Pill tone={runStatusTone(status)}>{runStatusLabel(status)}</Pill>
}

export function PullRequestLink({ url }: { url: string | null }) {
  if (url === null) return <span className="font-mono text-subtle">—</span>
  return (
    <a
      href={url}
      target="_blank"
      rel="noreferrer"
      className="font-mono text-info hover:underline"
      onClick={(e) => e.stopPropagation()}
    >
      open
    </a>
  )
}

export function Timestamp({ iso }: { iso: string }) {
  return <span className="font-mono text-muted-foreground">{formatDate(iso)}</span>
}
