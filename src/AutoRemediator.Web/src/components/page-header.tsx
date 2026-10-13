import { useEffect, useRef, type ReactNode } from 'react'
import { useLocation } from 'react-router'
import { cn } from '@/lib/utils'

// Pages mount their own PageHeader, so the previous pathname lives at module level.
// null until the first header mounts: the initial load keeps focus on the skip link.
let lastPathname: string | null = null

export function PageHeader({ title, subtitle, titleClassName, children }: { title: string; subtitle?: string; titleClassName?: string; children?: ReactNode }) {
  const { pathname } = useLocation()
  const ref = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    if (lastPathname !== null && lastPathname !== pathname) ref.current?.focus()
    lastPathname = pathname
  }, [pathname])

  return (
    <div className="mb-6 flex flex-wrap items-center gap-2">
      <div className="mr-auto">
        <h1 ref={ref} tabIndex={-1} className={cn('text-2xl font-semibold tracking-tight outline-none', titleClassName)}>
          {title}
        </h1>
        {subtitle && <p className="text-label text-muted-foreground">{subtitle}</p>}
      </div>
      {children}
    </div>
  )
}
