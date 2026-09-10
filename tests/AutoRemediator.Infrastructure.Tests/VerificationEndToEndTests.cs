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
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "contoso", "platform", "web-api");

    /// <summary>No feeds: restore resolves nothing, so these tests never touch the network.</summary>
    private static TargetingSettings Settings => new(["Contoso.*"], feeds: []);

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

    private static async Task<VerificationResult> VerifyAsync(
        Func<Stream> archive,
        IReadOnlyList<ChangedManifest>? edits = null)
    {
        using var session = await OpenAsync(archive, edits);
        return await session.VerifyAsync(TestContext.Current.CancellationToken);
    }

    private static Task<IVerificationSession> OpenAsync(
        Func<Stream> archive,
        IReadOnlyList<ChangedManifest>? edits = null)
        => Service(archive).OpenAsync(
            Guid.NewGuid(),
            Repo,
            "commit-abc",
            new RepositoryUpdatePlan([new DependencyUpdate("Contoso.Core", "1.0.0", "2.0.0")], edits ?? []),
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
                <PackageReference Include="Contoso.DefinitelyNotReal" Version="9.9.9" />
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

    // ---- Re-runnable verification (the AI loop's foundation) ------------------------

    [Fact]
    public async Task A_second_verification_compiles_edits_applied_since_the_first()
    {
        // Broken as extracted, then repaired in place — exactly what the remediation loop does.
        using var session = await OpenAsync(() => TestArchive.Of(
            ("src/App/App.csproj", Csproj),
            ("src/App/Program.cs", """
                public static class Program
                {
                    public static void Main() { NoSuchThing(); }
                }
                """)));

        var first = await session.VerifyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(VerificationClassification.DependencyFailure, first.Outcome.Classification);
        Assert.Contains(first.Outcome.Diagnostics, d => d.Code == "CS0103");

        await File.WriteAllTextAsync(
            Path.Combine(session.Workspace!.Root, "src", "App", "Program.cs"),
            "public static class Program { public static void Main() { } }",
            TestContext.Current.CancellationToken);

        var second = await session.VerifyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(VerificationClassification.Verified, second.Outcome.Classification);
    }

    [Fact]
    public async Task The_archive_is_downloaded_once_per_run_however_often_it_verifies()
    {
        var downloads = 0;
        Stream Archive()
        {
            downloads++;
            return TestArchive.Of(
                ("src/App/App.csproj", Csproj),
                ("src/App/Program.cs", "public static class Program { public static void Main() { } }"));
        }

        using var session = await OpenAsync(Archive);
        await session.VerifyAsync(TestContext.Current.CancellationToken);
        await session.VerifyAsync(TestContext.Current.CancellationToken);
        await session.VerifyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task Each_verification_stores_its_own_log()
    {
        // A repeated verification must not overwrite the evidence from an earlier attempt.
        var logs = new RecordingLogStore();
        var factory = new VerificationWorkspaceFactory(
            new ArchiveOnlyAdo(() => TestArchive.Of(
                ("src/App/App.csproj", Csproj),
                ("src/App/Program.cs", "public static class Program { public static void Main() { } }"))),
            Options.Create(new AzureDevOpsOptions { Pat = null }),
            NullLogger<VerificationWorkspaceFactory>.Instance);

        var service = new VerificationService(
            factory,
            new DotnetCliRunner(NullLogger<DotnetCliRunner>.Instance),
            logs,
            NullLogger<VerificationService>.Instance);

        using var session = await service.OpenAsync(
            Guid.NewGuid(), Repo, "commit-abc", new RepositoryUpdatePlan([], []), Settings,
            TestContext.Current.CancellationToken);

        var first = await session.VerifyAsync(TestContext.Current.CancellationToken);
        var second = await session.VerifyAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Outcome.LogReference, second.Outcome.LogReference);
        Assert.Equal(2, logs.Names.Distinct().Count());
    }

    [Fact]
    public async Task Disposing_a_session_deletes_the_tree_even_after_several_verifications()
    {
        var session = await OpenAsync(() => TestArchive.Of(
            ("src/App/App.csproj", Csproj),
            ("src/App/Program.cs", "public static class Program { public static void Main() { } }")));

        await session.VerifyAsync(TestContext.Current.CancellationToken);
        await session.VerifyAsync(TestContext.Current.CancellationToken);
        var root = session.Workspace!.Root;

        session.Dispose();

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task An_unavailable_tree_reports_skipped_and_disposes_cleanly()
    {
        using var session = await OpenAsync(() => throw new HttpRequestException("404 from the archive endpoint"));

        Assert.Null(session.Workspace);

        var result = await session.VerifyAsync(TestContext.Current.CancellationToken);
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

    /// <summary>Records the artifact names a run stored, to prove repeated verifications do not collide.</summary>
    private sealed class RecordingLogStore : IVerificationLogStore
    {
        public List<string> Names { get; } = [];

        public Task<string?> StoreAsync(Guid runId, string content, string name = "verification.log", CancellationToken ct = default)
        {
            Names.Add(name);
            return Task.FromResult<string?>($"{runId}/{name}");
        }

        public Task<string?> ReadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>Keeps blob storage out of these tests; log capture is covered elsewhere.</summary>
    private sealed class NullLogStore : IVerificationLogStore
    {
        public Task<string?> StoreAsync(Guid runId, string content, string name = "verification.log", CancellationToken ct = default)
            => Task.FromResult<string?>($"{runId}/{name}");

        public Task<string?> ReadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
