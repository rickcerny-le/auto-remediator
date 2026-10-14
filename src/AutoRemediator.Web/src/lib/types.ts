// Wire shapes of the API, mirrored by hand from src/AutoRemediator.Contracts/Dtos.
// Dates arrive as ISO-8601 strings (DateTimeOffset).

export interface ManagedRepository {
  id: string
  organization: string
  project: string
  name: string
  enabled: boolean
  targetBranch: string
}

export interface ManagedRepositoryInput {
  organization: string
  project: string
  name: string
  enabled: boolean
  targetBranch: string
}

/** Body of a 202 from POST /api/repositories/{id}/run. */
export interface RunNowAccepted {
  runId: string
  enabled: boolean
}

/** Body of a 409 from POST /api/repositories/{id}/run. */
export interface RunNowRefused {
  reason: string
  heldRunId?: string
  message: string
}

export interface RunNowResult {
  status: number
  body: Partial<RunNowAccepted> & Partial<RunNowRefused>
}

export interface TargetingSettings {
  patterns: string[]
  excludes: string[]
  feeds: string[]
  strategy: string
  ignore: string[]
  allowPrerelease: boolean
}

export interface DependencyMapEntry {
  repositorySlug: string
  packageId: string
  currentVersion: string | null
  latestVersion: string | null
  status: string
}

export interface DependencyMap {
  entries: DependencyMapEntry[]
}

export interface RunSummary {
  id: string
  repositorySlug: string
  status: string
  startedAtUtc: string
  finishedAtUtc: string | null
  updateCount: number
  pullRequestUrl: string | null
  trigger: 'Scheduled' | 'Manual'
}

export interface RunUpdate {
  packageId: string
  fromVersion: string
  toVersion: string
  kind: string
  beyondPolicy: boolean
}

export interface RunDiagnostic {
  code: string
  message: string
  path: string | null
  line: number | null
  column: number | null
}

export interface RunVerification {
  classification: string
  skipReason: string | null
  diagnostics: RunDiagnostic[]
  logUrl: string | null
}

export interface RunRemediation {
  attempts: number
  transcriptUrl: string | null
}

export interface RunDetail {
  id: string
  repositorySlug: string
  status: string
  startedAtUtc: string
  finishedAtUtc: string | null
  pullRequestUrl: string | null
  error: string | null
  updates: RunUpdate[]
  verification: RunVerification | null
  remediation: RunRemediation | null
  proposalUrl: string | null
  reviewNote: string | null
  /** Present only in local repository mode, when the run has a pull request. */
  pullRequestDiffUrl: string | null
  trigger: 'Scheduled' | 'Manual'
}

export interface ProposedFile {
  path: string
  originalContent: string | null
  newContent: string
  origin: string
}

export interface ChangeProposal {
  runId: string
  repositorySlug: string
  baseCommitId: string
  verifiedAtUtc: string
  attempts: number
  transcriptUrl: string | null
  reviewNote: string | null
  reviewCommandCount: number
  files: ProposedFile[]
  provokingDiagnostics: RunDiagnostic[]
}

export interface PullRequestDiffFile {
  path: string
  changeKind: 'Added' | 'Modified' | 'Deleted'
  originalContent: string | null
  newContent: string | null
  isBinary: boolean
  isTruncated: boolean
}

export interface PullRequestDiff {
  sourceBranch: string
  targetBranch: string
  baseCommitId: string
  headCommitId: string
  files: PullRequestDiffFile[]
}

export type ReviewCommand = 'approve' | 'rebuild' | 'retry' | 'discard'
