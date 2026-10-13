import type { ReactNode } from 'react'
import type { Tone } from '@/lib/status'

export function Pill({ tone, children, testId }: { tone: Tone; children: ReactNode; testId?: string }) {
  return (
    <span className={`ar-pill ${tone}`} data-testid={testId}>
      <span className="ar-dot" />
      {children}
    </span>
  )
}
