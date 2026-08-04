## 1. Domain model & pure logic

- [x] 1.1 Expand `ManagedRepository` (add `TargetBranch`; keep org/project/name/enabled) and add a `TargetingSettings` aggregate (patterns[], excludes[], feeds[], policy: strategy + ignore[])
- [x] 1.2 Add a `PackagePatternMatcher` (case-insensitive package-id glob → match, honoring excludes) in Domain, pure/no-I/O
- [x] 1.3 Add a `ManifestParser` in Domain that extracts `{packageId, version}` from `Directory.Packages.props` (`PackageVersion`) and `*.csproj` (`PackageReference`) via `System.Xml.Linq`; unresolved/variable versions surface as "unknown"
- [x] 1.4 Add `DependencyMap` read-model types (repository × package × current × latest × status) in Domain, and a DTO mirror in `Contracts`

## 2. Persistence (Table Storage)

- [x] 2.1 Add `IManagedRepositoryStore` (+ Table impl) — CRUD over the `repositories` table via `ITableStore`
- [x] 2.2 Add `ITargetingSettingsStore` (+ Table impl) — get/put the singleton settings row in the `config` table (patterns/excludes/feeds/policy as JSON columns)
- [x] 2.3 Register both stores in `AddInfrastructure`

## 3. Azure DevOps connectivity (read-only)

- [x] 3.1 Use raw REST + a typed `HttpClient` (design changed from the official SDK, which pulled in SqlClient + a vulnerable transitive + a downgrade conflict)
- [x] 3.2 Replace `StubAzureDevOpsClient` with an `HttpClient`-backed `IAzureDevOpsClient`: repo existence check, read file text, and enumerate `Directory.Packages.props` + `*.csproj` on the target branch — read-only only
- [x] 3.3 Add an `AzureDevOpsOptions` (organization URL, PAT) bound from configuration; authenticate with PAT Basic auth
- [x] 3.4 Update `AddInfrastructure` registration to the real client (typed `HttpClient`)

## 4. Secrets / Key Vault configuration source

- [x] 4.1 Add a Key Vault configuration source: when `KeyVaultUri` is set, `AddAzureKeyVault(uri, DefaultAzureCredential)` so `AzureDevOps--Pat` surfaces as `AzureDevOps:Pat` (ServiceDefaults extension, called by Api + workers)
- [x] 4.2 Document/enable local user-secrets for `AzureDevOps:Pat` and `AzureDevOps:OrganizationUrl` (no Key Vault locally) — README

## 5. Feed version resolution

- [x] 5.1 Add `NuGet.Protocol` to `Infrastructure`
- [x] 5.2 Add `IFeedVersionResolver` (+ impl): `SourceRepository` per feed with a `PackageSourceCredential` (PAT), return the highest stable `NuGetVersion` (exclude pre-release unless policy allows), per-feed failures swallowed
- [x] 5.3 Register the resolver in `AddInfrastructure`

## 6. Dependency analysis orchestration

- [x] 6.1 Add `DependencyMapService` that, per enabled repo: reads manifests (ADO) → parses → matches patterns → resolves latest (feed) → produces map rows with `outdated`/`up-to-date`/`unknown` status
- [x] 6.2 Aggregate per-repo results into the full `DependencyMap`

## 7. API (vertical slices)

- [x] 7.1 Replace the placeholder `Repositories/List` slice with configuration slices: list/create/update/delete managed repositories
- [x] 7.2 Add targeting-settings slices: get and update global settings (patterns/excludes/feeds/policy)
- [x] 7.3 Add a dependency-map slice: GET the aggregate map, returning the `Contracts` DTO

## 8. Web UI (Blazor)

- [x] 8.1 Add a Configuration page: manage repositories and targeting settings via the API
- [x] 8.2 Add a read-only Dependency Map page: table of repository × package × current × latest × status, with outdated rows highlighted
- [x] 8.3 Add nav entries for both pages

## 9. Infrastructure (Key Vault)

- [x] 9.1 Add a `modules/key-vault` Terraform module (RBAC-authorized Key Vault); the `AzureDevOps--Pat` secret value is NOT set by Terraform
- [x] 9.2 Assign the managed identity the `Key Vault Secrets User` role on the vault (identity module wiring)
- [x] 9.3 Inject `KeyVaultUri` into the Api/workers env in `environments/test/main.tf`; output the vault URI + name
- [x] 9.4 Document the one-time `az keyvault secret set` for the PAT in `infra/terraform/README.md`

## 10. Tests

- [x] 10.1 `ManifestParser` unit tests (CPM, .csproj attribute+element, variable/unknown version, malformed)
- [x] 10.2 `PackagePatternMatcher` unit tests (`Orion180.*` includes/excludes, case-insensitivity, empty includes)
- [x] 10.3 `DependencyMapService` tests with mocked ADO client + feed resolver (outdated / up-to-date / unknown)
- [x] 10.4 Config store round-trip tests against the Azurite emulator (skips when Azurite is not running)
- [x] 10.5 API integration tests for repositories CRUD and the dependency-map endpoint (services overridden via `WebApplicationFactory`)

## 11. Verification

- [x] 11.1 `dotnet build AutoRemediator.sln` — zero errors
- [x] 11.2 `dotnet test AutoRemediator.sln` — all tests pass (26 passed; Azurite round-trip + Playwright self-skip)
- [ ] 11.3 Run the AppHost and confirm the Configuration and Dependency Map pages load; with a PAT + a real configured repo, confirm the map renders matched `Orion180.*` packages *(not run — needs a container runtime + a real Azure DevOps PAT and repo; page-load can be checked via the AppHost on request)*
