using AutoRemediator.Contracts.Messages;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using AutoRemediator.Infrastructure.AzureDevOps;
using AutoRemediator.Infrastructure.Configuration;
using AutoRemediator.Infrastructure.Remediation;
using AutoRemediator.Infrastructure.Review;
using AutoRemediator.Infrastructure.Verification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutoRemediator.Infrastructure.Tests.Review;

public class ReviewCommandHandlerTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "orion180", "platform", "web-api");
    private static readonly DateTimeOffset VerifiedAt = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static ProposedFile ManifestFile(string newContent = "<New/>") =>
        new("/Directory.Packages.props", "<Old/>", newContent, ProposedFileOrigin.Manifest);

    private static ProposedFile AgentFile(string path = "/src/App/Program.cs", string newContent = "new source") =>
        new(path, "old source", newContent, ProposedFileOrigin.AgentEdit);

    private static ChangeProposal NewProposal(Guid runId, string baseCommitId = "commit-abc", int attempts = 2) => new(
        runId, Repo.Id, baseCommitId, VerifiedAt,
        [ManifestFile(), AgentFile()],
        [new VerificationDiagnostic("CS1061", "'Client' has no member 'SubmitAsync'", "src/App/Program.cs", 3, 42)],
        attempts, "runs/x/remediation-transcript.md");

    private static RemediationRun HeldRun(ChangeProposal proposal, out string reference)
    {
        var run = new RemediationRun(proposal.RunId, proposal.RepositoryId, Repo.Slug, DateTimeOffset.UtcNow.AddMinutes(-5));
        run.RecordUpdates([new DependencyUpdate("Orion180.Core", "1.0.0", "2.0.0")]);
        run.Advance(RunStatus.Remediating);
        reference = $"{proposal.RunId}/proposal.json";
        run.AwaitingReview(reference, VerifiedAt);
        return run;
    }

    private static ReviewCommandHandler BuildHandler(
        FakeRunStore runStore,
        FakeProposalStore proposals,
        FakeAdo? ado = null,
        FakeVerification? verification = null,
        IRemediationLoop? loop = null,
        FakeAgent? agent = null,
        RecordingLogs? logs = null,
        TimeProvider? timeProvider = null)
        => new(
            runStore,
            proposals,
            new FakeRepoStore(),
            new FakeSettingsStore(),
            verification ?? new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), [])),
            loop ?? FakeLoop.NoAgent(),
            agent ?? new FakeAgent { IsAvailable = true },
            logs ?? new RecordingLogs(),
            ado ?? new FakeAdo(),
            timeProvider ?? TimeProvider.System,
            NullLogger<ReviewCommandHandler>.Instance);

    // ---- Refusal (FR-020) ------------------------------------------------------------

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Discarded)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Verifying)]
    public async Task Every_command_against_a_non_AwaitingReview_run_changes_nothing_and_touches_no_azure_devops_method(RunStatus status)
    {
        foreach (var command in Enum.GetValues<ReviewCommand>())
        {
            var runId = Guid.NewGuid();
            var run = new RemediationRun(runId, Repo.Id, Repo.Slug, DateTimeOffset.UtcNow);
            run.Advance(status);
            var runStore = new FakeRunStore([run]);
            var proposals = new FakeProposalStore();
            var ado = new FakeAdo();

            var handler = BuildHandler(runStore, proposals, ado);
            await handler.HandleAsync(runId, command, TestContext.Current.CancellationToken);

            Assert.Equal(status, runStore.Get(runId)!.Status);
            Assert.Equal(0, ado.Pushes);
            Assert.Null(ado.LastDescription);
            Assert.False(ado.BranchHeadQueried);
        }
    }

    [Fact]
    public async Task An_unknown_run_id_is_a_no_op()
    {
        var runStore = new FakeRunStore([]);
        var handler = BuildHandler(runStore, new FakeProposalStore());

        await handler.HandleAsync(Guid.NewGuid(), ReviewCommand.Approve, TestContext.Current.CancellationToken);

        Assert.Empty(runStore.All);
    }

    // ---- Approve (US1) ----------------------------------------------------------------

    [Fact]
    public async Task Approve_pushes_exactly_the_proposals_files_with_its_base_commit_and_reaches_Completed()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), []));
        var ado = new FakeAdo();

        var handler = BuildHandler(runStore, proposals, ado, verification);
        await handler.HandleAsync(run.Id, ReviewCommand.Approve, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.Completed, saved.Status);
        Assert.NotNull(saved.PullRequestUrl);
        Assert.Equal(1, ado.Pushes);
        Assert.Equal("commit-abc", ado.LastBaseCommitId);
        Assert.Equal(
            proposal.Files.Select(f => f.Path).ToArray(),
            ado.LastChanges!.Select(c => c.Path).ToArray());
        Assert.False(verification.WasCalled); // FR-016: no re-verification.
    }

    [Fact]
    public async Task Approve_pushes_to_the_update_branch_and_opens_a_pr_into_the_target_branch()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo();

        var handler = BuildHandler(runStore, proposals, ado);
        await handler.HandleAsync(run.Id, ReviewCommand.Approve, TestContext.Current.CancellationToken);

        Assert.Equal(RemediationRunner.UpdateBranch, ado.LastPushBranch);
        Assert.Equal(RemediationRunner.UpdateBranch, ado.LastPrSource);
        Assert.Equal(Repo.TargetBranch, ado.LastPrTarget);
    }

    [Fact]
    public async Task Approved_pull_request_states_when_and_against_which_commit_the_change_was_verified()
    {
        var proposal = NewProposal(Guid.NewGuid(), baseCommitId: "abcdef1234567890");
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo();

        // Approving well after verification must not read as "just verified".
        var timeProvider = new SettableTimeProvider(VerifiedAt.AddDays(9));

        var handler = BuildHandler(runStore, proposals, ado, timeProvider: timeProvider);
        await handler.HandleAsync(run.Id, ReviewCommand.Approve, TestContext.Current.CancellationToken);

        Assert.Contains("2026-08-01", ado.LastDescription);
        Assert.Contains("abcdef12", ado.LastDescription);
        Assert.Contains("AI-authored source edits", ado.LastDescription);
    }

    // ---- Staleness (US4) ---------------------------------------------------------------

    [Fact]
    public async Task Approve_whose_push_is_refused_leaves_the_run_AwaitingReview_with_a_note_and_no_error()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo { RejectPush = new PushRejectedException("GitRefUpdateStaleException", "stale") };

        var handler = BuildHandler(runStore, proposals, ado);
        await handler.HandleAsync(run.Id, ReviewCommand.Approve, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.AwaitingReview, saved.Status);
        Assert.NotNull(saved.ReviewNote);
        Assert.Null(saved.Error);
        Assert.Null(saved.PullRequestUrl);
        Assert.False(ado.PullRequestCalled);
    }

    [Fact]
    public async Task A_refused_approve_leaves_the_proposal_byte_identical()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo { RejectPush = new PushRejectedException("GitRefUpdateStaleException", "stale") };

        var handler = BuildHandler(runStore, proposals, ado);
        await handler.HandleAsync(run.Id, ReviewCommand.Approve, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(reference, saved.ProposalReference);
        Assert.Same(proposal, proposals[reference]);
    }

    [Fact]
    public async Task Rebuild_materializes_at_the_current_head_and_replaces_the_proposal_on_success()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo { BranchHeads = { [RemediationRunner.UpdateBranch] = "commit-new-head" } };
        var newFiles = new List<ProposedFile> { ManifestFile(), AgentFile(newContent: "rebuilt source") };
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), []))
        {
            Workspace = new FakeWorkspace { Files = newFiles },
        };

        var handler = BuildHandler(runStore, proposals, ado, verification);
        await handler.HandleAsync(run.Id, ReviewCommand.Rebuild, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.AwaitingReview, saved.Status);
        Assert.Equal("commit-new-head", verification.CommitId);
        // Overwritten in place: the reference is stable across a replacement (data-model.md).
        Assert.Equal(reference, saved.ProposalReference);

        var rebuilt = proposals[saved.ProposalReference!]!;
        Assert.Equal("commit-new-head", rebuilt.BaseCommitId);
        Assert.Equal("rebuilt source", rebuilt.AgentEdits.Single().NewContent);
    }

    [Fact]
    public async Task Rebuild_replays_the_manifest_edits_and_the_recorded_agent_edits_into_the_new_tree()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo();
        var workspace = new FakeWorkspace();
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), [])) { Workspace = workspace };

        var handler = BuildHandler(runStore, proposals, ado, verification);
        await handler.HandleAsync(run.Id, ReviewCommand.Rebuild, TestContext.Current.CancellationToken);

        Assert.Single(workspace.AppliedEdits, e => e.Path == "/src/App/Program.cs" && e.NewContent == "new source");
        Assert.Equal(proposal.ManifestEdits.Single().Path, verification.PlanUsed!.ChangedManifests.Single().Path);
    }

    [Fact]
    public async Task Rebuild_whose_reverification_fails_leaves_the_previous_proposal_in_place()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo();
        var failing = VerificationOutcome.DependencyFailure([new VerificationDiagnostic("CS1061", "still broken")]);
        var verification = new FakeVerification(new VerificationResult(failing, [])) { Workspace = new FakeWorkspace() };

        var handler = BuildHandler(runStore, proposals, ado, verification);
        await handler.HandleAsync(run.Id, ReviewCommand.Rebuild, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.AwaitingReview, saved.Status);
        Assert.NotNull(saved.ReviewNote);
        Assert.Equal(reference, saved.ProposalReference);
        Assert.Same(proposal, proposals[reference]);
    }

    // ---- Retry (US3) --------------------------------------------------------------------

    [Fact]
    public async Task Retry_materializes_at_the_stored_base_commit_and_seeds_the_loop_with_the_provoking_diagnostics()
    {
        var proposal = NewProposal(Guid.NewGuid(), baseCommitId: "commit-original");
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var verification = new FakeVerification(new VerificationResult(
            VerificationOutcome.DependencyFailure(proposal.ProvokingDiagnostics), []))
        {
            Workspace = new FakeWorkspace { Files = [ManifestFile(), AgentFile(newContent: "retried source")] },
        };
        var loop = new FakeLoop(new RemediationLoopResult(
            1, true, new VerificationResult(VerificationOutcome.Verified(), []), "retry transcript"));

        var handler = BuildHandler(runStore, proposals, verification: verification, loop: loop);
        await handler.HandleAsync(run.Id, ReviewCommand.Retry, TestContext.Current.CancellationToken);

        Assert.Equal("commit-original", verification.CommitId);
        Assert.True(loop.WasCalled);
        Assert.Equal("CS1061", loop.SeededDiagnostics!.Single().Code);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.AwaitingReview, saved.Status);
        var retried = proposals[saved.ProposalReference!]!;
        Assert.Equal("commit-original", retried.BaseCommitId);
        Assert.Equal("retried source", retried.AgentEdits.Single().NewContent);
    }

    [Fact]
    public async Task Retry_drops_the_recorded_agent_edits_and_replays_manifest_edits_only()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var workspace = new FakeWorkspace { Files = [ManifestFile(), AgentFile(newContent: "brand new")] };
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), [])) { Workspace = workspace };
        var loop = new FakeLoop(new RemediationLoopResult(1, true, new VerificationResult(VerificationOutcome.Verified(), []), "t"));

        var handler = BuildHandler(runStore, proposals, verification: verification, loop: loop);
        await handler.HandleAsync(run.Id, ReviewCommand.Retry, TestContext.Current.CancellationToken);

        // The old agent edit is never applied to the fresh tree — only the loop's new one is present.
        Assert.Empty(workspace.AppliedEdits);
        Assert.Equal(proposal.ManifestEdits.Single().Path, verification.PlanUsed!.ChangedManifests.Single().Path);
    }

    [Fact]
    public async Task Retry_with_an_unavailable_agent_leaves_the_proposal_untouched_and_sets_a_note()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var agent = new FakeAgent { IsAvailable = false };
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), []));

        var handler = BuildHandler(runStore, proposals, verification: verification, agent: agent);
        await handler.HandleAsync(run.Id, ReviewCommand.Retry, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.AwaitingReview, saved.Status);
        Assert.NotNull(saved.ReviewNote);
        Assert.Equal(reference, saved.ProposalReference);
        Assert.False(verification.WasCalled);
    }

    [Fact]
    public async Task Retry_that_does_not_verify_leaves_the_previous_proposal_in_place()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.DependencyFailure([]), []))
        {
            Workspace = new FakeWorkspace(),
        };
        var loop = new FakeLoop(new RemediationLoopResult(3, false, new VerificationResult(VerificationOutcome.DependencyFailure([]), []), "t"));

        var handler = BuildHandler(runStore, proposals, verification: verification, loop: loop);
        await handler.HandleAsync(run.Id, ReviewCommand.Retry, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.AwaitingReview, saved.Status);
        Assert.NotNull(saved.ReviewNote);
        Assert.Equal(reference, saved.ProposalReference);
    }

    [Fact]
    public async Task Retry_starts_a_fresh_token_budget_each_time()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };

        var agent = new FakeAgent { IsAvailable = true, TokensPerAttempt = 100 };
        var options = Options.Create(new RemediationLoopOptions { MaxAttempts = 1, TokenBudget = 100 });
        var realLoop = new RemediationLoop(agent, options, NullLogger<RemediationLoop>.Instance);

        var workspace = new FakeWorkspace();
        var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), [])) { Workspace = workspace };

        var handler = BuildHandler(runStore, proposals, verification: verification, loop: realLoop, agent: agent);
        await handler.HandleAsync(run.Id, ReviewCommand.Retry, TestContext.Current.CancellationToken);

        Assert.Equal(1, agent.CallCount);

        // A second retry against a fresh proposal must not see the first call's spend carried over.
        var saved = runStore.Get(run.Id)!;
        var second = proposals[saved.ProposalReference!]!;
        var secondRun = new RemediationRun(second.RunId, second.RepositoryId, Repo.Slug, DateTimeOffset.UtcNow);
        secondRun.RecordUpdates(run.Updates);
        secondRun.Advance(RunStatus.Remediating);
        secondRun.AwaitingReview(saved.ProposalReference!, VerifiedAt);
        var secondStore = new FakeRunStore([secondRun]);
        var secondWorkspace = new FakeWorkspace();
        var secondVerification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), [])) { Workspace = secondWorkspace };
        var secondHandler = BuildHandler(secondStore, proposals, verification: secondVerification, loop: realLoop, agent: agent);

        await secondHandler.HandleAsync(secondRun.Id, ReviewCommand.Retry, TestContext.Current.CancellationToken);

        Assert.Equal(2, agent.CallCount);
        Assert.Equal(RunStatus.AwaitingReview, secondStore.Get(secondRun.Id)!.Status);
    }

    // ---- Discard (US3) -------------------------------------------------------------------

    [Fact]
    public async Task Discard_reaches_terminal_Discarded_and_touches_the_repository_not_at_all()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo();

        var handler = BuildHandler(runStore, proposals, ado);
        await handler.HandleAsync(run.Id, ReviewCommand.Discard, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.Discarded, saved.Status);
        Assert.NotNull(saved.FinishedAtUtc);
        Assert.Equal(0, ado.Pushes);
        Assert.False(ado.PullRequestCalled);
        Assert.False(ado.BranchHeadQueried);
    }

    [Fact]
    public async Task A_race_between_approve_and_discard_resolves_to_one_winner()
    {
        var proposal = NewProposal(Guid.NewGuid());
        var run = HeldRun(proposal, out var reference);
        var runStore = new FakeRunStore([run]);
        var proposals = new FakeProposalStore { [reference] = proposal };
        var ado = new FakeAdo();

        // The queue's single-consumer processing means these execute one after another.
        var handler = BuildHandler(runStore, proposals, ado);
        await handler.HandleAsync(run.Id, ReviewCommand.Approve, TestContext.Current.CancellationToken);
        await handler.HandleAsync(run.Id, ReviewCommand.Discard, TestContext.Current.CancellationToken);

        var saved = runStore.Get(run.Id)!;
        Assert.Equal(RunStatus.Completed, saved.Status); // approve won; discard was refused.
        Assert.Equal(1, ado.Pushes);
    }

    [Fact]
    public async Task Neither_retry_nor_rebuild_leaves_a_workspace_directory_behind()
    {
        foreach (var command in new[] { ReviewCommand.Retry, ReviewCommand.Rebuild })
        {
            var proposal = NewProposal(Guid.NewGuid());
            var run = HeldRun(proposal, out var reference);
            var runStore = new FakeRunStore([run]);
            var proposals = new FakeProposalStore { [reference] = proposal };
            var workspace = new FakeWorkspace();
            var verification = new FakeVerification(new VerificationResult(VerificationOutcome.Verified(), [])) { Workspace = workspace };
            var loop = new FakeLoop(new RemediationLoopResult(1, true, new VerificationResult(VerificationOutcome.Verified(), []), "t"));

            var handler = BuildHandler(runStore, proposals, verification: verification, loop: loop);
            await handler.HandleAsync(run.Id, command, TestContext.Current.CancellationToken);

            Assert.True(workspace.Disposed);
            Assert.False(Directory.Exists(workspace.Root));
        }
    }

    // ---- Test doubles ---------------------------------------------------------------------

    private sealed class FakeRunStore(IEnumerable<RemediationRun> seed) : IRemediationRunStore
    {
        private readonly Dictionary<Guid, RemediationRun> _runs = seed.ToDictionary(r => r.Id);

        public IReadOnlyCollection<RemediationRun> All => _runs.Values;

        public RemediationRun? Get(Guid runId) => _runs.GetValueOrDefault(runId);

        public Task SaveAsync(RemediationRun run, CancellationToken ct = default) { _runs[run.Id] = run; return Task.CompletedTask; }
        public Task<IReadOnlyList<RemediationRun>> ListByRepositoryAsync(Guid repositoryId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([.. _runs.Values.Where(r => r.RepositoryId == repositoryId)]);
        public Task<IReadOnlyList<RemediationRun>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemediationRun>>([.. _runs.Values]);
        public Task<RemediationRun?> GetAsync(Guid runId, CancellationToken ct = default) => Task.FromResult(Get(runId));
        public Task<RemediationRun?> FindAwaitingReviewAsync(Guid repositoryId, CancellationToken ct = default)
            => Task.FromResult(_runs.Values.FirstOrDefault(r => r.RepositoryId == repositoryId && r.Status == RunStatus.AwaitingReview));
    }

    private sealed class FakeProposalStore : IChangeProposalStore
    {
        private readonly Dictionary<string, ChangeProposal> _stored = [];

        public ChangeProposal? this[string reference]
        {
            get => _stored.GetValueOrDefault(reference);
            set => _stored[reference] = value!;
        }

        public Task<string> StoreAsync(ChangeProposal proposal, CancellationToken ct = default)
        {
            var reference = $"{proposal.RunId}/proposal.json";
            _stored[reference] = proposal;
            return Task.FromResult(reference);
        }

        public Task<ChangeProposal?> ReadAsync(string reference, CancellationToken ct = default) => Task.FromResult(this[reference]);
    }

    private sealed class FakeRepoStore : IManagedRepositoryStore
    {
        public Task<IReadOnlyList<ManagedRepository>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ManagedRepository>>([Repo]);
        public Task<ManagedRepository?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ManagedRepository?>(id == Repo.Id ? Repo : null);
        public Task UpsertAsync(ManagedRepository r, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : ITargetingSettingsStore
    {
        public Task<TargetingSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(new TargetingSettings(["Orion180.*"], feeds: ["https://feed"]));
        public Task SetAsync(TargetingSettings s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeVerification(VerificationResult result) : IVerificationService
    {
        private readonly VerificationResult _result = result;

        public bool WasCalled { get; private set; }
        public string? CommitId { get; private set; }
        public RepositoryUpdatePlan? PlanUsed { get; private set; }
        public IVerificationWorkspace? Workspace { get; init; }

        public Task<IVerificationSession> OpenAsync(Guid runId, ManagedRepository repository, string commitId, RepositoryUpdatePlan plan, TargetingSettings settings, CancellationToken ct = default)
        {
            CommitId = commitId;
            PlanUsed = plan;
            return Task.FromResult<IVerificationSession>(new FakeSession(this));
        }

        private sealed class FakeSession(FakeVerification owner) : IVerificationSession
        {
            public IVerificationWorkspace? Workspace => owner.Workspace;
            public Task<VerificationResult> VerifyAsync(CancellationToken ct = default) { owner.WasCalled = true; return Task.FromResult(owner._result); }
            public void Dispose() => owner.Workspace?.Dispose();
        }
    }

    private sealed class FakeWorkspace : IVerificationWorkspace
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("autoremediator-review-test-").FullName;
        public bool Disposed { get; private set; }
        public IReadOnlyCollection<string> GeneratedPaths => [];
        public IReadOnlyList<string> BuildTargets => [];
        public List<ProposedEdit> AppliedEdits { get; } = [];

        public IReadOnlyList<ProposedFile> Files { get; set; } = [ManifestFile(), AgentFile()];

        public Task<string?> ReadAsync(string path, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<FileChange>> LockFileChangesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FileChange>>([]);
        public Task<EditApplication> ApplyProposedEditAsync(ProposedEdit edit, CancellationToken ct = default)
        {
            AppliedEdits.Add(edit);
            return Task.FromResult(EditApplication.Accepted(edit.Path));
        }
        public Task<IReadOnlyList<FileChange>> AppliedEditChangesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FileChange>>([]);
        public Task<IReadOnlyList<ProposedFile>> ProposedFilesAsync(CancellationToken ct = default) => Task.FromResult(Files);

        public void Dispose()
        {
            Disposed = true;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FakeAdo : IAzureDevOpsClient
    {
        public Dictionary<string, string?> BranchHeads { get; } = new() { [RemediationRunner.UpdateBranch] = "commit-abc" };
        public bool BranchHeadQueried { get; private set; }
        public PushRejectedException? RejectPush { get; set; }
        public int Pushes { get; private set; }
        public string? LastBaseCommitId { get; private set; }
        public string? LastPushBranch { get; private set; }
        public IReadOnlyList<FileChange>? LastChanges { get; private set; }
        public bool PullRequestCalled { get; private set; }
        public string? LastDescription { get; private set; }
        public string? LastPrSource { get; private set; }
        public string? LastPrTarget { get; private set; }

        public Task<bool> RepositoryExistsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<RepositoryFile>> GetManifestsAsync(ManagedRepository r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RepositoryFile>>([]);

        public Task<string?> GetBranchHeadAsync(ManagedRepository r, string branch, CancellationToken ct = default)
        {
            BranchHeadQueried = true;
            return Task.FromResult(BranchHeads.GetValueOrDefault(branch));
        }

        public Task<Stream> GetRepositoryArchiveAsync(ManagedRepository r, string commitId, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());

        public Task PushFilesAsync(ManagedRepository r, string branch, string baseCommitId, IReadOnlyList<FileChange> changes, string message, CancellationToken ct = default)
        {
            if (RejectPush is not null)
            {
                throw RejectPush;
            }

            Pushes++;
            LastBaseCommitId = baseCommitId;
            LastPushBranch = branch;
            LastChanges = changes;
            return Task.CompletedTask;
        }

        public Task<string> EnsurePullRequestAsync(ManagedRepository r, string s, string t, string title, string desc, CancellationToken ct = default)
        {
            PullRequestCalled = true;
            LastDescription = desc;
            LastPrSource = s;
            LastPrTarget = t;
            return Task.FromResult("https://dev.azure.com/pr/1");
        }
    }

    private sealed class FakeLoop(RemediationLoopResult result) : IRemediationLoop
    {
        public static FakeLoop NoAgent() => new(new RemediationLoopResult(
            0, false, new VerificationResult(VerificationOutcome.DependencyFailure([]), []), "no model is configured"));

        public bool WasCalled { get; private set; }
        public IReadOnlyList<VerificationDiagnostic>? SeededDiagnostics { get; private set; }

        public Task<RemediationLoopResult> RunAsync(Guid runId, IVerificationSession session, VerificationResult rejected, CancellationToken ct = default)
        {
            WasCalled = true;
            SeededDiagnostics = rejected.Outcome.Diagnostics;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeAgent : IRemediationAgent
    {
        public bool IsAvailable { get; set; } = true;
        public int TokensPerAttempt { get; set; } = 10;
        public int CallCount { get; private set; }

        public Task<RemediationProposal> ProposeAsync(RemediationAttempt attempt, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new RemediationProposal(
                [new ProposedEdit("/src/App/Program.cs", "fixed source")], TokensPerAttempt));
        }
    }

    private sealed class RecordingLogs : IVerificationLogStore
    {
        public Dictionary<string, string> Stored { get; } = [];

        public Task<string?> StoreAsync(Guid runId, string content, string name = "verification.log", CancellationToken ct = default)
        {
            Stored[name] = content;
            return Task.FromResult<string?>($"{runId}/{name}");
        }

        public Task<string?> ReadAsync(string reference, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
