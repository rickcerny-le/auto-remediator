## Why

The tool has no real configuration or visibility: repositories and dependencies are hard-coded stubs. Slice 1 of the application roadmap delivers the first shippable value — **visibility**: configure the repositories to manage and the package patterns to target (e.g. `Orion180.*`), then see, across those repos, every matching package pin and whether it is outdated. This is the foundation every later slice (updates, PRs, AI remediation) builds on, and it is useful on its own as a dependency map.

## What Changes

- **Target configuration** — replace the `ManagedRepository` stub with a persisted configuration model: an explicit list of managed repositories (org/project/name, enabled, target branch) plus global targeting settings (package **patterns** like `Orion180.*` with excludes, the **feeds** that define "latest", and update **policy**), stored in Azure Table Storage. API vertical slices (CRUD) and a Blazor configuration page.
- **Azure DevOps connectivity** — replace `StubAzureDevOpsClient` with a real read-only client over the official Azure DevOps .NET SDK: validate configured repos exist and read their dependency manifests (`Directory.Packages.props`, `*.csproj`) from the default branch. Authenticated by a PAT sourced from **Key Vault** (via managed identity) in Azure and user-secrets locally.
- **Dependency analysis** — parse the manifests, match package IDs against the configured patterns, resolve the latest stable version of each matched package from the configured feed, and compute the outdated set. Expose the result as a **dependency map** (repo × package × current × latest × status) via the API and render it read-only in the UI.
- **Secrets (infra)** — add an Azure **Key Vault** to the test environment holding the Azure DevOps PAT, grant the managed identity read access, and wire the app to read it as a configuration source. The PAT value is set out-of-band (never in Terraform state).

Scope boundary: **read-only**. This slice performs no writes to Azure DevOps — no branches, commits, or pull requests. Update application, PR authoring, and the AI loop are later slices.

## Capabilities

### New Capabilities
- `target-configuration`: The persisted configuration model — managed repositories plus global package patterns, feeds, and policy — with API and UI to manage it.
- `azure-devops-connectivity`: An authenticated, read-only Azure DevOps client (list repos, read manifest files) with PAT-from-Key-Vault auth and local user-secrets.
- `dependency-analysis`: Parsing manifests, matching packages against patterns, resolving latest versions from the feed, and producing the outdated dependency map.

### Modified Capabilities
- `azure-test-environment`: Adds an Azure Key Vault to the footprint (holding the ADO PAT) and a Key Vault Secrets User role for the managed identity. Additive — external credentials such as a PAT genuinely require a secret store and do not contradict the existing RBAC-for-Azure-resources model.

## Impact

- Replaces the `ManagedRepository` placeholder and `StubAzureDevOpsClient`; adds Domain entities (target config, dependency map records), Infrastructure stores (Table Storage) and the ADO SDK client, Agents-independent analysis services, API feature slices, and a Blazor page.
- New dependencies: the Azure DevOps .NET SDK (`Microsoft.TeamFoundationServer.Client`), `NuGet.Protocol` (feed version resolution), `Azure.Security.KeyVault.Secrets` / Key Vault config provider, and `Azure.Data.Tables` usage for persistence.
- Infra: a Key Vault module added to `infra/terraform` (test environment) plus a role assignment; the PAT secret is populated manually.
- Local development uses user-secrets for the PAT and can run analysis against real Azure DevOps repos; tests mock the ADO client and feed.
