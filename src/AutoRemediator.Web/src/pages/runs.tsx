import { useCallback } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DataTable } from '@/components/data-table'
import { columnHelper } from '@/lib/table'
import { PageHeader } from '@/components/page-header'
import { PullRequestLink, RunStatus, Timestamp } from '@/components/run-cells'
import { api, errorMessage } from '@/lib/api'
import { runStatuses, runStatusLabel } from '@/lib/status'
import type { RunSummary } from '@/lib/types'
import { useLoad, useTitle } from '@/hooks'

const col = columnHelper<RunSummary>()

const columns = col.columns([
  col.accessor('repositorySlug', { header: 'Repository', cell: (c) => <span className="font-mono">{c.getValue()}</span> }),
  col.accessor('status', { header: 'Status', cell: (c) => <RunStatus status={c.getValue()} /> }),
  col.accessor('startedAtUtc', { header: 'Started', cell: (c) => <Timestamp iso={c.getValue()} /> }),
  col.accessor('updateCount', { header: 'Updates', cell: (c) => <span className="font-mono">{c.getValue()}</span> }),
  col.accessor('pullRequestUrl', { header: 'PR', cell: (c) => <PullRequestLink url={c.getValue()} /> }),
])

const ALL = 'all'
const noRuns: RunSummary[] = []

export function RunsPage() {
  useTitle('Runs · AutoRemediator')
  const navigate = useNavigate()
  // The status filter lives in the URL (?status=...) so a filtered feed can be linked to.
  const [params, setParams] = useSearchParams()
  const status = params.get('status') ?? undefined
  const load = useCallback(() => api.runs(status), [status])
  const { data: runs = noRuns, error, loading, reload } = useLoad(load)

  const setStatus = (value: string) => setParams(value === ALL ? {} : { status: value })

  return (
    <>
      <PageHeader title="Runs">
        <Select value={status ?? ALL} onValueChange={setStatus}>
          <SelectTrigger className="w-52" data-testid="status-filter" aria-label="Status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All statuses</SelectItem>
            {runStatuses.map((s) => (
              <SelectItem key={s} value={s}>
                {runStatusLabel(s)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button onClick={reload} disabled={loading} data-testid="refresh-runs">
          <RefreshCw /> {loading ? 'Loading…' : 'Refresh'}
        </Button>
      </PageHeader>

      {error !== undefined ? (
        <Alert variant="warning" data-testid="runs-error">
          <AlertDescription>Failed to load runs: {errorMessage(error)}</AlertDescription>
        </Alert>
      ) : (
        <Card className="py-0">
          <DataTable
            columns={columns}
            data={runs}
            loading={loading}
            empty={<span data-testid="runs-empty">No runs yet.</span>}
            onRowClick={(run) => navigate(`/runs/${run.id}`)}
            testId="runs-table"
          />
        </Card>
      )}
    </>
  )
}
