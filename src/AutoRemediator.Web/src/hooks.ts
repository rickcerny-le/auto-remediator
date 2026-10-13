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

export type ThemeMode = 'light' | 'dark' | 'system'

const THEME_QUERY = '(prefers-color-scheme: dark)'
const themeOrder: ThemeMode[] = ['system', 'light', 'dark']

function readThemeMode(): ThemeMode {
  const stored = localStorage.getItem('theme')
  return stored === 'light' || stored === 'dark' ? stored : 'system'
}

/**
 * Theme mode (`light | dark | system`, default `system`) persisted in `localStorage.theme`.
 * `system` follows the OS scheme live. The same resolution runs in the pre-paint script in index.html.
 * `cycle()` advances system -> light -> dark -> system.
 */
export function useTheme() {
  const [mode, setMode] = useState<ThemeMode>(readThemeMode)

  useEffect(() => {
    const query = window.matchMedia(THEME_QUERY)
    const root = document.documentElement
    const apply = () => {
      const dark = mode === 'dark' || (mode === 'system' && query.matches)
      root.classList.toggle('dark', dark)
      root.style.colorScheme = dark ? 'dark' : 'light'
    }
    apply()
    if (mode === 'system') {
      localStorage.removeItem('theme')
      query.addEventListener('change', apply)
      return () => query.removeEventListener('change', apply)
    }
    localStorage.setItem('theme', mode)
  }, [mode])

  const cycle = useCallback(() => setMode((m) => themeOrder[(themeOrder.indexOf(m) + 1) % themeOrder.length]), [])

  return { mode, cycle }
}
