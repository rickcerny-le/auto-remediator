import { useState, useSyncExternalStore } from 'react'
import { NavLink, Outlet } from 'react-router'
import { History, House, Menu, Monitor, Moon, Package, Settings, Sun } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent } from '@/components/ui/sheet'
import { useTheme } from '@/hooks'
import { cn } from '@/lib/utils'

const nav = [
  { to: '/', label: 'Home', icon: House, end: true },
  { to: '/runs', label: 'Runs', icon: History, end: false },
  { to: '/dependency-map', label: 'Dependency Map', icon: Package, end: false },
  { to: '/configuration', label: 'Configuration', icon: Settings, end: false },
]

const themeIcon = { system: Monitor, light: Sun, dark: Moon }

const wideQuery = '(min-width: 768px)'
const subscribeWide = (cb: () => void) => {
  const mql = window.matchMedia(wideQuery)
  mql.addEventListener('change', cb)
  return () => mql.removeEventListener('change', cb)
}
const useIsWide = () => useSyncExternalStore(subscribeWide, () => window.matchMedia(wideQuery).matches)

/** GitHub-style app shell: top bar with brand and theme toggle, collapsible side navigation (drawer below md). */
export function AppShell() {
  const { mode, cycle } = useTheme()
  const ThemeIcon = themeIcon[mode]
  const wide = useIsWide()
  // Desktop sidebar collapse and the mobile modal drawer are independent states.
  const [sidebarOpen, setSidebarOpen] = useState(true)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const expanded = wide ? sidebarOpen : drawerOpen

  const links = (
    <nav id="primary-nav" aria-label="Primary" className="py-0" data-testid="nav">
      {nav.map(({ to, label, icon: Icon, end }) => (
        <NavLink
          key={to}
          to={to}
          end={end}
          onClick={() => setDrawerOpen(false)}
          className="flex items-center gap-3 px-4 py-2 text-sm text-foreground/80 hover:bg-accent aria-[current=page]:bg-primary/10 aria-[current=page]:font-semibold aria-[current=page]:text-foreground aria-[current=page]:shadow-[inset_3px_0_0_var(--primary)]"
        >
          <Icon aria-hidden="true" className="size-4 text-muted-foreground" />
          {label}
        </NavLink>
      ))}
    </nav>
  )

  return (
    <div className="min-h-screen">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-sm focus:font-semibold focus:text-primary-foreground"
      >
        Skip to main content
      </a>
      <header className="fixed inset-x-0 top-0 z-20 flex h-12 items-center gap-2 border-b bg-header px-3">
        <Button
          variant="ghost"
          size="icon"
          aria-label="Toggle navigation"
          aria-expanded={expanded}
          aria-controls="primary-nav"
          onClick={() => (wide ? setSidebarOpen((o) => !o) : setDrawerOpen((o) => !o))}
        >
          <Menu aria-hidden="true" />
        </Button>
        <span
          aria-hidden="true"
          className="mx-1 grid size-[26px] place-items-center rounded-[7px] bg-primary text-sm font-extrabold text-primary-foreground"
        >
          ◆
        </span>
        <span className="text-lg font-semibold tracking-tight">AutoRemediator</span>
        <span className="ml-2 rounded-full px-2 font-mono text-xs text-muted-foreground">test</span>
        <div className="flex-1" />
        <Button
          variant="ghost"
          size="icon"
          title={`Theme: ${mode}`}
          aria-label={`Theme: ${mode}`}
          data-testid="theme-toggle"
          onClick={cycle}
        >
          <ThemeIcon aria-hidden="true" />
        </Button>
      </header>

      {wide ? (
        sidebarOpen && (
          <div className="fixed top-12 bottom-0 left-0 z-10 w-62 border-r bg-sidebar py-2">{links}</div>
        )
      ) : (
        <Sheet open={drawerOpen} onOpenChange={setDrawerOpen}>
          <SheetContent title="Navigation">{links}</SheetContent>
        </Sheet>
      )}

      <main id="main" tabIndex={-1} className={cn('px-6 pt-16 pb-8 outline-none', wide && sidebarOpen && 'md:pl-68')}>
        <Outlet />
      </main>
    </div>
  )
}
