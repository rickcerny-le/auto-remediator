using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Tests;

/// <summary>
/// Drives the real verification chain — workspace extraction, `dotnet restore`, `dotnet build`,
/// diagnostic parsing and classification — against a synthetic repository delivered as a zip.
/// Only the Azure DevOps archive call is faked, so this covers everything the run does locally.
///
/// The synthetic projects deliberately reference no packages, so restore needs no network.
/// </summary>
public class VerificationEndToEndTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "orion180", "platform", "web-api");

    /// <summary>No feeds: restore resolves nothing, so these tests never touch the network.</summary>
    private static TargetingSettings Settings => new(["Orion180.*"], feeds: []);

    private const string Csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>
        """;

    private static VerificationService Service(Func<Stream> archive)
    {
        var factory = new VerificationWorkspaceFactory(
            new ArchiveOnlyAdo(archive),
            Options.Create(new AzureDevOpsOptions { Pat = null }),
            NullLogger<VerificationWorkspaceFactory>.Instance);

        return new VerificationService(
            factory,
            new DotnetCliRunner(NullLogger<DotnetCliRunner>.Instance),
            new NullLogStore(),
            NullLogger<VerificationService>.Instance);
    }

    private static Task<VerificationResult> VerifyAsync(
        Func<Stream> archive,
        IReadOnlyList<ChangedManifest>? edits = null)
        => Service(archive).VerifyAsync(
            Guid.NewGuid(),
            Repo,
            "commit-abc",
            new RepositoryUpdatePlan([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")], edits ?? []),
            Settings,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_compiling_repository_verifies()
    {
        var result = await VerifyAsync(() => TestArchive.Of(
            ("src/App/App.csproj", Csproj),
            ("src/App/Program.cs", "public static class Program { public static void Main() { } }")));

        Assert.Equal(VerificationClassification.Verified, result.Outcome.Classification);
        Assert.Empty(result.Outcome.Diagnostics);
    }

    [Fact]
    public async Task A_compile_break_is_rejected_with_a_located_diagnostic()
    {
        var result = await VerifyAsync(() => TestArchive.Of(
            ("src/App/App.csproj", Csproj),
            ("src/App/Program.cs", """
                public static class Program
                {
                    public static void Main()
                    {
                        var client = new Client();
                        client.SubmitAsync();
                    }
                }

                public sealed class Client
                {
                }
                """)));

        Assert.Equal(VerificationClassification.DependencyFailure, result.Outcome.Classification);

        var diagnostic = Assert.Single(result.Outcome.Diagnostics);
        Assert.Equal("CS1061", diagnostic.Code);
        Assert.Equal("src/App/Program.cs", diagnostic.Path);
        Assert.Equal(6, diagnostic.Line);
        Assert.Contains("SubmitAsync", diagnostic.Message);

        // Nothing may be offered for pushing when the change was rejected.
        Assert.Empty(result.LockFileChanges);
    }

    [Fact]
    public async Task An_edit_that_breaks_the_build_is_what_gets_verified()
    {
        // The tree compiles as extracted; the edit is what breaks it. This proves verification
        // runs against the computed change rather than the repository's current state.
        var result = await VerifyAsync(
            () => TestArchive.Of(
                ("src/App/App.csproj", Csproj),
                ("src/App/Program.cs", "public static class Program { public static void Main() { } }")),
            [new ChangedManifest("/src/App/Program.cs", "public static class Program { public static void Main() { NoSuchThing(); } }")]);

        Assert.Equal(VerificationClassification.DependencyFailure, result.Outcome.Classification);
        Assert.Contains(result.Outcome.Diagnostics, d => d.Code == "CS0103");
    }

    [Fact]
    public async Task An_unresolvable_package_is_rejected_at_restore()
    {
        var withMissingPackage = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Orion180.DefinitelyNotReal" Version="9.9.9" />
              </ItemGroup>
            </Project>
            """;

        var result = await VerifyAsync(() => TestArchive.Of(
            ("src/App/App.csproj", withMissingPackage),
            ("src/App/Program.cs", "public static class Program { public static void Main() { } }")));

        Assert.Equal(VerificationClassification.DependencyFailure, result.Outcome.Classification);
        Assert.Contains(result.Outcome.Diagnostics, d => d.Code.StartsWith("NU", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_committed_lock_file_is_regenerated_and_offered_for_the_commit()
    {
        var withLockFile = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
            </Project>
            """;

        var result = await VerifyAsync(() => TestArchive.Of(
            ("src/App/App.csproj", withLockFile),
            ("src/App/Program.cs", "public static class Program { public static void Main() { } }"),
            // A stale lock file, as a repository in locked mode would have after a bump.
            ("src/App/packages.lock.json", """{ "version": 1, "dependencies": {} }""")));

        Assert.Equal(VerificationClassification.Verified, result.Outcome.Classification);

        var change = Assert.Single(result.LockFileChanges);
        Assert.Equal("/src/App/packages.lock.json", change.Path);
        Assert.NotEqual("""{ "version": 1, "dependencies": {} }""", change.Content);
    }

    [Fact]
    public async Task A_repository_without_a_lock_file_is_not_given_one()
    {
        var result = await VerifyAsync(() => TestArchive.Of(
            ("src/App/App.csproj", Csproj),
            ("src/App/Program.cs", "public static class Program { public static void Main() { } }")));

        Assert.Equal(VerificationClassification.Verified, result.Outcome.Classification);
        Assert.Empty(result.LockFileChanges);
    }

    [Fact]
    public async Task A_failed_archive_download_degrades_to_Skipped()
    {
        var result = await VerifyAsync(() => throw new HttpRequestException("404 from the archive endpoint"));

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
        Assert.Contains("could not be prepared", result.Outcome.SkipReason);
    }

    [Fact]
    public async Task No_workspace_directory_survives_a_run()
    {
        var root = Path.Combine(Path.GetTempPath(), "autoremediator");
        var before = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;

        await VerifyAsync(() => TestArchive.Of(
            ("src/App/App.csproj", Csproj),
            ("src/App/Program.cs", "public static class Program { public static void Main() { } }")));

        var after = Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;
        Assert.Equal(before, after);
    }

    private sealed class ArchiveOnlyAdo(Func<Stream> archive) : IAzureDevOpsClient
    {
        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RepositoryFile>>([]);
        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string b, CancellationToken ct = default)
            => Task.FromResult<string?>("commit-abc");
        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default)
            => Task.FromResult(archive());
        public Task PushFilesAsync(ManagedRepository r, string b, string c, IReadOnlyList<FileChange> ch, string m, CancellationToken ct = default)
            => throw new NotSupportedException("verification never pushes");
        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string ti, string d, CancellationToken ct = default)
            => throw new NotSupportedException("verification never opens a pull request");
    }

    /// <summary>Keeps blob storage out of these tests; log capture is covered elsewhere.</summary>
    private sealed class NullLogStore : IVerificationLogStore
    {
        public Task<string?> StoreAsync(Guid runId, string content, CancellationToken ct = default)
            => Task.FromResult<string?>($"{runId}/verification.log");

        public Task<string?> ReadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
