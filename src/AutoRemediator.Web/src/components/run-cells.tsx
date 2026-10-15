import { Pill } from '@/components/pill'
import { runStatusLabel, runStatusTone } from '@/lib/status'
import { formatDate } from '@/lib/format'
import { cn, focusRing } from '@/lib/utils'

export function RunStatus({ status, trigger }: { status: string; trigger?: 'Scheduled' | 'Manual' }) {
  const pill = <Pill tone={runStatusTone(status)}>{runStatusLabel(status)}</Pill>
  if (trigger !== 'Manual') return pill
  return (
    <span className="inline-flex flex-wrap items-center gap-1.5">
      {pill}
      <Pill tone="info" testId="trigger-manual">
        Manual
      </Pill>
    </span>
  )
}

export function PullRequestLink({ url }: { url: string | null }) {
  if (url === null) return (
      <span className="text-subtle" role="img" aria-label="No pull request">
        —
      </span>
    )
  return (
    <a
      href={url}
      target="_blank"
      rel="noreferrer"
      className={cn(focusRing, 'rounded-sm text-info hover:underline')}
    >
      open<span className="sr-only"> (opens in a new tab)</span>
    </a>
  )
}

export function Timestamp({ iso }: { iso: string }) {
  return <span className="text-muted-foreground">{formatDate(iso)}</span>
}
