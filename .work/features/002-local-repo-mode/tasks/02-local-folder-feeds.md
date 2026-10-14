# 02 Local folder feeds through resolver, dependency reader and NuGet.config

**Status:** done
**Feature:** 002-local-repo-mode
**Depends on:** none (can run in parallel with 01)

## What to do
Prove with tests that a feed given as an absolute local folder path, such as `C:\...\.local\feed` holding flat `.nupkg` files, works end to end through the existing NuGet code, both with and without a PAT configured. Change production code only if a test shows a failure.

1. Add a test helper `TestFeed` (in the test project). It writes minimal nuspec-only nupkgs to a folder. Each is a zip containing `<id>.nuspec`, with optional `<dependencies><group targetFramework=".NETStandard2.0">` entries that carry version ranges. No assemblies are needed. Tasks 03 and later reuse it.
2. `FeedVersionResolver` tests against a temp feed:
   - versions returned normalized
   - pre-release excluded unless `allowPrerelease`
   - an unknown id gives an empty result
   - a missing folder gives an empty result without throwing
   - identical results when `AzureDevOpsOptions.Pat` is null and when it is `"dummy"`
3. `PackageDependencyReader` tests: the ranges declared in the nuspec come back as `FamilyDependency`, filtered by the family patterns, with the same results with and without a PAT.
4. Verification workspace check, in `VerificationWorkspaceTests` or a new focused test. Use `WriteNuGetConfigAsync` with a settings feed that is a local folder and a non-empty PAT, then run a real `dotnet restore` (through `DotnetCliRunner`) of a tiny project that references a `TestFeed` package. It must succeed. Self-skip if `dotnet` is unavailable, following the existing pattern in `DotnetCliRunnerTests`/`VerificationEndToEndTests`.
5. **Only if** a test fails because credentials are attached to a local source: attach the PAT only to `http`/`https` feeds, in `FeedVersionResolver`, `PackageDependencyReader` and `VerificationWorkspace.WriteNuGetConfigAsync`. Use one shared helper, and keep behaviour for https feeds identical. Record in the task notes which path was taken.

## Files
- `tests/AutoRemediator.Infrastructure.Tests/TestFeed.cs` (new)
- `tests/AutoRemediator.Infrastructure.Tests/LocalFeedTests.cs` (new)
- `tests/AutoRemediator.Infrastructure.Tests/VerificationWorkspaceTests.cs`: restore-from-local-feed test (or a new file)
- Only if step 5 is needed: `src/AutoRemediator.Infrastructure/Feeds/FeedVersionResolver.cs`, `src/AutoRemediator.Infrastructure/Alignment/PackageDependencyReader.cs`, `src/AutoRemediator.Infrastructure/Verification/VerificationWorkspace.cs`

## Out of scope
- Feed-to-pattern mapping and surfacing feed errors (PRD §8 "mapped and loud"). Not built here.
- `file:///` URI feeds. Plain absolute paths are the supported form; note in the walkthrough if URIs also happen to work.

## Done when
- [ ] Every case in steps 2–4 has a test and they pass: `dotnet test tests/AutoRemediator.Infrastructure.Tests --filter "FullyQualifiedName~LocalFeed|FullyQualifiedName~VerificationWorkspace"`.
- [ ] Package ids in these tests are unique per test run (for example, suffixed with a GUID fragment), so the global packages cache can never serve a stale copy.
- [ ] Either no production file changed, or the step 5 change is limited to skipping credentials for non-http(s) feeds and existing tests for https feeds still pass.
- [ ] `dotnet test AutoRemediator.sln` passes.

## Notes
- `Repository.Factory.GetCoreV3(new PackageSource(path))` should pick a local `FindPackageByIdResource` for a folder. This task exists to confirm that, not assume it.
- The restore test writes into the global packages folder. Unique ids keep that harmless.
- The nuspec needs `id`, `version`, `authors` and `description` at minimum. A `[Content_Types].xml` is not required for restore from a local folder; add one if NuGet complains.
