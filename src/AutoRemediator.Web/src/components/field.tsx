import type { ReactNode } from 'react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { describedBy } from '@/lib/form'

/** Help text and a field-level server error, linked to their control through `describedBy`. */
export function FieldNotes({ id, name, help, error }: { id: string; name: string; help?: ReactNode; error?: string }) {
  return (
    <>
      {error && (
        <p id={`${id}-error`} className="text-sm text-destructive" data-testid={`error-${name}`}>
          <span className="sr-only">Error: </span>
          {error}
        </p>
      )}
      {help && (
        <p id={`${id}-help`} className="text-label text-muted-foreground">
          {help}
        </p>
      )}
    </>
  )
}

interface FieldProps {
  id: string
  label: string
  value: string
  onChange: (v: string) => void
  placeholder?: string
  testId?: string
  /** Server field key, used for `data-testid="error-<name>"`. Defaults to the id. */
  name?: string
  help?: ReactNode
  error?: string
}

/** A labelled text input; a server error sets aria-invalid and is announced with the field. */
export function Field({ id, label, value, onChange, placeholder, testId, name = id, help, error }: FieldProps) {
  return (
    <div className="grid min-w-36 flex-1 gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, { help, error })}
        data-testid={testId}
      />
      <FieldNotes id={id} name={name} help={help} error={error} />
    </div>
  )
}

/** Lists every field error in one live region so they are announced together. */
export function ErrorSummary({ errors, labels, testId }: { errors: Record<string, string>; labels: Record<string, string>; testId: string }) {
  const entries = Object.entries(errors)
  if (entries.length === 0) return null
  return (
    <Alert variant="destructive" role="alert" data-testid={testId}>
      <AlertTitle>Not saved: fix {entries.length === 1 ? 'this field' : 'these fields'}</AlertTitle>
      <AlertDescription>
        <ul className="list-disc pl-4">
          {entries.map(([field, message]) => (
            <li key={field}>
              {labels[field] ?? field}: {message}
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  )
}
