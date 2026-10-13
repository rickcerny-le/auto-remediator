export type Tone = 'ok' | 'warn' | 'bad' | 'info' | 'mute' | 'run'

export const runStatuses = [
  'Completed',
  'AwaitingReview',
  'NoUpdates',
  'VerificationFailed',
  'Failed',
  'Discarded',
  'SkippedHeld',
] as const

export function runStatusLabel(status: string): string {
  switch (status) {
    case 'NoUpdates':
      return 'No updates'
    case 'VerificationFailed':
      return 'Verification failed'
    case 'AwaitingReview':
      return 'Awaiting review'
    case 'SkippedHeld':
      return 'Held (skipped)'
    default:
      return status
  }
}

// VerificationFailed reads as a warning, not an error: the tool worked, the bump did not.
// AwaitingReview and SkippedHeld read as waiting on a person, not as finished or failed.
// Anything not terminal is still running.
export function runStatusTone(status: string): Tone {
  switch (status) {
    case 'Completed':
      return 'ok'
    case 'VerificationFailed':
    case 'AwaitingReview':
      return 'warn'
    case 'Failed':
      return 'bad'
    case 'NoUpdates':
    case 'SkippedHeld':
    case 'Discarded':
      return 'mute'
    default:
      return 'run'
  }
}

export function verificationLabel(classification: string): string {
  switch (classification) {
    case 'Verified':
      return 'verified'
    case 'DependencyFailure':
      return 'rejected'
    case 'Skipped':
      return 'not verified'
    default:
      return classification
  }
}

export function verificationTone(classification: string): Tone {
  switch (classification) {
    case 'Verified':
      return 'ok'
    case 'DependencyFailure':
      return 'warn'
    case 'Skipped':
      return 'mute'
    default:
      return 'info'
  }
}

export function dependencyTone(status: string): Tone {
  switch (status) {
    case 'Outdated':
      return 'warn'
    case 'UpToDate':
      return 'ok'
    case 'Ignored':
      return 'mute'
    default:
      return 'info'
  }
}
