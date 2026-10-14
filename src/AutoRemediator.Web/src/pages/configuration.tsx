import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import { Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DataTable } from '@/components/data-table'
import { columnHelper } from '@/lib/table'
import { PageHeader } from '@/components/page-header'
import { api, errorMessage } from '@/lib/api'
import { cn, focusRing } from '@/lib/utils'
import type { ManagedRepository } from '@/lib/types'
import { useTitle } from '@/hooks'

const split = (csv: string) =>
  csv
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean)

function Field({ id, label, value, onChange, placeholder, testId }: { id: string; label: string; value: string; onChange: (v: string) => void; placeholder?: string; testId?: string }) {
  return (
    <div className="grid min-w-36 flex-1 gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} data-testid={testId} />
    </div>
  )
}

const col = columnHelper<ManagedRepository>()

interface RepoActions {
  /** Ids with a toggle or Run now request in flight. */
  pending: ReadonlySet<string>
  onDelete: (id: string) => Promise<void>
  onToggle: (repo: ManagedRepository) => Promise<void>
  onRunNow: (repo: ManagedRepository) => Promise<void>
}

const repoColumns = ({ pending, onDelete, onToggle, onRunNow }: RepoActions) =>
  col.columns([
    col.accessor('organization', { header: 'Organization' }),
    col.accessor('project', { header: 'Project' }),
    col.accessor('name', { header: 'Name' }),
    col.accessor('targetBranch', { header: 'Branch' }),
    col.accessor('enabled', {
      header: 'Enabled',
      cell: (c) => (
        <Switch
          checked={c.getValue()}
          disabled={pending.has(c.row.original.id)}
          onCheckedChange={() => void onToggle(c.row.original)}
          aria-label={`Enable ${c.row.original.name}`}
          data-testid="repo-enabled"
        />
      ),
    }),
    col.display({
      id: 'run-now',
      header: 'Run now',
      cell: (c) => {
        const repo = c.row.original
        const noteId = `not-scheduled-${repo.id}`
        return (
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={pending.has(repo.id)}
              aria-label={`Run ${repo.name} now`}
              aria-describedby={repo.enabled ? undefined : noteId}
              onClick={() => void onRunNow(repo)}
              data-testid="run-now"
            >
              Run now
            </Button>
            {!repo.enabled && (
              <span id={noteId} className="text-sm text-muted-foreground" data-testid="repo-not-scheduled">
                Disabled: not scheduled
              </span>
            )}
          </div>
        )
      },
    }),
    col.display({
      id: 'delete',
      header: '',
      cell: (c) => (
        <Button
          variant="ghost"
          size="icon-sm"
          className="text-destructive"
          aria-label={`Delete ${c.row.original.name}`}
          onClick={() => void onDelete(c.row.original.id)}
          data-testid="delete-repo"
        >
          <Trash2 />
        </Button>
      ),
    }),
  ])

const emptyRepo = { organization: '', project: '', name: '', targetBranch: 'main' }

