import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { cn, focusRing } from '@/lib/utils'
import type { RunNowResult } from '@/lib/types'

/** The status text for a Run now response: queued (with a disabled warning), held (with a link), or failed. */
export function runNowMessage(name: string, { status, body }: RunNowResult): ReactNode {
  if (status === 202) {
    return (
      <>
        Run queued for {name}. It appears in <Link to="/runs" className={cn(focusRing, 'rounded-sm underline')}>Runs</Link> when the worker
        picks it up.
        {body.enabled === false && ` ${name} is disabled and will not run on schedule.`}
      </>
    )
  }
  if (status === 409 && body.heldRunId) {
    return (
      <>
        {name} is held for review; resolve the held run first.{' '}
        <Link to={`/runs/${body.heldRunId}`} className={cn(focusRing, 'rounded-sm underline')}>
          Open the held run
        </Link>
      </>
    )
  }
  return `Could not run ${name}: ${body.message ?? `request failed (${status})`}`
}
