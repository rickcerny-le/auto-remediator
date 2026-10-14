import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Pill } from '@/components/pill'
import { ErrorSummary, Field, FieldNotes } from '@/components/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DataTable } from '@/components/data-table'
import { columnHelper } from '@/lib/table'
import { PageHeader } from '@/components/page-header'
import { api, errorMessage, readFieldErrors } from '@/lib/api'
import { BUILT_IN_SCHEDULE, describedBy, join, split } from '@/lib/form'
import type { ManagedRepository } from '@/lib/types'
import { useRunNow, useTitle } from '@/hooks'

const settingsLabels: Record<string, string> = {
  patterns: 'Package patterns',
  excludes: 'Exclude patterns',
  feeds: 'Feeds',
  ignore: 'Ignore',
  strategy: 'Strategy',
  defaultSchedule: 'Default schedule',
}

type SettingsBadge = { label: string; tone: 'mute' | 'info' | 'warn' }

/** Inherits: no overrides. Overrides: inheriting with overrides. Local only: inheritance off. */
function settingsBadge({ settings }: ManagedRepository): SettingsBadge {
  if (!settings) return { label: 'Inherits', tone: 'mute' }
  if (!settings.inheritGlobal) return { label: 'Local only', tone: 'warn' }
  const overrides =
    settings.patterns.length + settings.excludes.length + settings.feeds.length + settings.ignore.length > 0 ||
    settings.strategy !== null ||
    settings.allowPrerelease !== null ||
    !!settings.schedule
  return overrides ? { label: 'Overrides', tone: 'info' } : { label: 'Inherits', tone: 'mute' }
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
      id: 'settings',
      header: 'Settings',
      cell: (c) => {
        const badge = settingsBadge(c.row.original)
        return (
          <Pill tone={badge.tone} testId="repo-settings-badge">
            {badge.label}
          </Pill>
        )
      },
    }),
    col.display({
      id: 'schedule',
      header: 'Schedule',
      cell: (c) => {
        const schedule = c.row.original.settings?.schedule
        return schedule ? <span className="font-mono" data-testid="repo-schedule">{schedule}</span> : <span className="text-muted-foreground" data-testid="repo-schedule">default</span>
      },
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
  const [defaultSchedule, setDefaultSchedule] = useState('')
  const [settingsErrors, setSettingsErrors] = useState<Record<string, string>>({})

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
        setPatterns(join(s.patterns))
        setExcludes(join(s.excludes))
        setFeeds(join(s.feeds))
        setIgnore(join(s.ignore))
        setStrategy(s.strategy)
        setAllowPrerelease(s.allowPrerelease)
        setDefaultSchedule(s.defaultSchedule ?? '')
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
        defaultSchedule: defaultSchedule.trim() || null,
      })
      if (response.status === 400) {
        setSettingsErrors(await readFieldErrors(response))
        setStatus('Settings were not saved. Fix the highlighted fields.')
        return
      }
      setSettingsErrors({})
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

  const { runNow, pending: running } = useRunNow(setStatus)

  const columns = useMemo(
    () => repoColumns({ pending: new Set([...pending, ...running]), onDelete: deleteRepo, onToggle: toggleRepo, onRunNow: runNow }),
    [pending, running, deleteRepo, toggleRepo, runNow],
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
          <h2 className="text-base font-semibold">Global settings</h2>
          <ErrorSummary errors={settingsErrors} labels={settingsLabels} testId="settings-errors" />
          <Field id="patterns" label="Package patterns (comma-separated)" placeholder="Contoso.*" value={patterns} onChange={setPatterns} testId="patterns" error={settingsErrors.patterns} />
          <Field id="excludes" label="Exclude patterns" placeholder="Contoso.Legacy.*" value={excludes} onChange={setExcludes} testId="excludes" error={settingsErrors.excludes} />
          <Field id="feeds" label="Feeds" placeholder="https://pkgs.dev.azure.com/Contoso/..." value={feeds} onChange={setFeeds} testId="feeds" error={settingsErrors.feeds} />
          <Field id="ignore" label="Ignore (globs, held from updates)" placeholder="Contoso.Legacy.*" value={ignore} onChange={setIgnore} testId="ignore" error={settingsErrors.ignore} />
          <div className="flex flex-wrap items-start gap-6">
            <div className="grid gap-1.5">
              <Label htmlFor="strategy">Strategy</Label>
              <Select value={strategy} onValueChange={setStrategy}>
                <SelectTrigger
                  id="strategy"
                  className="w-48"
                  aria-invalid={settingsErrors.strategy ? true : undefined}
                  aria-describedby={describedBy('strategy', { error: settingsErrors.strategy })}
                  data-testid="strategy"
                >
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
              <FieldNotes id="strategy" name="strategy" error={settingsErrors.strategy} />
            </div>
            <div className="flex h-9 items-center gap-2 self-end">
              <Switch id="prerelease" checked={allowPrerelease} onCheckedChange={setAllowPrerelease} data-testid="allow-prerelease" />
              <Label htmlFor="prerelease">Allow pre-release</Label>
            </div>
          </div>
          <Field
            id="default-schedule"
            name="defaultSchedule"
            label="Default schedule (cron, UTC)"
            placeholder={BUILT_IN_SCHEDULE}
            value={defaultSchedule}
            onChange={setDefaultSchedule}
            testId="default-schedule"
            help="Used by repositories without their own schedule. Empty means daily at 00:00 UTC."
            error={settingsErrors.defaultSchedule}
          />
          <Button type="submit" className="justify-self-start" data-testid="save-settings">
            Save settings
          </Button>
        </form>
      </Card>

      <h2 className="mb-3 text-base font-semibold">Managed repositories</h2>
      <Card className="mb-6 py-0">
        <DataTable
          columns={columns}
          data={repos}
          caption="Managed repositories"
          rowHref={(repo) => `/configuration/repositories/${repo.id}`}
          primaryColumnId="name"
          empty="No repositories configured."
          testId="repos-table"
        />
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