export function ConfigurationPage() {
  useTitle('Configuration · AutoRemediator')
  const [status, setStatus] = useState<ReactNode | null>(null)
  const [repos, setRepos] = useState<ManagedRepository[]>([])
  const [pending, setPending] = useState<ReadonlySet<string>>(new Set())

  const [patterns, setPatterns] = useState('')
  const [excludes, setExcludes] = useState('')
  const [feeds, setFeeds] = useState('')
  const [ignore, setIgnore] = useState('')
  const [strategy, setStrategy] = useState('Minor')
  const [allowPrerelease, setAllowPrerelease] = useState(false)

  const [newRepo, setNewRepo] = useState(emptyRepo)

  const loadRepos = useCallback(async () => {
    try {
      setRepos(await api.repositories())
    } catch (e) {
      setStatus(`Could not load repositories: ${errorMessage(e)}`)
      setRepos([])
    }
  }, [])

  useEffect(() => {
    api
      .settings()
      .then((s) => {
        setPatterns(s.patterns.join(', '))
        setExcludes(s.excludes.join(', '))
        setFeeds(s.feeds.join(', '))
        setIgnore(s.ignore.join(', '))
        setStrategy(s.strategy)
        setAllowPrerelease(s.allowPrerelease)
      })
      .catch((e) => setStatus(`Could not load settings: ${errorMessage(e)}`))
      .finally(() => void loadRepos())
  }, [loadRepos])

  const saveSettings = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const response = await api.saveSettings({
        patterns: split(patterns),
        excludes: split(excludes),
        feeds: split(feeds),
        strategy,
        ignore: split(ignore),
        allowPrerelease,
      })
      setStatus(response.ok ? 'Settings saved.' : `Save failed (${response.status}).`)
    } catch (e) {
      setStatus(`Save failed: ${errorMessage(e)}`)
    }
  }

  const addRepo = async (e: FormEvent) => {
    e.preventDefault()
    try {
      const response = await api.addRepository({ ...newRepo, enabled: true, targetBranch: newRepo.targetBranch.trim() || 'main' })
      if (!response.ok) return setStatus(`Add failed (${response.status}).`)
      setNewRepo(emptyRepo)
    } catch (e) {
      return setStatus(`Add failed: ${errorMessage(e)}`)
    }
    await loadRepos()
  }

  const deleteRepo = useCallback(
    async (id: string) => {
      try {
        const response = await api.deleteRepository(id)
        if (!response.ok) setStatus(`Delete failed (${response.status}).`)
      } catch (e) {
        setStatus(`Delete failed: ${errorMessage(e)}`)
      }
      await loadRepos()
    },
    [loadRepos],
  )

  const track = useCallback(async (id: string, work: () => Promise<void>) => {
    setPending((p) => new Set(p).add(id))
    try {
      await work()
    } finally {
      setPending((p) => {
        const next = new Set(p)
        next.delete(id)
        return next
      })
    }
  }, [])

  const toggleRepo = useCallback(
    (repo: ManagedRepository) =>
      track(repo.id, async () => {
        try {
          const response = await api.updateRepository(repo.id, {
            organization: repo.organization,
            project: repo.project,
            name: repo.name,
            targetBranch: repo.targetBranch,
            enabled: !repo.enabled,
          })
          if (!response.ok) setStatus(`Could not update ${repo.name} (${response.status}).`)
        } catch (e) {
          setStatus(`Could not update ${repo.name}: ${errorMessage(e)}`)
        }
        await loadRepos()
      }),
    [loadRepos, track],
  )

  const runNow = useCallback(
    (repo: ManagedRepository) =>
      track(repo.id, async () => {
        try {
          const { status: code, body } = await api.runNow(repo.id)
          if (code === 202) {
            setStatus(
              <>
                Run queued for {repo.name}. It appears in <Link to="/runs" className={cn(focusRing, 'rounded-sm underline')}>Runs</Link> when the
                worker picks it up.
                {body.enabled === false && ` ${repo.name} is disabled and will not run on schedule.`}
              </>,
            )
          } else if (code === 409 && body.heldRunId) {
            setStatus(
              <>
                {repo.name} is held for review; resolve the held run first.{' '}
                <Link to={`/runs/${body.heldRunId}`} className={cn(focusRing, 'rounded-sm underline')}>
                  Open the held run
                </Link>
              </>,
            )
          } else {
            setStatus(`Could not run ${repo.name}: ${body.message ?? `request failed (${code})`}`)
          }
        } catch (e) {
          setStatus(`Could not run ${repo.name}: ${errorMessage(e)}`)
        }
      }),
    [track],
  )

  const columns = useMemo(
    () => repoColumns({ pending, onDelete: deleteRepo, onToggle: toggleRepo, onRunNow: runNow }),
    [pending, deleteRepo, toggleRepo, runNow],
  )

  const repoField = (key: keyof typeof emptyRepo, label: string) => (
    <Field id={`new-${key}`} label={label} value={newRepo[key]} onChange={(v) => setNewRepo((r) => ({ ...r, [key]: v }))} testId={`new-${key}`} />
  )

  return (
    <>
      <PageHeader title="Configuration" />

      {status !== null && (
        <Alert variant="info" role="status" className="mb-6" data-testid="config-status">
          <AlertDescription>{status}</AlertDescription>
        </Alert>
      )}

      <Card className="mb-8 max-w-[720px] p-6">
        <form className="grid gap-4" onSubmit={(e) => void saveSettings(e)}>
          <h2 className="text-base font-semibold">Targeting settings</h2>
          <Field id="patterns" label="Package patterns (comma-separated)" placeholder="Contoso.*" value={patterns} onChange={setPatterns} testId="patterns" />
          <Field id="excludes" label="Exclude patterns" placeholder="Contoso.Legacy.*" value={excludes} onChange={setExcludes} testId="excludes" />
          <Field id="feeds" label="Feeds" placeholder="https://pkgs.dev.azure.com/Contoso/..." value={feeds} onChange={setFeeds} testId="feeds" />
          <Field id="ignore" label="Ignore (globs, held from updates)" placeholder="Contoso.Legacy.*" value={ignore} onChange={setIgnore} testId="ignore" />
          <div className="flex flex-wrap items-end gap-6">
            <div className="grid gap-1.5">
              <Label htmlFor="strategy">Strategy</Label>
              <Select value={strategy} onValueChange={setStrategy}>
                <SelectTrigger id="strategy" className="w-48" data-testid="strategy">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {['Patch', 'Minor', 'Major'].map((s) => (
                    <SelectItem key={s} value={s}>
                      {s}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex h-9 items-center gap-2">
              <Switch id="prerelease" checked={allowPrerelease} onCheckedChange={setAllowPrerelease} data-testid="allow-prerelease" />
              <Label htmlFor="prerelease">Allow pre-release</Label>
            </div>
          </div>
          <Button type="submit" className="justify-self-start" data-testid="save-settings">
            Save settings
          </Button>
        </form>
      </Card>

      <h2 className="mb-3 text-base font-semibold">Managed repositories</h2>
      <Card className="mb-6 py-0">
        <DataTable columns={columns} data={repos} caption="Managed repositories" empty="No repositories configured." testId="repos-table" />
      </Card>

      <Card className="max-w-[900px] p-5">
        <h2 className="mb-3 text-base font-semibold">Add a repository</h2>
        <form className="flex flex-wrap items-end gap-3" onSubmit={(e) => void addRepo(e)}>
          {repoField('organization', 'Organization')}
          {repoField('project', 'Project')}
          {repoField('name', 'Repository')}
          {repoField('targetBranch', 'Branch')}
          <Button type="submit" className="bg-success text-primary-foreground hover:bg-success/90" data-testid="add-repo">
            Add
          </Button>
        </form>
      </Card>
    </>
  )
}
