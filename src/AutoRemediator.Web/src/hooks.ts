import { useCallback, useEffect, useState } from 'react'

export function useTitle(title: string) {
  useEffect(() => {
    document.title = title
  }, [title])
}

interface Loaded<T> {
  data?: T
  error?: unknown
  loading: boolean
  /** The `load` that produced this state; a different current `load` means a fetch is in flight. */
  source?: () => Promise<T>
}

/**
 * Runs `load` on mount, whenever it changes identity, and on `reload()`. A response that arrives
 * after a newer request started is dropped, so a slow earlier fetch can't overwrite a newer one.
 * Pass a memoized `load` (useCallback) or it re-runs on every render.
 */
export function useLoad<T>(load: () => Promise<T>) {
  const [state, setState] = useState<Loaded<T>>({ loading: true })
  const [version, setVersion] = useState(0)

  useEffect(() => {
    let current = true
    load().then(
      (data) => current && setState({ data, loading: false, source: load }),
      (error: unknown) => current && setState((s) => ({ ...s, error, loading: false, source: load })),
    )
    return () => {
      current = false
    }
  }, [load, version])

  const reload = useCallback(() => {
    setState((s) => ({ ...s, loading: true }))
    setVersion((v) => v + 1)
  }, [])

  return { data: state.data, error: state.error, loading: state.loading || state.source !== load, reload }
}
