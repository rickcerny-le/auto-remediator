using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoRemediator.Infrastructure.Tests;

public class VerificationServiceTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "contoso", "platform", "web-api");
    private static readonly Guid RunId = Guid.NewGuid();

    private static TargetingSettings Settings => new(["Contoso.*"], feeds: ["https://feed/v3/index.json"]);

    private static RepositoryUpdatePlan Plan => new(
        [new DependencyUpdate("Contoso.Core", "1.4.0", "1.5.0")],
        [new ChangedManifest("/Directory.Packages.props", "<Project />")]);

    private static async Task<(VerificationResult Result, FakeCli Cli, FakeLogStore Logs)> VerifyAsync(
        FakeCli cli,
        FakeWorkspaceFactory? workspaces = null)
    {
        var logs = new FakeLogStore();
        var service = new VerificationService(
            workspaces ?? new FakeWorkspaceFactory(),
            cli,
            logs,
            NullLogger<VerificationService>.Instance);

        using var session = await service.OpenAsync(
            RunId, Repo, "commit-abc", Plan, Settings, TestContext.Current.CancellationToken);
        var result = await session.VerifyAsync(TestContext.Current.CancellationToken);

        return (result, cli, logs);
    }

    // ---- Verified -------------------------------------------------------------------

    [Fact]
    public async Task Restore_and_build_success_is_Verified()
    {
        var (result, cli, logs) = await VerifyAsync(FakeCli.AllSucceed());

        Assert.Equal(VerificationClassification.Verified, result.Outcome.Classification);
        Assert.Empty(result.Outcome.Diagnostics);
        Assert.NotNull(result.Outcome.LogReference);
        Assert.Single(logs.Stored);
        Assert.Equal(
            ["restore \"App.sln\" --nologo", "build \"App.sln\" --no-restore --nologo -p:GenerateFullPaths=true"],
            cli.Invocations);
    }

    [Fact]
    public async Task Every_project_is_restored_and_built_when_there_is_no_solution()
    {
        var workspaces = new FakeWorkspaceFactory { BuildTargets = ["A.csproj", "B.csproj"] };

        var (result, cli, _) = await VerifyAsync(FakeCli.AllSucceed(), workspaces);

        Assert.Equal(VerificationClassification.Verified, result.Outcome.Classification);
        Assert.Equal(4, cli.Invocations.Count);
        Assert.Contains(cli.Invocations, i => i.Contains("A.csproj") && i.StartsWith("restore", StringComparison.Ordinal));
        Assert.Contains(cli.Invocations, i => i.Contains("B.csproj") && i.StartsWith("build", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tree_with_nothing_buildable_is_Skipped()
    {
        var workspaces = new FakeWorkspaceFactory { BuildTargets = [] };

        var (result, cli, _) = await VerifyAsync(FakeCli.AllSucceed(), workspaces);

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
        Assert.Contains("no solution or project", result.Outcome.SkipReason);
        Assert.Empty(cli.Invocations);
    }

    [Fact]
    public async Task Build_runs_with_no_restore_so_restore_is_not_repeated()
    {
        var (_, cli, _) = await VerifyAsync(FakeCli.AllSucceed());

        Assert.Contains("--no-restore", cli.Invocations[1]);
    }

    [Fact]
    public async Task Tests_are_never_run()
    {
        var (_, cli, _) = await VerifyAsync(FakeCli.AllSucceed());

        Assert.DoesNotContain(cli.Invocations, i => i.Contains("test"));
    }

    // ---- Restore gates build --------------------------------------------------------

    [Fact]
    public async Task Failed_restore_short_circuits_before_build()
    {
        var cli = FakeCli.RestoreFails("""
            error NU1107: Version conflict detected for Contoso.Common. Install/reference Contoso.Common 2.0.0 directly.
            """);

        var (result, invoked, _) = await VerifyAsync(cli);

        Assert.Single(invoked.Invocations);
        Assert.Contains("restore", invoked.Invocations[0]);
        Assert.Equal(VerificationClassification.DependencyFailure, result.Outcome.Classification);
        Assert.Equal("NU1107", result.Outcome.Diagnostics.Single().Code);
    }

    [Fact]
    public async Task Project_level_restore_error_is_parsed_with_its_path()
    {
        // MSBuild's project-level form: no (line,col). This is how NuGet reports most restore
        // errors, so failing to parse it would misclassify them as environmental.
        var cli = FakeCli.RestoreFails(
            @"C:\work\src\App\App.csproj : error NU1101: Unable to find package Contoso.Nope. No packages exist with this id.");

        var (result, _, _) = await VerifyAsync(cli);

        Assert.Equal(VerificationClassification.DependencyFailure, result.Outcome.Classification);
        var diagnostic = result.Outcome.Diagnostics.Single();
        Assert.Equal("NU1101", diagnostic.Code);
        Assert.Contains("Contoso.Nope", diagnostic.Message);
        Assert.Null(diagnostic.Line);
        Assert.EndsWith("App.csproj", diagnostic.Path);
    }

    [Fact]
    public async Task Downgrade_error_is_a_dependency_failure()
    {
        var cli = FakeCli.RestoreFails("""
            error NU1605: Warning As Error: Detected package downgrade: Contoso.Common from 2.0.0 to 1.4.0.
            """);

        var (result, _, _) = await VerifyAsync(cli);

        Assert.Equal(VerificationClassification.DependencyFailure, result.Outcome.Classification);
        Assert.Equal("NU1605", result.Outcome.Diagnostics.Single().Code);
    }

    // ---- Compile break --------------------------------------------------------------

    [Fact]
    public async Task Compile_error_is_a_dependency_failure_with_file_and_position()
    {
        var workspaces = new FakeWorkspaceFactory();
        var cli = FakeCli.BuildFails(root => $"""
            {root}{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}Foo{Path.DirectorySeparatorChar}Bar.cs(42,17): error CS0117: 'Client' does not contain a definition for 'SubmitAsync' [{root}\src\Foo\Foo.csproj]
            """);

        var (result, _, _) = await VerifyAsync(cli, workspaces);

        Assert.Equal(VerificationClassification.DependencyFailure, result.Outcome.Classification);
        var diagnostic = result.Outcome.Diagnostics.Single();
        Assert.Equal("CS0117", diagnostic.Code);
        Assert.Equal("src/Foo/Bar.cs", diagnostic.Path);
        Assert.Equal(42, diagnostic.Line);
        Assert.Equal(17, diagnostic.Column);
        Assert.Equal("'Client' does not contain a definition for 'SubmitAsync'", diagnostic.Message);
    }

    [Fact]
    public async Task Rejected_change_carries_no_lock_file_changes()
    {
        var (result, _, _) = await VerifyAsync(FakeCli.RestoreFails("error NU1107: Version conflict detected."));

        Assert.Empty(result.LockFileChanges);
    }

    // ---- Skipped --------------------------------------------------------------------

    [Fact]
    public async Task Unreachable_feed_is_Skipped_not_a_dependency_failure()
    {
        var cli = FakeCli.RestoreFails("""
            error NU1301: Unable to load the service index for source https://feed/v3/index.json.
            """);

        var (result, _, _) = await VerifyAsync(cli);

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
        Assert.Contains("NU1301", result.Outcome.SkipReason);
    }

    [Fact]
    public async Task Unauthorized_feed_is_Skipped()
    {
        var cli = FakeCli.RestoreFails("""
            error NU1301: Response status code does not indicate success: 401 (Unauthorized).
            """);

        var (result, _, _) = await VerifyAsync(cli);

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
    }

    [Fact]
    public async Task Unsatisfiable_sdk_pin_is_Skipped()
    {
        var cli = FakeCli.RestoreFails("""
            A compatible .NET SDK was not found. Requested SDK version: 8.0.404 from global.json.
            """);

        var (result, _, _) = await VerifyAsync(cli);

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
        Assert.Contains("sdk", result.Outcome.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_is_Skipped()
    {
        var (result, _, _) = await VerifyAsync(FakeCli.RestoreTimesOut());

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
        Assert.Contains("time limit", result.Outcome.SkipReason);
    }

    [Fact]
    public async Task Unrecognized_failure_defaults_to_Skipped()
    {
        var (result, _, _) = await VerifyAsync(FakeCli.RestoreFails("something went sideways and nobody knows why"));

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
        Assert.Contains("without any recognizable diagnostic", result.Outcome.SkipReason);
    }

    [Fact]
    public async Task Tree_that_cannot_be_prepared_is_Skipped()
    {
        var workspaces = new FakeWorkspaceFactory { Throw = new HttpRequestException("404 from the archive endpoint") };

        var (result, cli, _) = await VerifyAsync(FakeCli.AllSucceed(), workspaces);

        Assert.Equal(VerificationClassification.Skipped, result.Outcome.Classification);
        Assert.Contains("could not be prepared", result.Outcome.SkipReason);
        Assert.Empty(cli.Invocations);
    }

    // ---- Logs and lock files --------------------------------------------------------

    [Fact]
    public async Task Logs_are_stored_for_every_outcome()
    {
        foreach (var cli in new[]
                 {
                     FakeCli.AllSucceed(),
                     FakeCli.RestoreFails("error NU1107: Version conflict detected."),
                     FakeCli.RestoreFails("error NU1301: Unable to load the service index for source."),
                     FakeCli.RestoreTimesOut(),
                 })
        {
            var (result, _, logs) = await VerifyAsync(cli);

            Assert.Single(logs.Stored);
            Assert.Equal(logs.Stored.Keys.Single(), result.Outcome.LogReference);
        }
    }

    [Fact]
    public async Task A_failed_build_log_includes_the_preceding_restore_output()
    {
        var cli = FakeCli.BuildFails(_ => "error CS0117: 'Client' has no member 'SubmitAsync'");
        cli.RestoreOutput = "Restored Contoso.Core 1.5.0";

        var (_, _, logs) = await VerifyAsync(cli);

        var log = logs.Stored.Values.Single();
        Assert.Contains("Restored Contoso.Core 1.5.0", log);
        Assert.Contains("CS0117", log);
    }

    [Fact]
    public async Task Verified_run_carries_the_lock_file_changes_restore_produced()
    {
        var workspaces = new FakeWorkspaceFactory
        {
            LockFileChanges = [new FileChange("/src/Web/packages.lock.json", """{ "version": 1 }""")],
        };

        var (result, _, _) = await VerifyAsync(FakeCli.AllSucceed(), workspaces);

        Assert.Equal(VerificationClassification.Verified, result.Outcome.Classification);
        Assert.Equal("/src/Web/packages.lock.json", Assert.Single(result.LockFileChanges).Path);
    }

    [Fact]
    public async Task The_workspace_is_disposed_on_every_path()
    {
        var succeeded = new FakeWorkspaceFactory();
        await VerifyAsync(FakeCli.AllSucceed(), succeeded);
        Assert.True(succeeded.Created!.Disposed);

        var rejected = new FakeWorkspaceFactory();
        await VerifyAsync(FakeCli.RestoreFails("error NU1107: Version conflict detected."), rejected);
        Assert.True(rejected.Created!.Disposed);

        var skipped = new FakeWorkspaceFactory();
        await VerifyAsync(FakeCli.RestoreTimesOut(), skipped);
        Assert.True(skipped.Created!.Disposed);
    }

    // ---- Fakes ----------------------------------------------------------------------

    private sealed class FakeCli : IDotnetCliRunner
    {
        private Func<string, CliResult>? _restore;
        private Func<string, CliResult>? _build;

        public List<string> Invocations { get; } = [];
        public string RestoreOutput { get; set; } = "Restore succeeded.";

        public static FakeCli AllSucceed() => new()
        {
            _restore = _ => new CliResult(0, "Restore succeeded.", false),
            _build = _ => new CliResult(0, "Build succeeded.", false),
        };

        public static FakeCli RestoreFails(string output) => new()
        {
            _restore = _ => new CliResult(1, output, false),
        };

        public static FakeCli RestoreTimesOut() => new()
        {
            _restore = _ => new CliResult(-1, "…", true),
        };

        public static FakeCli BuildFails(Func<string, string> output)
        {
            var cli = new FakeCli();
            cli._restore = _ => new CliResult(0, cli.RestoreOutput, false);
            cli._build = root => new CliResult(1, output(root), false);
            return cli;
        }

        public Task<CliResult> RunAsync(string arguments, string workingDirectory, TimeSpan timeout, CancellationToken ct = default)
        {
            Invocations.Add(arguments);

            var responder = arguments.StartsWith("restore", StringComparison.Ordinal) ? _restore : _build;
            return Task.FromResult(responder?.Invoke(workingDirectory) ?? new CliResult(0, string.Empty, false));
        }
    }

    private sealed class FakeWorkspaceFactory : IVerificationWorkspaceFactory
    {
        public Exception? Throw { get; set; }
        public IReadOnlyList<FileChange> LockFileChanges { get; set; } = [];
        public IReadOnlyList<string> BuildTargets { get; set; } = ["App.sln"];
        public FakeWorkspace? Created { get; private set; }

        public Task<IVerificationWorkspace> CreateAsync(
            ManagedRepository repository, string commitId, IReadOnlyList<ChangedManifest> edits,
            TargetingSettings settings, CancellationToken ct = default)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Created = new FakeWorkspace(LockFileChanges) { BuildTargets = BuildTargets };
            return Task.FromResult<IVerificationWorkspace>(Created);
        }
    }

    private sealed class FakeWorkspace(IReadOnlyList<FileChange> lockFileChanges) : IVerificationWorkspace
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "autoremediator-fake");
        public bool Disposed { get; private set; }
        public IReadOnlyCollection<string> GeneratedPaths => ["NuGet.config"];
        public IReadOnlyList<string> BuildTargets { get; init; } = ["App.sln"];

        public Task<string?> ReadAsync(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<FileChange>> LockFileChangesAsync(CancellationToken ct = default) => Task.FromResult(lockFileChanges);
        public Task<EditApplication> ApplyProposedEditAsync(ProposedEdit edit, CancellationToken ct = default)
            => Task.FromResult(EditApplication.Accepted(edit.Path));
        public Task<IReadOnlyList<FileChange>> AppliedEditChangesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FileChange>>([]);
        public Task<IReadOnlyList<ProposedFile>> ProposedFilesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProposedFile>>([]);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeLogStore : IVerificationLogStore
    {
        public Dictionary<string, string> Stored { get; } = [];

        public Task<string?> StoreAsync(Guid runId, string content, string name = "verification.log", CancellationToken ct = default)
        {
            var reference = $"{runId}/{name}";
            Stored[reference] = content;
            return Task.FromResult<string?>(reference);
        }

        public Task<string?> ReadAsync(string reference, CancellationToken ct = default)
            => Task.FromResult(Stored.TryGetValue(reference, out var v) ? v : null);
    }
}
