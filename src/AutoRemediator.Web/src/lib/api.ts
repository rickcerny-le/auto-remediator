import type {
  ChangeProposal,
  DependencyMap,
  ManagedRepository,
  ManagedRepositoryInput,
  PullRequestDiff,
  EffectiveSettings,
  RepositorySettings,
  ReviewCommand,
  RunDetail,
  RunNowResult,
  RunSummary,
  TargetingSettings,
} from './types'

// The browser only ever calls same-origin relative /api URLs: the Vite dev server proxies them
// locally and nginx proxies them when deployed. The API returns its own links (log, transcript,
// proposal) in that same relative form, so they are usable as-is.

/** Seam for authentication (PRD §6.17): returns a bearer token to send, or nothing. */
export type TokenProvider = () => Promise<string | undefined>

let tokenProvider: TokenProvider = async () => undefined

export function setTokenProvider(provider: TokenProvider) {
  tokenProvider = provider
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers)
  const token = await tokenProvider()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  return fetch(path, { ...init, headers })
}

async function json<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await send(path, init)
  if (!response.ok) throw new ApiError(response.status, `${response.status} ${response.statusText}`)
  return (await response.json()) as T
}

function body(value: unknown): RequestInit {
  return { body: JSON.stringify(value), headers: { 'Content-Type': 'application/json' } }
}

export const api = {
  runs: (status?: string) =>
    json<RunSummary[]>(status ? `/api/runs?status=${encodeURIComponent(status)}` : '/api/runs'),
  run: (id: string) => json<RunDetail>(`/api/runs/${id}`),
  proposal: (url: string) => json<ChangeProposal>(url),
  pullRequestDiff: (url: string) => json<PullRequestDiff>(url),
  /** Returns the HTTP status: 202 accepted, 409 the run already moved on. */
  review: async (id: string, command: ReviewCommand) =>
    (await send(`/api/runs/${id}/review/${command}`, { method: 'POST' })).status,
  repositories: () => json<ManagedRepository[]>('/api/repositories'),
  repository: (id: string) => json<ManagedRepository>(`/api/repositories/${id}`),
  repositorySettings: (id: string) => json<RepositorySettings>(`/api/repositories/${id}/settings`),
  saveRepositorySettings: (id: string, settings: RepositorySettings) =>
    send(`/api/repositories/${id}/settings`, { method: 'PUT', ...body(settings) }),
  effectiveSettings: (id: string) => json<EffectiveSettings>(`/api/repositories/${id}/effective-settings`),
  addRepository: (input: ManagedRepositoryInput) => send('/api/repositories', { method: 'POST', ...body(input) }),
  updateRepository: (id: string, input: ManagedRepositoryInput) =>
    send(`/api/repositories/${id}`, { method: 'PUT', ...body(input) }),
  /** Queues a manual run. 202 { runId, enabled }; 409 { reason, heldRunId, message } while held. */
  runNow: async (id: string): Promise<RunNowResult> => {
    const response = await send(`/api/repositories/${id}/run`, { method: 'POST' })
    let parsed: RunNowResult['body'] = {}
    try {
      parsed = (await response.json()) as RunNowResult['body']
    } catch {
      // No JSON body (for example a 404 or a proxy error page): the status alone is reported.
    }
    return { status: response.status, body: parsed }
  },
  deleteRepository: (id: string) => send(`/api/repositories/${id}`, { method: 'DELETE' }),
  settings: () => json<TargetingSettings>('/api/settings'),
  saveSettings: (settings: TargetingSettings) => send('/api/settings', { method: 'PUT', ...body(settings) }),
  dependencyMap: () => json<DependencyMap>('/api/dependency-map'),
}

/**
 * Reads a 400 ValidationProblem body into the first message per field. The server keys are camelCase
 * field names (patterns, strategy, schedule, defaultSchedule, ...). Anything else yields an empty record.
 */
export async function readFieldErrors(response: Response): Promise<Record<string, string>> {
  try {
    const problem = (await response.json()) as { errors?: Record<string, string[] | string> }
    const result: Record<string, string> = {}
    for (const [field, messages] of Object.entries(problem.errors ?? {})) {
      const first = Array.isArray(messages) ? messages[0] : messages
      if (first) result[field] = first
    }
    return result
  } catch {
    return {}
  }
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
