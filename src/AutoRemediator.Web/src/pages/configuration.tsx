import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
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
const repoColumns = (onDelete: (id: string) => Promise<void>) =>
  col.columns([
    col.accessor('organization', { header: 'Organization' }),
    col.accessor('project', { header: 'Project' }),
    col.accessor('name', { header: 'Name' }),
    col.accessor('targetBranch', { header: 'Branch' }),
    col.accessor('enabled', { header: 'Enabled', cell: (c) => (c.getValue() ? 'yes' : 'no') }),
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
  const [status, setStatus] = useState<string | null>(null)
  const [repos, setRepos] = useState<ManagedRepository[]>([])

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

  const columns = useMemo(() => repoColumns(deleteRepo), [deleteRepo])

  const repoField = (key: keyof typeof emptyRepo, label: string) => (
    <Field id={`new-${key}`} label={label} value={newRepo[key]} onChange={(v) => setNewRepo((r) => ({ ...r, [key]: v }))} testId={`new-${key}`} />
  )

  return (
    <>
      <PageHeader title="Configuration" />

      {status !== null && (
        <Alert variant="info" className="mb-4" data-testid="config-status">
          <AlertDescription>{status}</AlertDescription>
        </Alert>
      )}

      <Card className="mb-6 max-w-[720px] p-5">
        <form className="grid gap-3" onSubmit={(e) => void saveSettings(e)}>
          <h2 className="text-lg font-semibold">Targeting settings</h2>
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

      <h2 className="mb-2 text-lg font-semibold">Managed repositories</h2>
      <Card className="mb-3 py-0">
        <DataTable columns={columns} data={repos} empty="No repositories configured." testId="repos-table" />
      </Card>

      <Card className="max-w-[900px] p-4">
        <form className="flex flex-wrap items-end gap-2" onSubmit={(e) => void addRepo(e)}>
          {repoField('organization', 'organization')}
          {repoField('project', 'project')}
          {repoField('name', 'repository')}
          {repoField('targetBranch', 'branch')}
          <Button type="submit" className="bg-success text-primary-foreground hover:bg-success/90" data-testid="add-repo">
            Add
          </Button>
        </form>
      </Card>
    </>
  )
}
