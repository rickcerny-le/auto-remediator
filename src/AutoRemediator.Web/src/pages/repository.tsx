import { useCallback, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ArrowLeft } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DataTable } from '@/components/data-table'
import { ErrorSummary, Field, FieldNotes } from '@/components/field'
import { Pill } from '@/components/pill'
import { PageHeader } from '@/components/page-header'
import { columnHelper } from '@/lib/table'
import type { Tone } from '@/lib/status'
import { api, ApiError, errorMessage, readFieldErrors } from '@/lib/api'
import { BUILT_IN_SCHEDULE, describedBy, join, split } from '@/lib/form'
import type { EffectiveListItem, EffectiveSettings, ManagedRepository, RepositorySettings, SettingSource, TargetingSettings } from '@/lib/types'
import { useLoad, useRunNow, useTitle } from '@/hooks'

const INHERIT = 'inherit'
const noSettings: RepositorySettings = {
  inheritGlobal: true,
  patterns: [],
  excludes: [],
  feeds: [],
  ignore: [],
  strategy: null,
  allowPrerelease: null,
  schedule: null,
}

const settingsLabels: Record<string, string> = {
  patterns: 'Patterns',
  excludes: 'Excludes',
  feeds: 'Feeds',
  ignore: 'Ignore',
  strategy: 'Strategy',
  schedule: 'Schedule',
}

const sourceLabel: Record<SettingSource, string> = { Global: 'Global', Repository: 'Repo', Both: 'Both', Default: 'Default' }
const sourceTone: Record<SettingSource, Tone> = { Global: 'mute', Repository: 'info', Both: 'ok', Default: 'mute' }

function SourcePill({ source, testId }: { source: SettingSource; testId?: string }) {
  return (
    <Pill tone={sourceTone[source]} testId={testId}>
      {sourceLabel[source]}
    </Pill>
  )
}

type EffectiveRow =
  | { key: string; setting: string; kind: 'scalar'; value: string; source: SettingSource }
  | { key: string; setting: string; kind: 'list'; items: EffectiveListItem[] }

function effectiveRows(e: EffectiveSettings): EffectiveRow[] {
  const list = (key: string, setting: string, items: EffectiveListItem[]): EffectiveRow => ({ key, setting, kind: 'list', items })
  return [
    list('patterns', 'Patterns', e.patterns),
    list('excludes', 'Excludes', e.excludes),
    list('feeds', 'Feeds', e.feeds),
    list('ignore', 'Ignore', e.ignore),
    { key: 'strategy', setting: 'Strategy', kind: 'scalar', value: e.strategy.value, source: e.strategy.source },
    {
      key: 'allowPrerelease',
      setting: 'Allow pre-release',
      kind: 'scalar',
      value: e.allowPrerelease.value === 'true' ? 'Allowed' : 'Not allowed',
      source: e.allowPrerelease.source,
    },
    { key: 'schedule', setting: 'Schedule (cron, UTC)', kind: 'scalar', value: e.schedule.value, source: e.schedule.source },
  ]
}

const row = columnHelper<EffectiveRow>()
const effectiveColumns = row.columns([
  row.accessor('setting', { header: 'Setting', cell: (c) => <span className="font-medium">{c.getValue()}</span> }),
  row.display({
    id: 'value',
    header: 'Value',
    meta: { className: 'whitespace-normal' },
    cell: ({ row: { original: r } }) =>
      r.kind === 'scalar' ? (
        <span className={r.key === 'schedule' ? 'font-mono' : undefined} data-testid={`effective-value-${r.key}`}>
          {r.value}
        </span>
      ) : r.items.length === 0 ? (
        <span className="text-muted-foreground" data-testid={`effective-value-${r.key}`}>
          None
        </span>
      ) : (
        <ul className="grid gap-1" data-testid={`effective-value-${r.key}`}>
          {r.items.map((item) => (
            <li key={item.value} className="flex flex-wrap items-center gap-2" data-testid={`effective-item-${r.key}`}>
              <span className="font-mono break-all">{item.value}</span>
              <SourcePill source={item.source} testId="item-source" />
            </li>
          ))}
        </ul>
      ),
  }),
  row.display({
    id: 'source',
    header: 'Source',
    cell: ({ row: { original: r } }) =>
      r.kind === 'scalar' ? <SourcePill source={r.source} testId={`effective-source-${r.key}`} /> : <span className="text-muted-foreground">Per item</span>,
  }),
])

