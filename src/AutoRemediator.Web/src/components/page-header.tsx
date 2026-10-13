import type { ReactNode } from 'react'

export function PageHeader({ title, subtitle, children }: { title: string; subtitle?: string; children?: ReactNode }) {
  return (
    <div className="mb-5 flex flex-wrap items-center gap-2">
      <div className="mr-auto">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {subtitle && <p className="font-mono text-xs text-muted-foreground">{subtitle}</p>}
      </div>
      {children}
    </div>
  )
}
