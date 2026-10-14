import { Link } from 'react-router'
import { Plus, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { DataTable } from '@/components/data-table'
import { columnHelper } from '@/lib/table'
import { PageHeader } from '@/components/page-header'
import { Pill } from '@/components/pill'
import { PullRequestLink, RunStatus, Timestamp } from '@/components/run-cells'
import { api } from '@/lib/api'
import { shortSlug } from '@/lib/format'
import { cn, focusRing } from '@/lib/utils'
import type { ManagedRepository, RunSummary } from '@/lib/types'
import { useLoad, useTitle } from '@/hooks'

const col = columnHelper<RunSummary>()

const recentColumns = col.columns([
  col.accessor('repositorySlug', {
    header: 'Repository',
    cell: (c) => <span className="font-mono">{shortSlug(c.getValue())}</span>,
  }),
  col.accessor('status', { header: 'Status', cell: (c) => <RunStatus status={c.getValue()} trigger={c.row.original.trigger} /> }),
  col.accessor('updateCount', {
    header: 'Updates',
    cell: (c) => c.getValue(),
    meta: { className: 'text-right' },
  }),
  col.accessor('pullRequestUrl', { header: 'PR', cell: (c) => <PullRequestLink url={c.getValue()} /> }),
  col.accessor('startedAtUtc', {
    header: 'Started',
    cell: (c) => <Timestamp iso={c.getValue()} />,
    meta: { className: 'text-right' },
  }),
])

const heldColumns = col.columns([
  col.accessor('repositorySlug', {
    header: 'Repository',
    cell: (c) => <span className="font-mono">{shortSlug(c.getValue())}</span>,
  }),
  col.accessor('startedAtUtc', { header: 'Held since', cell: (c) => <Timestamp iso={c.getValue()} /> }),
])

function Kpi({ label, value, sub, valueClass }: { label: string; value: React.ReactNode; sub: React.ReactNode; valueClass?: string }) {
  return (
    <Card className="gap-0 p-5">
      <div className="ar-label">{label}</div>
      <div className={`mt-2 text-kpi font-bold tracking-tight tabular-nums ${valueClass ?? ''}`}>{value}</div>
      <div className="mt-1 text-label text-muted-foreground">{sub}</div>
    </Card>
  )
}

function TargetingRow({ label, children, last }: { label: string; children: React.ReactNode; last?: boolean }) {
  return (
    <div className={`flex items-center justify-between gap-4 py-2 ${last ? '' : 'border-b'}`}>
      <span className="ar-label">{label}</span>
      {children}
    </div>
  )
}

// Best-effort dashboard: each source degrades to empty on its own if the API can't serve it.
const loadDashboard = async () => {
  const [repos, runs, settings] = await Promise.all([
    api.repositories().catch((): ManagedRepository[] => []),
    api.runs().catch((): RunSummary[] => []),
    api.settings().catch(() => null),
  ])
  return { repos, runs, settings }
}

const noRuns: RunSummary[] = []

const join = (values: string[] | undefined) => (values && values.length > 0 ? values.join(', ') : null)

export function HomePage() {
  useTitle('Overview · AutoRemediator')
  const { data, loading, reload } = useLoad(loadDashboard)
  const repoCount = data?.repos.length ?? 0
  const runs = data?.runs ?? noRuns
  const settings = data?.settings ?? null

  const runHref = (run: RunSummary) => `/runs/${run.id}`
  const held = runs.filter((r) => r.status === 'AwaitingReview')
  const failed = runs.filter((r) => r.status === 'Failed').length
  // Known defect (PRD §8): this counts runs with a PR link, not open PRs. Ported as-is.
  const prs = runs.filter((r) => r.pullRequestUrl).length
  const strategy = settings?.strategy ?? '—'

  return (
    <>
      <PageHeader title="Overview" subtitle="automated dependency remediation">
        <Button variant="outline" asChild>
          <Link to="/configuration">
            <Plus /> Add repo
          </Link>
        </Button>
        <Button onClick={reload} disabled={loading} data-testid="refresh-home">
          <RefreshCw /> Refresh
        </Button>
      </PageHeader>

      <div className="mb-6 grid grid-cols-2 gap-4 md:grid-cols-4">
        <Kpi label="Managed repos" value={repoCount} sub="enrolled for remediation" />
        <Kpi
          label="Runs"
          value={runs.length}
          sub={
            <>
              <span className="font-semibold text-destructive tabular-nums">{failed}</span> failed
            </>
          }
        />
        <Kpi label="Open PRs" value={prs} sub="opened by AutoRemediator" valueClass="text-success" />
        <Kpi label="Strategy" value={strategy} sub="update policy" />
      </div>

      {held.length > 0 && (
        <Card className="mb-6 gap-0 py-0" data-testid="held-panel">
          <div className="flex items-center border-b p-4">
            <h2 className="mr-auto text-base font-semibold">Held for review</h2>
            <Pill tone="warn">
              {held.length} {held.length === 1 ? 'repository' : 'repositories'}
            </Pill>
          </div>
          <DataTable
            columns={heldColumns}
            data={held}
            caption="Runs awaiting review"
            rowHref={runHref}
            testId="held-table"
          />
        </Card>
      )}

      <div className="grid gap-6 md:grid-cols-3">
        <Card className="gap-0 py-0 md:col-span-2">
          <div className="flex items-center border-b p-4">
            <h2 className="mr-auto text-base font-semibold">Recent runs</h2>
            <Link to="/runs" className={cn(focusRing, 'rounded-sm text-sm text-info hover:underline')}>
              View all →
            </Link>
          </div>
          <DataTable
            columns={recentColumns}
            data={runs.slice(0, 6)}
            caption="Recent runs"
            rowHref={runHref}
            loading={loading}
            empty="No runs yet."
            testId="recent-runs"
          />
        </Card>

        <Card className="gap-0 p-4" data-testid="targeting">
          <h2 className="mb-3 text-base font-semibold">Global targeting</h2>
          <TargetingRow label="Patterns">
            <span className="font-mono text-success">{join(settings?.patterns) ?? '—'}</span>
          </TargetingRow>
          <TargetingRow label="Feeds">
            <span className="font-mono break-all">{join(settings?.feeds) ?? '—'}</span>
          </TargetingRow>
          <TargetingRow label="Ignore">
            <span className="font-mono">{join(settings?.ignore) ?? 'none'}</span>
          </TargetingRow>
          <TargetingRow label="Strategy" last>
            <Pill tone="info">{strategy}</Pill>
          </TargetingRow>
          <Button variant="link" size="sm" className="mt-2 self-start px-0" asChild>
            <Link to="/configuration">Edit configuration →</Link>
          </Button>
        </Card>
      </div>
    </>
  )
}
