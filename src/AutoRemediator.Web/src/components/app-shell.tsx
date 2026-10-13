import { useEffect, useState } from 'react'
import { NavLink, Outlet } from 'react-router'
import { History, House, Menu, Moon, Package, Settings, Sun } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const nav = [
  { to: '/', label: 'Home', icon: House, end: true },
  { to: '/runs', label: 'Runs', icon: History, end: false },
  { to: '/dependency-map', label: 'Dependency Map', icon: Package, end: false },
  { to: '/configuration', label: 'Configuration', icon: Settings, end: false },
]

const isWide = () => window.matchMedia('(min-width: 768px)').matches

function useDarkMode() {
  const [dark, setDark] = useState(() => localStorage.getItem('theme') !== 'light')
  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    localStorage.setItem('theme', dark ? 'dark' : 'light')
  }, [dark])
  return [dark, setDark] as const
}

/** GitHub-style app shell: top bar with brand and theme toggle, collapsible side navigation. */
export function AppShell() {
  const [dark, setDark] = useDarkMode()
  const [drawerOpen, setDrawerOpen] = useState(isWide)
  // Below md the drawer overlays the page, so it closes once a link is followed.
  const closeIfNarrow = () => !isWide() && setDrawerOpen(false)

  return (
    <div className="min-h-screen">
      <header className="fixed inset-x-0 top-0 z-20 flex h-14 items-center gap-2 border-b bg-header px-3">
        <Button variant="ghost" size="icon" aria-label="Toggle navigation" onClick={() => setDrawerOpen((o) => !o)}>
          <Menu />
        </Button>
        <span className="mx-1 grid size-[26px] place-items-center rounded-[7px] bg-primary text-sm font-extrabold text-[#0d1117]">
          ◆
        </span>
        <span className="text-lg font-semibold tracking-tight">AutoRemediator</span>
        <span className="ml-2 rounded-full px-2 font-mono text-xs text-muted-foreground">test</span>
        <div className="flex-1" />
        <Button
          variant="ghost"
          size="icon"
          title="Toggle light/dark"
          aria-label="Toggle light/dark"
          data-testid="theme-toggle"
          onClick={() => setDark((d) => !d)}
        >
          {dark ? <Sun /> : <Moon />}
        </Button>
      </header>

      {drawerOpen && (
        <nav className="fixed top-14 bottom-0 left-0 z-10 w-62 border-r bg-sidebar py-2" data-testid="nav">
          {nav.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              onClick={closeIfNarrow}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 px-4 py-2 text-sm text-foreground/80 hover:bg-accent',
                  isActive && 'bg-primary/10 font-semibold text-foreground shadow-[inset_3px_0_0_var(--primary)]',
                )
              }
            >
              <Icon className="size-4 text-muted-foreground" />
              {label}
            </NavLink>
          ))}
        </nav>
      )}

      <main className={cn('px-6 pt-20 pb-8', drawerOpen && 'md:pl-68')}>
        <Outlet />
      </main>
    </div>
  )
}
