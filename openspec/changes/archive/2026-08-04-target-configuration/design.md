## Context

Slice 1 of the application roadmap: turn the hard-coded stubs into real configuration + read-only visibility. Configure managed repos and package patterns (`Orion180.*`), read each repo's manifests from Azure DevOps, resolve latest versions from the Orion180 feed, and render an outdated-dependency map. No writes to Azure DevOps. Builds on the platform (Aspire, Storage, managed-identity auth, ACA/IaC, CI/CD).

Confirmed decisions: PAT-in-Key-Vault auth (user-secrets locally); parse Central Package Management (`Directory.Packages.props`) + `.csproj`; official Azure DevOps .NET SDK for reads.

## Goals / Non-Goals

**Goals:**
- Persist managed repositories + global targeting settings (patterns, excludes, feeds, policy) in Table Storage.
- Read-only ADO client (SDK) reading manifests; PAT from Key Vault / user-secrets.
- Parse manifests, match patterns, resolve latest from the feed, produce a dependency map.
- API slices + a Blazor configuration page and a read-only map page.
- Add a Key Vault to the IaC for the PAT.

**Non-Goals:**
- Any ADO write (branch/commit/PR), update application, AI loop — later slices.
- Package-publish-triggered fan-out (timer only).
- Persisting run history (no runs yet — this is the read/analyze path).
- Transitive/lock-file analysis (CPM + `.csproj` declared versions only this slice).

## Decisions

**1. Layering — keep pure logic in Domain, external clients in Infrastructure, orchestration in an API feature.**
- `Domain`: expanded `ManagedRepository`, a `TargetingSettings` aggregate, a `DependencyMap` read model, and pure services `ManifestParser` (XML → `{id,version}`) and `PackagePatternMatcher` (glob → match). No external deps.
- `Infrastructure`: `IAzureDevOpsClient` (real, SDK-backed, read-only), `IFeedVersionResolver` (NuGet.Protocol), and Table-Storage stores `IManagedRepositoryStore` / `ITargetingSettingsStore`.
- `Api`: a `DependencyMap` feature handler orchestrates read → parse → match → resolve → map. Configuration CRUD as feature slices.
Rationale: parsing/matching are deterministic and unit-testable without I/O; clients are mockable; the orchestration is reused by the worker in later slices. *Alternative:* a dedicated Application project — deferred; the vertical-slice API handler suffices now.

**2. Azure DevOps via raw REST + a typed `HttpClient`, behind our interface.**
Replace `StubAzureDevOpsClient` with an `HttpClient`-backed `IAzureDevOpsClient` calling the ADO REST API (`api-version=7.1`): `GET /_apis/git/repositories/{repo}` (existence), `GET /_apis/git/repositories/{repo}/items?path=...&includeContent=true` (file text), and the items list to find `Directory.Packages.props` + `*.csproj`. Auth is PAT via HTTP Basic (`":{pat}"` base64). The interface stays read-only; write methods arrive in Slice 2. Rationale chosen over the official SDK: the SDK (`Microsoft.TeamFoundationServer.Client`) transitively pulls `Microsoft.Data.SqlClient` (unused), causes a `System.Configuration.ConfigurationManager` downgrade (NU1605), and includes a package with a moderate security advisory — heavy for two read calls. Raw REST is tiny, image-friendly, and trivially mockable via a stubbed `HttpMessageHandler` or the interface. *Trade-off:* we hand-model the few responses we use instead of getting typed clients — acceptable for the read-only surface.

**3. PAT via a Key Vault configuration source.**
In Azure, register Key Vault as a configuration source (`AddAzureKeyVault(uri, DefaultAzureCredential)`) only when `KeyVaultUri` is configured; the secret `AzureDevOps--Pat` surfaces as config key `AzureDevOps:Pat`. Locally, user-secrets provides `AzureDevOps:Pat` and `AzureDevOps:OrganizationUrl`. The client reads plain config, unaware of the source. Rationale: keeps credential handling out of business code; reuses the managed identity already provisioned. *Alternative:* inject `SecretClient` directly — more coupling; rejected.

