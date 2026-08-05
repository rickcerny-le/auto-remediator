## Why

Slice 1 gives visibility (which `Orion180.*` packages are outdated across configured repos). Slice 2 turns that into action: for each configured repo, bump the matched outdated packages to their latest version and open a pull request. This is the first time the tool changes anything — the first automated PR — and it makes the remediation worker do real work instead of logging a placeholder.

## What Changes

- **Update execution** — for each enabled repo, compute the set of version bumps for matched outdated packages (reusing Slice-1 analysis) and produce the edited manifest contents (`Directory.Packages.props` / `*.csproj`) with those packages set to their latest version. No local build: validation is delegated to the repository's own Azure DevOps CI on the resulting PR (per the chosen build model).
- **Pull-request authoring** — push the edited manifests to a deterministic per-repo branch (`autoremediator/dependency-updates`) via the Azure DevOps push API, and open a pull request into the repo's target branch summarizing the updates. Idempotent: re-running the same repo refreshes that one branch/PR rather than opening duplicates.
- **Run orchestration** — the remediation worker consumes `RemediationRunRequested` and drives a per-run state machine (Reading → Analyzing → Applying → CreatingPR → Completed | NoUpdates | Failed), persisting run history (status, the updates applied, the PR link, and any error) to Table Storage.
- **Azure DevOps connectivity (extended)** — add the write operations needed for the above (push a commit of changed files to a branch; create or find a pull request) to the existing read-only client. Still no clone and no local build.

Scope boundary: **no AI, no local build, no collateral bumps.** Only matched (`Orion180.*`) outdated packages are bumped. If the PR's ADO CI is red, that surfaces on the PR — fixing breaks (collateral bumps, the AI loop) is Slice 3/5. Run errors (read/push/PR failures) mark the run `Failed` with no PR; nothing matched marks it `NoUpdates`.

## Capabilities

### New Capabilities
- `update-execution`: Computing the version bumps for matched outdated packages and producing the edited manifest contents (validation delegated to the repo's ADO CI, no local build).
- `pull-request-authoring`: Pushing bumped manifests to a per-repo branch and opening/refreshing a single pull request, idempotently.
- `run-orchestration`: The per-run state machine and persisted run history driven by the remediation worker.

### Modified Capabilities
- `azure-devops-connectivity`: Extend the client from read-only to include the write operations required for remediation (push a commit to a branch, create/find a pull request).

## Impact

- Makes `AutoRemediator.Worker.Remediation` orchestrate a real run (replacing the placeholder agent call for this slice); the `IRemediationAgent` placeholder stays for Slice 5.
- New Domain: `RemediationRun` + `DependencyUpdate` + run status, and a `ManifestEditor` (set a package's version in manifest XML). New Infrastructure: a run-history store (Table) and a `RemediationRunner` orchestrator; ADO client write methods (REST pushes + pull requests).
- No container image change — the remediation job stays on the runtime base (no SDK/git, no clone) because building is delegated to ADO CI.
- Requires the PAT to also carry write scopes (Code: Read & Write, Pull Request contribute) — a documentation update to the Slice-1 Key Vault/PAT note.
- No changes to the API or UI in this slice (run-history UI is Slice 6).