function RepositoryForm({ repo, onSaved, onMessage }: { repo: ManagedRepository; onSaved: (r: ManagedRepository) => void; onMessage: (m: ReactNode) => void }) {
  const [organization, setOrganization] = useState(repo.organization)
  const [project, setProject] = useState(repo.project)
  const [name, setName] = useState(repo.name)
  const [branch, setBranch] = useState(repo.targetBranch)
  const [enabled, setEnabled] = useState(repo.enabled)
  const [saving, setSaving] = useState(false)

  const save = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    try {
      // No settings: the server keeps the stored per-repository settings.
      const response = await api.updateRepository(repo.id, {
        organization: organization.trim(),
        project: project.trim(),
        name: name.trim(),
        targetBranch: branch.trim() || 'main',
        enabled,
      })
      if (response.ok) {
        onSaved((await response.json()) as ManagedRepository)
        onMessage('Repository saved.')
      } else {
        onMessage(`Could not save the repository (${response.status}).`)
      }
    } catch (error) {
      onMessage(`Could not save the repository: ${errorMessage(error)}`)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card className="mb-6 max-w-[720px] p-6" data-testid="repo-form">
      <form className="grid gap-4" onSubmit={(e) => void save(e)}>
        <h2 className="text-base font-semibold">Repository</h2>
        <Field
          id="repo-organization"
          label="Organization"
          value={organization}
          onChange={setOrganization}
          testId="repo-organization"
          help="Informational. All repositories use the configured Azure DevOps organization URL."
        />
        <Field id="repo-project" label="Project" value={project} onChange={setProject} testId="repo-project" />
        <Field id="repo-name" label="Name" value={name} onChange={setName} testId="repo-name" />
        <Field id="repo-branch" label="Branch" value={branch} onChange={setBranch} testId="repo-branch" />
        <div className="flex h-9 items-center gap-2">
          <Switch id="repo-form-enabled" checked={enabled} onCheckedChange={setEnabled} data-testid="repo-form-enabled" />
          <Label htmlFor="repo-form-enabled">Enabled</Label>
        </div>
        <Button type="submit" className="justify-self-start" disabled={saving} data-testid="save-repo">
          Save repository
        </Button>
      </form>
    </Card>
  )
}

type PrereleaseChoice = typeof INHERIT | 'allow' | 'deny'

const prereleaseChoice = (v: boolean | null): PrereleaseChoice => (v === null ? INHERIT : v ? 'allow' : 'deny')

