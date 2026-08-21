# Design: in-app review gate

## Context

The remediation loop runs entirely inside one `RemediationRunner.RunAsync` call. The materialized tree lives in a `using var session`, and every path out of the method deletes it. That is not incidental — `local-verification` requires unconditional cleanup on every exit path, including success, exhaustion, error and cancellation.

A human review gate inserts wall-clock human time into the middle of that call. Something has to survive it.

## Decision 1: persist the change set, not the tree

Three shapes were considered.

| | A. Park the tree | B. Persist the change set | C. Re-materialize every time |
|---|---|---|---|
| Survives process restart | ✗ | ✓ | ✓ |
| Survives ACA scale-to-zero | ✗ | ✓ | ✓ |
| Disk cost | grows per parked proposal | negligible | none |
| Breaks unconditional cleanup | yes | no | no |
| Supports human file editing | naturally | poorly | poorly |

**Chosen: B, with C for rebuilds.**

The system is meant to be deployed, where the remediation worker is an event-driven Container Apps Job that scales to zero. A parked directory on a job replica that will not exist in ten minutes is not a design. That alone eliminates A.

The insight that makes B cheap is that the tree was never the artifact. Pushing needs only a list of `FileChange` and a base commit; `RemediationRunner` already assembles exactly that from three sources before calling `PushFilesAsync`. The tree is the machine that produces and validates the list, not the thing being delivered.

Because human editing is out of scope, every file in a change set is a pure function of the base commit, the update plan, and the recorded agent edits. There is no human state to preserve, so discarding the tree loses nothing that cannot be recomputed.

Change sets carry whole file contents and therefore live in blob storage, not Table Storage, whose entities cap at 1 MB and properties at 64 KB. This is the pattern already used for verification logs and the transcript: the payload in blob, a reference on the run record.

## Decision 2: `AwaitingReview` is a non-terminal resting state

Every current `RunStatus` is either in-flight within a single call (`Reading` … `CreatingPr`) or terminal (`Completed`, `NoUpdates`, `VerificationFailed`, `Failed`). `AwaitingReview` is neither: the run is parked, durable, and waiting on a person.

```
                     ┌──────────────────────────────────────────┐
   cron ──► RUN ─────┤ Analyzing → Applying → Verifying          │
                     │                 ↓                         │
                     │      [rejected + CS diagnostics]          │
                     │                 ↓                         │
                     │            Remediating                    │
                     └──────┬───────────────────────┬────────────┘
                            │                       │
        agent repaired it   │                       │  exhausted
                            ▼                       ▼
                  ╔═══════════════════╗    ┌────────────────────┐
                  ║  AwaitingReview   ║    │ VerificationFailed │  terminal,
                  ║  (durable)        ║    │ dev takes it from  │  unchanged
                  ╚═════════╤═════════╝    │ here               │
                            │              └────────────────────┘
       ┌──────────┬─────────┼─────────┬──────────┐
       ▼          ▼         ▼         ▼          ▼
   [Approve]  [Rebuild]  [Retry]  [Discard]  (held by scheduler)
       │          │         │         │
       ▼          └────┬────┘         ▼
  Pushing → PR         │          Discarded  ← new terminal
       │               ▼
       ▼        re-enqueue → worker re-materializes,
   Completed           verifies, re-parks

  verified without the agent ──────────────► Pushing → PR (today's path, untouched)
```

Only a **verified** change is ever parked. A parked proposal always builds, so review is about whether the repair is *right*, never whether it compiles. This keeps the reviewer's job well-defined and keeps the exhaustion path exactly as it is.

## Decision 3: the gate keys on agent involvement, not on policy

Gating every change would mean approving dozens of pure version bumps that restore and build cleanly and contain no judgment call. A gate that fires on everything is one people click through without looking, which is worse than no gate.

The condition is the same one the pull request description already uses to decide whether to disclose AI authorship, so no new notion of "interesting" enters the system. A run where `RemediationAttempts` is greater than zero and the change verified is parked; everything else pushes as it does today.

This has a deliberate consequence: **the mechanical bump path is untouched from end to end**, so this change carries no regression surface on the behaviour that already works.

## Decision 4: commands are messages, not HTTP handlers

The approve button cannot push from the API process, for the same reason verification does not run there: this work belongs to the worker, which already consumes a queue and already owns the Azure DevOps client and the SDK.

```
Web ──HTTP──► API ──message──► Worker (KEDA, scales from zero) ──► Azure DevOps
 ▲                                         │
 └──────────── run record (Tables) ◄───────┘
```

Approval is therefore **asynchronous**, and the UI must say so. The click reports that a push was requested; the run record reports when the pull request exists. Faking synchrony here would mean holding an HTTP request open across a push and a pull request creation.

`Rebuild` and `Retry` are the same shape: enqueue, let the worker re-materialize, re-verify and re-park.

## Decision 5: staleness fails loudly, and Rebuild is the remedy

`AzureDevOpsClient.PushFilesAsync` issues a compare-and-swap ref update:

```csharp
refUpdates = new[] { new { name = "refs/heads/" + branch, oldObjectId = baseCommitId } }
```

If the update branch moved while a proposal sat parked, Azure DevOps rejects the push. Combined with `changeType = "edit"`, a file deleted upstream also fails rather than being resurrected. Drift cannot silently revert someone's work.

So a stale approval is a normal, expected failure with an obvious remedy: rebuild the proposal against the current head and approve the refreshed one. The UI treats it that way rather than as an error.

The remaining staleness problem is honesty rather than correctness. The description claims the change was verified locally; that verification has a date and a commit. The proposal records both, and the review surface shows them, so a nine-day-old approval is not presented as a fresh one.

## Decision 6: the scheduler skips, and says so

A repository with a proposal awaiting review is skipped by scheduled runs. The alternative — letting a new run supersede the parked proposal — changes what a reviewer is looking at while they are looking at it.

Skipping introduces its own hazard, and it is the one this system has already ruled against once: the model was deliberately made a soft dependency because gating the worker on it stopped dependency updates entirely over a missing repair capability. A forgotten proposal holding a repository out of the schedule is the same failure with a different cause. The held state must therefore be **visible** — surfaced in the app as held-for-review, not merely absent from the runs feed.

## Open questions

- **How is a proposal keyed?** One active proposal per repository follows from the skip rule, but the run is the natural identity. Likely: the proposal is stored per run, with an index of the active proposal per repository.
- **What exactly does the reviewable diff contain?** `FileChange` is `(Path, Content)` — new content only. Rendering a diff needs the before-text, which means either storing it alongside the change set or reading it from Azure DevOps at view time. Storing it keeps review free of Azure DevOps calls, at the cost of duplicating file contents in blob.
- **Does `Retry` reuse the run's token budget or start a fresh one?** The budget is per run and the loop already spent it; a reviewer asking for another attempt is arguably a new run's worth of work.
- **Should approval re-verify?** Cheap insurance against the aging verification claim, at the cost of making the button slow and reintroducing a materialization on the approval path.
