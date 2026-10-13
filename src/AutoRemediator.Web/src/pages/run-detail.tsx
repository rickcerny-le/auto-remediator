import { useCallback, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ArrowLeft, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from '@/components/ui/accordion'
import { DataTable } from '@/components/data-table'
import { columnHelper } from '@/lib/table'
import { Pill } from '@/components/pill'
import { RunStatus } from '@/components/run-cells'
import { api, ApiError, errorMessage } from '@/lib/api'
import { formatDate, location, plural } from '@/lib/format'
import { verificationLabel, verificationTone } from '@/lib/status'
import type { ReviewCommand, RunDiagnostic, RunUpdate } from '@/lib/types'
import { useLoad, useTitle } from '@/hooks'

const diag = columnHelper<RunDiagnostic>()
const diagnosticColumns = diag.columns([
  diag.accessor('code', { header: 'Code', cell: (c) => <span className="font-mono">{c.getValue()}</span> }),
  diag.display({ id: 'location', header: 'Location', cell: (c) => <span className="font-mono">{location(c.row.original)}</span> }),
  diag.accessor('message', { header: 'Message', meta: { className: 'whitespace-normal' } }),
])

const upd = columnHelper<RunUpdate>()
const updateColumns = upd.columns([
  upd.accessor('packageId', { header: 'Package', cell: (c) => <span className="font-mono">{c.getValue()}</span> }),
  upd.accessor('fromVersion', { header: 'From', cell: (c) => <span className="font-mono text-muted-foreground">{c.getValue()}</span> }),
  upd.accessor('toVersion', { header: 'To', cell: (c) => <span className="font-mono text-success">{c.getValue()}</span> }),
  upd.display({
    id: 'kind',
    header: 'Kind',
    cell: ({ row: { original: u } }) => (
      <>
        <Pill tone={u.kind === 'Collateral' ? 'info' : 'ok'}>{u.kind.toLowerCase()}</Pill>
        {u.beyondPolicy && <span className="ar-tag">beyond policy</span>}
      </>
    ),
  }),
])

function Section({ title, badge, link, children, testId }: { title: string; badge?: ReactNode; link?: ReactNode; children: ReactNode; testId?: string }) {
  return (
    <Card className="mb-4 gap-2 p-4" data-testid={testId}>
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="font-semibold">{title}</h2>
        {badge}
        <div className="flex-1" />
        {link}
      </div>
      {children}
    </Card>
  )
}

function ApiLink({ href, testId, children }: { href: string; testId: string; children: ReactNode }) {
  // The API returns these as relative /api URLs, which resolve against this origin.
  return (
    <a href={href} target="_blank" rel="noreferrer" className="font-mono text-sm text-info hover:underline" data-testid={testId}>
      {children}
    </a>
  )
}

function commandResult(command: ReviewCommand, status: number): string {
  switch (status) {
    case 202:
      return `${command[0].toUpperCase()}${command.slice(1)} requested. The worker will process it shortly — refresh to see the result.`
    case 409:
      return 'This run has already moved on; the command was not applied.'
    default:
      return `Could not send the command (${status}).`
  }
}

export function RunDetailPage() {
  useTitle('Run detail · AutoRemediator')
  const { id = '' } = useParams()
  const [commandInFlight, setCommandInFlight] = useState(false)
  const [commandMessage, setCommandMessage] = useState<string | null>(null)

  const load = useCallback(async () => {
    const run = await api.run(id)
    const proposal = run.proposalUrl ? await api.proposal(run.proposalUrl) : null
    return { run, proposal }
  }, [id])
  const { data, error, loading, reload } = useLoad(load)
  const run = data?.run ?? null
  const proposal = data?.proposal ?? null

  // Presents the result as a push *requested*, not completed: the worker executes it, and the run
  // record is the authority for what happened (FR-014, SC-007).
  const send = async (command: ReviewCommand) => {
    setCommandInFlight(true)
    setCommandMessage(null)
    try {
      setCommandMessage(commandResult(command, await api.review(id, command)))
    } catch (e) {
      setCommandMessage(`Failed to send the command: ${errorMessage(e)}`)
    } finally {
      setCommandInFlight(false)
    }
    reload()
  }

  return (
    <>
      <div className="mb-4 flex items-center">
        <Button variant="ghost" asChild>
          <Link to="/runs">
            <ArrowLeft /> Runs
          </Link>
        </Button>
        <div className="flex-1" />
        <Button variant="outline" onClick={reload} disabled={loading}>
          <RefreshCw /> Refresh
        </Button>
      </div>

      {commandMessage !== null && (
        <Alert variant="info" className="mb-4" data-testid="review-command-message">
          <AlertDescription>{commandMessage}</AlertDescription>
        </Alert>
      )}

      {error !== undefined ? (
        <Alert variant="warning" data-testid="run-error">
          <AlertDescription>
            {error instanceof ApiError && error.status === 404 ? 'Run not found.' : `Failed to load run: ${errorMessage(error)}`}
          </AlertDescription>
        </Alert>
      ) : run === null ? (
        <p className="italic">Loading…</p>
      ) : (
        <>
          <Card className="mb-4 gap-1 p-4" data-testid="run-header">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="font-mono text-xl font-medium">{run.repositorySlug}</h1>
              <RunStatus status={run.status} />
            </div>
            <p className="text-sm">
              Started: {formatDate(run.startedAtUtc)}
              {run.finishedAtUtc && ` · Finished: ${formatDate(run.finishedAtUtc)}`}
            </p>
            {run.pullRequestUrl && (
              <a href={run.pullRequestUrl} target="_blank" rel="noreferrer" className="text-sm text-info hover:underline" data-testid="run-pr-link">
                Pull request
              </a>
            )}
            {run.error && (
              <Alert variant="destructive" className="mt-2" data-testid="run-error-message">
                <AlertDescription>{run.error}</AlertDescription>
              </Alert>
            )}
          </Card>

          {run.status === 'AwaitingReview' && (
            <Section title="Held for review" badge={<Pill tone="warn">awaiting a decision</Pill>} testId="review-panel">
              <p className="mb-2 text-sm">
                An AI agent repaired a compile break in this change. Nothing has been pushed and no pull request exists
                yet — review the edits below before deciding.
              </p>

              {run.reviewNote && (
                <Alert variant="warning" className="mb-2" data-testid="review-note">
                  <AlertDescription>
                    <p>{run.reviewNote}</p>
                    <Button
                      size="sm"
                      variant="outline"
                      className="mt-2 border-warning text-warning"
                      onClick={() => void send('rebuild')}
                      disabled={commandInFlight}
                      data-testid="rebuild-button"
                    >
                      Rebuild
                    </Button>
                  </AlertDescription>
                </Alert>
              )}

              {proposal === null ? (
                <p className="text-sm italic">Loading proposal…</p>
              ) : (
                <>
                  <p className="text-sm">
                    Verified <span className="font-mono">{formatDate(proposal.verifiedAtUtc)}</span> against base commit{' '}
                    <span className="font-mono">{proposal.baseCommitId.slice(0, 8)}</span> over {plural(proposal.attempts, 'attempt')}.{' '}
                    {proposal.transcriptUrl && (
                      <ApiLink href={proposal.transcriptUrl} testId="proposal-transcript-link">
                        transcript
                      </ApiLink>
                    )}
                  </p>

                  {proposal.provokingDiagnostics.length > 0 && (
                    <DataTable columns={diagnosticColumns} data={proposal.provokingDiagnostics} testId="proposal-diagnostics" className="mb-2" />
                  )}

                  <Accordion type="multiple" defaultValue={proposal.files.map((f) => f.path)} className="rounded-md border px-4">
                    {proposal.files.map((file) => (
                      <AccordionItem key={file.path} value={file.path} data-testid="proposal-file">
                        <AccordionTrigger className="font-mono">{`${file.origin}: ${file.path}`}</AccordionTrigger>
                        <AccordionContent className="flex flex-wrap gap-3">
                          <div className="min-w-[280px] flex-1">
                            <div className="text-xs text-muted-foreground">Before</div>
                            <pre className="overflow-x-auto rounded bg-muted p-2 font-mono text-xs whitespace-pre-wrap">
                              {file.originalContent ?? '(file did not exist)'}
                            </pre>
                          </div>
                          <div className="min-w-[280px] flex-1">
                            <div className="text-xs text-muted-foreground">After</div>
                            <pre className="overflow-x-auto rounded bg-muted p-2 font-mono text-xs whitespace-pre-wrap">{file.newContent}</pre>
                          </div>
                        </AccordionContent>
                      </AccordionItem>
                    ))}
                  </Accordion>
                </>
              )}

              <div className="mt-3 flex gap-2">
                <Button className="bg-success text-primary-foreground hover:bg-success/90" onClick={() => void send('approve')} disabled={commandInFlight} data-testid="approve-button">
                  Approve
                </Button>
                <Button variant="outline" className="border-primary text-primary" onClick={() => void send('retry')} disabled={commandInFlight} data-testid="retry-button">
                  Retry
                </Button>
                <Button variant="outline" className="border-destructive text-destructive" onClick={() => void send('discard')} disabled={commandInFlight} data-testid="discard-button">
                  Discard
                </Button>
              </div>
            </Section>
          )}

          {run.remediation && (
            <Section
              title="AI remediation"
              badge={<Pill tone="info">{plural(run.remediation.attempts, 'attempt')}</Pill>}
              link={
                run.remediation.transcriptUrl && (
                  <ApiLink href={run.remediation.transcriptUrl} testId="remediation-transcript-link">
                    transcript
                  </ApiLink>
                )
              }
              testId="run-remediation"
            >
              <p className="text-sm">
                A compile break in this run was handed to an AI agent, which proposed source edits that were re-verified.{' '}
                {run.pullRequestUrl === null
                  ? 'The attempts did not produce a building tree, so no pull request was opened.'
                  : 'The pull request therefore contains AI-authored source edits.'}
              </p>
            </Section>
          )}

          {run.verification && (
            <Section
              title="Local verification"
              badge={<Pill tone={verificationTone(run.verification.classification)}>{verificationLabel(run.verification.classification)}</Pill>}
              link={
                run.verification.logUrl && (
                  <ApiLink href={run.verification.logUrl} testId="verification-log-link">
                    full log
                  </ApiLink>
                )
              }
              testId="run-verification"
            >
              {run.verification.classification === 'Skipped' && (
                <Alert variant="warning" data-testid="verification-skip-reason">
                  <AlertDescription>Verification was skipped, so this change was not compiled: {run.verification.skipReason}</AlertDescription>
                </Alert>
              )}
              {run.verification.classification === 'Verified' && (
                <p className="text-sm">
                  <code>dotnet restore</code> and <code>dotnet build</code> both succeeded. Tests are run by the repository's own CI.
                </p>
              )}
              {run.verification.diagnostics.length > 0 && (
                <>
                  <DataTable columns={diagnosticColumns} data={run.verification.diagnostics} testId="verification-diagnostics" />
                  {/* The run record keeps a bounded set of diagnostics; the log always has them all. */}
                  <p className="text-xs text-muted-foreground" data-testid="verification-diagnostics-note">
                    A capped set of diagnostics is kept on the run — the full log has every one.
                  </p>
                </>
              )}
            </Section>
          )}

          <Card className="py-0">
            <DataTable columns={updateColumns} data={run.updates} empty="No updates in this run." testId="run-updates" />
          </Card>
        </>
      )}
    </>
  )
}