function SettingsForm({
  repoId,
  initial,
  global,
  onSaved,
  onMessage,
}: {
  repoId: string
  initial: RepositorySettings
  global: TargetingSettings | undefined
  onSaved: (s: RepositorySettings) => void
  onMessage: (m: ReactNode) => void
}) {
  const [inherit, setInherit] = useState(initial.inheritGlobal)
  const [patterns, setPatterns] = useState(join(initial.patterns))
  const [excludes, setExcludes] = useState(join(initial.excludes))
  const [feeds, setFeeds] = useState(join(initial.feeds))
  const [ignore, setIgnore] = useState(join(initial.ignore))
  const [strategy, setStrategy] = useState(initial.strategy ?? INHERIT)
  const [prerelease, setPrerelease] = useState<PrereleaseChoice>(prereleaseChoice(initial.allowPrerelease))
  const [schedule, setSchedule] = useState(initial.schedule ?? '')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)

  const listHelp = inherit ? 'Added to the global list' : 'Used instead of the global list'
  const strategyInherit = inherit ? `Inherit (global: ${global?.strategy ?? 'Minor'})` : 'Default (Minor)'
  const prereleaseInherit = inherit ? `Inherit (global: ${global?.allowPrerelease ? 'Allow' : "Don't allow"})` : "Default (Don't allow)"
  const defaultSchedule = global?.defaultSchedule || BUILT_IN_SCHEDULE

  const save = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    try {
      const settings: RepositorySettings = {
        inheritGlobal: inherit,
        patterns: split(patterns),
        excludes: split(excludes),
        feeds: split(feeds),
        ignore: split(ignore),
        strategy: strategy === INHERIT ? null : strategy,
        allowPrerelease: prerelease === INHERIT ? null : prerelease === 'allow',
        schedule: schedule.trim() || null,
      }
      const response = await api.saveRepositorySettings(repoId, settings)
      if (response.status === 400) {
        setErrors(await readFieldErrors(response))
        onMessage('Settings were not saved. Fix the highlighted fields.')
      } else if (response.ok) {
        setErrors({})
        onSaved((await response.json()) as RepositorySettings)
        onMessage('Settings saved.')
      } else {
        onMessage(`Could not save the settings (${response.status}).`)
      }
    } catch (error) {
      onMessage(`Could not save the settings: ${errorMessage(error)}`)
    } finally {
      setSaving(false)
    }
  }

  const strategyError = errors.strategy
  const scheduleHelp = '5-field cron, UTC, at most every 15 minutes'

  return (
    <Card className="mb-6 max-w-[720px] p-6">
      <form className="grid gap-4" onSubmit={(e) => void save(e)} data-testid="repo-settings-form">
        <h2 className="text-base font-semibold">Settings</h2>
        <ErrorSummary errors={errors} labels={settingsLabels} testId="repo-settings-errors" />
        <div className="flex h-9 items-center gap-2">
          <Switch id="inherit-global" checked={inherit} onCheckedChange={setInherit} data-testid="inherit-global" />
          <Label htmlFor="inherit-global">Inherit global settings</Label>
        </div>
        {!inherit && (
          <Alert variant="info" role="status" data-testid="inherit-off-note">
            <AlertDescription>
              This repository uses only the values below. Unset strategy and pre-release fall back to Minor and no pre-release. The schedule
              still falls back to the global default.
            </AlertDescription>
          </Alert>
        )}
        <Field id="repo-patterns" name="patterns" label="Patterns (comma-separated)" placeholder="Contoso.*" value={patterns} onChange={setPatterns} testId="repo-patterns" help={listHelp} error={errors.patterns} />
        <Field id="repo-excludes" name="excludes" label="Excludes" placeholder="Contoso.Legacy.*" value={excludes} onChange={setExcludes} testId="repo-excludes" help={listHelp} error={errors.excludes} />
        <Field id="repo-feeds" name="feeds" label="Feeds" placeholder="https://pkgs.dev.azure.com/Contoso/..." value={feeds} onChange={setFeeds} testId="repo-feeds" help={listHelp} error={errors.feeds} />
        <Field id="repo-ignore" name="ignore" label="Ignore (held from updates)" placeholder="Contoso.Legacy.*" value={ignore} onChange={setIgnore} testId="repo-ignore" help={listHelp} error={errors.ignore} />
        <div className="flex flex-wrap items-start gap-6">
          <div className="grid gap-1.5">
            <Label htmlFor="repo-strategy">Strategy</Label>
            <Select value={strategy} onValueChange={setStrategy}>
              <SelectTrigger
                id="repo-strategy"
                className="w-56"
                aria-invalid={strategyError ? true : undefined}
                aria-describedby={describedBy('repo-strategy', { error: strategyError })}
                data-testid="repo-strategy"
              >
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={INHERIT}>{strategyInherit}</SelectItem>
                {['Patch', 'Minor', 'Major'].map((s) => (
                  <SelectItem key={s} value={s}>
                    {s}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <FieldNotes id="repo-strategy" name="strategy" error={strategyError} />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="repo-prerelease">Allow pre-release</Label>
            <Select value={prerelease} onValueChange={(v) => setPrerelease(v as PrereleaseChoice)}>
              <SelectTrigger id="repo-prerelease" className="w-56" data-testid="repo-prerelease">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={INHERIT}>{prereleaseInherit}</SelectItem>
                <SelectItem value="allow">Allow</SelectItem>
                <SelectItem value="deny">Don&apos;t allow</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <Field
          id="repo-schedule"
          name="schedule"
          label="Schedule (cron, UTC)"
          placeholder={`default: ${defaultSchedule}`}
          value={schedule}
          onChange={setSchedule}
          testId="repo-schedule"
          help={scheduleHelp}
          error={errors.schedule}
        />
        <Button type="submit" className="justify-self-start" disabled={saving} data-testid="save-repo-settings">
          Save settings
        </Button>
      </form>
    </Card>
  )
}

function EffectiveCard({ effective, error, loading }: { effective: EffectiveSettings | undefined; error: unknown; loading: boolean }) {
  return (
    <Card className="mb-6 gap-3 p-5" data-testid="effective-card">
      <h2 className="text-base font-semibold">Effective settings</h2>
      <p className="text-sm text-muted-foreground">What a run of this repository actually uses, and where each value comes from.</p>
      {error !== undefined && effective === undefined ? (
        <Alert variant="warning">
          <AlertDescription>Could not load the effective settings: {errorMessage(error)}</AlertDescription>
        </Alert>
      ) : (
        <>
          {effective && effective.warnings.length > 0 && (
            <Alert variant="warning" role="status" data-testid="effective-warnings">
              <AlertTitle>Warnings</AlertTitle>
              <AlertDescription>
                <ul className="list-disc pl-4">
                  {effective.warnings.map((w) => (
                    <li key={w}>{w}</li>
                  ))}
                </ul>
              </AlertDescription>
            </Alert>
          )}
          <DataTable
            columns={effectiveColumns}
            data={effective ? effectiveRows(effective) : []}
            caption="Effective settings"
            loading={loading}
            empty="No effective settings."
            testId="effective-settings"
          />
        </>
      )}
    </Card>
  )
}

export function RepositoryPage() {
  const { id = '' } = useParams()
  const [status, setStatus] = useState<ReactNode | null>(null)
  const [saved, setSaved] = useState<ManagedRepository | null>(null)

  const loadRepo = useCallback(() => api.repository(id), [id])
  const { data: loaded, error } = useLoad(loadRepo)
  const { data: global } = useLoad(api.settings)
  const loadEffective = useCallback(() => api.effectiveSettings(id), [id])
  const { data: effective, error: effectiveError, loading: effectiveLoading, reload: reloadEffective } = useLoad(loadEffective)
  const { runNow, pending } = useRunNow(setStatus)

  const repo = saved?.id === id ? saved : (loaded ?? null)
  useTitle(repo ? `${repo.name} · AutoRemediator` : 'Repository · AutoRemediator')

  const notFound = error instanceof ApiError && error.status === 404
  const noteId = 'repo-not-scheduled-note'

  return (
    <>
      <div className="mb-4 flex items-center">
        <Button variant="ghost" asChild>
          <Link to="/configuration">
            <ArrowLeft /> Configuration
          </Link>
        </Button>
      </div>

      {status !== null && (
        <Alert variant="info" role="status" className="mb-6" data-testid="repo-status">
          <AlertDescription>{status}</AlertDescription>
        </Alert>
      )}

      <PageHeader title={repo?.name ?? 'Repository'} subtitle={repo ? `${repo.organization}/${repo.project}/${repo.name}` : undefined}>
        {repo && (
          <div className="flex flex-wrap items-center gap-3">
            {!repo.enabled && (
              <span id={noteId} className="text-sm text-muted-foreground" data-testid="repo-not-scheduled">
                Disabled: not scheduled. Run now still works.
              </span>
            )}
            <Button
              variant="outline"
              disabled={pending.has(repo.id)}
              aria-describedby={repo.enabled ? undefined : noteId}
              onClick={() => void runNow(repo)}
              data-testid="run-now"
            >
              Run now
            </Button>
          </div>
        )}
      </PageHeader>

      {error !== undefined && repo === null ? (
        <Alert variant="warning" data-testid="repo-error">
          <AlertDescription>{notFound ? 'Repository not found.' : `Failed to load repository: ${errorMessage(error)}`}</AlertDescription>
        </Alert>
      ) : repo === null ? (
        <p className="italic" role="status">
          Loading…
        </p>
      ) : (
        <>
          <RepositoryForm key={`repo-${repo.id}`} repo={repo} onSaved={setSaved} onMessage={setStatus} />
          <SettingsForm
            key={`settings-${repo.id}`}
            repoId={repo.id}
            initial={repo.settings ?? noSettings}
            global={global}
            onSaved={(settings) => {
              setSaved({ ...repo, settings })
              reloadEffective()
            }}
            onMessage={setStatus}
          />
          <EffectiveCard effective={effective} error={effectiveError} loading={effectiveLoading} />
        </>
      )}
    </>
  )
}

