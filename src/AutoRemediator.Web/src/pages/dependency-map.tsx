import { useState } from 'react'
import { RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { DataTable } from '@/components/data-table'
import { columnHelper } from '@/lib/table'
import { PageHeader } from '@/components/page-header'
import { Pill } from '@/components/pill'
import { api, errorMessage } from '@/lib/api'
import { shortSlug } from '@/lib/format'
import { dependencyTone } from '@/lib/status'
import type { DependencyMap, DependencyMapEntry } from '@/lib/types'
import { useTitle } from '@/hooks'

function Version({ entry: e }: { entry: DependencyMapEntry }) {
  if (e.status === 'Outdated' && e.latestVersion !== null) {
    return (
      <span className="font-mono">
        <span className="text-muted-foreground">{e.currentVersion}</span>
        <span className="mx-1.5 text-subtle">→</span>
        <span className="text-success">{e.latestVersion}</span>
      </span>
    )
  }
  if (e.status === 'Ignored') return <span className="font-mono text-subtle">{e.currentVersion} · held</span>
  return <span className="font-mono">{e.currentVersion ?? 'unknown'}</span>
}

const col = columnHelper<DependencyMapEntry>()
const columns = col.columns([
  col.accessor('repositorySlug', { header: 'Repository', cell: (c) => <span className="font-mono">{shortSlug(c.getValue())}</span> }),
  col.accessor('packageId', { header: 'Package', cell: (c) => <span className="font-mono">{c.getValue()}</span> }),
  col.display({ id: 'version', header: 'Version', cell: (c) => <Version entry={c.row.original} /> }),
  col.accessor('status', { header: 'Status', cell: (c) => <Pill tone={dependencyTone(c.getValue())}>{c.getValue()}</Pill> }),
])

export function DependencyMapPage() {
  useTitle('Dependency Map · AutoRemediator')
  const [map, setMap] = useState<DependencyMap | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // The map is computed live against Azure DevOps and every feed, so it only runs on request.
  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      setMap(await api.dependencyMap())
    } catch (e) {
      setError(`Failed to load dependency map: ${errorMessage(e)}`)
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      <PageHeader title="Dependency Map">
        <Button onClick={() => void load()} disabled={loading} data-testid="refresh-map">
          <RefreshCw /> {loading ? 'Loading…' : 'Refresh'}
        </Button>
      </PageHeader>

      {error !== null ? (
        <Alert variant="warning" data-testid="map-error">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      ) : map === null ? (
        <p className="italic">Click Refresh to run analysis.</p>
      ) : (
        <Card className="py-0">
          <DataTable
            columns={columns}
            data={map.entries}
            empty={<span data-testid="map-empty">No matched packages found.</span>}
            caption="Dependency map" testId="map-table"
          />
        </Card>
      )}
    </>
  )
}