**4. Feed version resolution with NuGet.Protocol.**
`IFeedVersionResolver` builds a `SourceRepository` for the configured feed with a `PackageSourceCredential` (the PAT), and queries available versions (`FindPackageByIdResource` / metadata), returning the highest **stable** `NuGetVersion` (pre-release excluded unless policy allows). Results cached per analysis pass. Rationale: `NuGet.Protocol` speaks the ADO Artifacts v3 feed natively, including auth and version semantics. *Alternative:* raw ADO Artifacts REST — reimplements NuGet version ordering; rejected.

**5. Table Storage modeling.**
- `repositories` table: one entity per managed repo (`PartitionKey = "repo"`, `RowKey = id`), columns org/project/name/enabled/targetBranch.
- `config` table: a single `TargetingSettings` entity (`PartitionKey = "settings"`, `RowKey = "global"`) with patterns/excludes/feeds/policy serialized as JSON columns.
Accessed via the existing `ITableStore`. Rationale: tiny, low-write config; a singleton settings row is simplest. *Alternative:* a row per pattern — more rows, no benefit now.

**6. Dependency map is computed on demand (not persisted).**
The map is derived read-side from current config + live ADO/feed reads; no run/history persistence in this slice. Rationale: no runs exist yet; persisting run history is Slice 2's `run-orchestration`. *Trade-off:* each map view does live reads — fine for the small configured set; add caching later if needed.

**7. Key Vault in IaC — vault + access, value out-of-band.**
Add a `key-vault` Terraform module (RBAC-authorized Key Vault) + a Key Vault Secrets User role assignment for the managed identity, and inject `KeyVaultUri` into the apps/jobs. The `AzureDevOps--Pat` secret is created and set manually (`az keyvault secret set`), never in Terraform. Rationale: keeps the real PAT out of state/vars while provisioning everything else. *Alternative:* a `key_vault_secret` resource fed by a sensitive var — puts the PAT in state; rejected.

## Risks / Trade-offs

- **[App still targets connection strings vs MI]** → Not a concern here; this slice adds a *new* KV config source and reuses the existing endpoint+MI clients. The PAT is a secret by nature (external system), distinct from Azure-resource RBAC.
- **[ADO SDK size / transitive conflicts]** → `Microsoft.TeamFoundationServer.Client` pulls a broad graph. Mitigation: reference only in `Infrastructure`, behind the interface; watch for version conflicts at build.
- **[Feed auth in local dev]** → Resolving versions locally needs feed access. Mitigation: same PAT via user-secrets; tests mock `IFeedVersionResolver` so CI needs no feed.
- **[Manifest variety]** → Real repos use `$(Variables)`, imported props, conditionals. Mitigation: Slice 1 handles literal `Version`/`PackageVersion` values on declared entries; unresolved/variable versions are surfaced as "unknown" rather than failing the map. Deepen later.
- **[Read-only boundary leaking]** → Keep the ADO interface read-only so no accidental writes; write methods land deliberately in Slice 2.
- **[Key Vault requires a manual step]** → First deploy needs `az keyvault secret set` before analysis works in Azure. Mitigation: documented in the infra/pipeline README.

## Migration Plan

Additive to the running system. Replacing `StubAzureDevOpsClient` changes the DI registration; the API `List` slice evolves into configuration + map slices. No data migration (no prior persisted data). Rollback = revert the change; local dev unaffected (user-secrets). Key Vault addition applies via Terraform; the secret is set once.

## Open Questions

- Exact Azure DevOps SDK package/version to pin and whether the trimmed `Microsoft.VisualStudio.Services.Client` suffices for reads (resolve at apply).
- Whether targeting settings should be per-repo overridable now or stay global (starting **global**, with the model shaped to allow an override later).
- Caching strategy for feed lookups if the configured repo/package set grows (none now; per-pass cache only).
