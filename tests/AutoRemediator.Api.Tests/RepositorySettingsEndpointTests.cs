using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;

namespace AutoRemediator.Api.Tests;

public class RepositorySettingsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RepositorySettingsDto Sample = new(
        true, ["Repo.*"], ["Repo.Skip"], ["https://repo-feed/index.json"], ["Repo.Ignore"], "Major", true, "0 3 * * *");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement;

    private static ManagedRepository NewRepo(RepositorySettings? settings = null) =>
        new(Guid.NewGuid(), "contoso", "platform", "web-api", settings: settings);

    private static TargetingSettings Global() => new(
        ["Contoso.*"], [], ["https://global-feed/index.json"], new UpdatePolicy(UpdateStrategy.Minor, [], false));

    private (HttpClient Client, MemoryRepositoryStore Repos) Start(TargetingSettings? global = null, params ManagedRepository[] seed)
    {
        var repos = new MemoryRepositoryStore();
        foreach (var r in seed)
        {
            repos.Seed(r);
        }

        return (factory.CreateClient(repos, new MemorySettingsStore(global ?? Global())), repos);
    }

    // ---- GET by id / list ------------------------------------------------------------

    [Fact]
    public async Task Get_by_id_returns_the_dto_with_settings_and_list_includes_settings()
    {
        var repo = NewRepo(new RepositorySettings(false, ["Repo.*"], strategy: UpdateStrategy.Patch));
        var (client, _) = Start(null, repo);

        var one = await client.GetFromJsonAsync<ManagedRepositoryDto>($"/api/repositories/{repo.Id}", Ct);
        var list = await client.GetFromJsonAsync<List<ManagedRepositoryDto>>("/api/repositories", Ct);

        Assert.False(one!.Settings!.InheritGlobal);
        Assert.Equal(["Repo.*"], one.Settings.Patterns);
        Assert.Equal("Patch", one.Settings.Strategy);
        Assert.Equal("Patch", Assert.Single(list!).Settings!.Strategy);
    }

    // ---- POST / PUT repositories -----------------------------------------------------

    [Fact]
    public async Task Post_with_settings_stores_them_and_without_means_inherit()
    {
        var (client, repos) = Start();

        var withSettings = await client.PostAsJsonAsync(
            "/api/repositories", new ManagedRepositoryInput("o", "p", "with", Settings: Sample), Ct);
        var without = await client.PostAsJsonAsync("/api/repositories", new ManagedRepositoryInput("o", "p", "without"), Ct);

        Assert.Equal(HttpStatusCode.Created, withSettings.StatusCode);
        Assert.Equal(HttpStatusCode.Created, without.StatusCode);
        var all = await repos.ListAsync(Ct);
        var stored = all.Single(r => r.Name == "with").Settings;
        Assert.Equal(["Repo.*"], stored.Patterns);
        Assert.Equal(UpdateStrategy.Major, stored.Strategy);
        Assert.True(stored.AllowPrerelease);
        Assert.Equal("0 3 * * *", stored.Schedule);
        Assert.False(all.Single(r => r.Name == "without").Settings.HasOverrides);
        Assert.True(all.Single(r => r.Name == "without").Settings.InheritGlobal);
    }

    [Fact]
    public async Task Post_with_invalid_settings_is_400_and_creates_nothing()
    {
        var (client, repos) = Start();

        var response = await client.PostAsJsonAsync(
            "/api/repositories", new ManagedRepositoryInput("o", "p", "bad", Settings: Sample with { Schedule = "*/5 * * * *" }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await ReadAsync(response)).GetProperty("errors").GetProperty("schedule").EnumerateArray());
        Assert.Empty(await repos.ListAsync(Ct));
    }

    [Fact]
    public async Task Put_without_settings_keeps_the_stored_settings_and_with_settings_replaces_them()
    {
        var repo = NewRepo(new RepositorySettings(true, ["Keep.*"], strategy: UpdateStrategy.Patch));
        var (client, repos) = Start(null, repo);

        // Old-client body: no settings property at all.
        var oldBody = """{"organization":"contoso","project":"platform","name":"renamed","enabled":false,"targetBranch":"develop"}""";
        var kept = await client.PutAsync($"/api/repositories/{repo.Id}", new StringContent(oldBody, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        var afterKeep = (await repos.GetAsync(repo.Id, Ct))!;
        Assert.Equal("renamed", afterKeep.Name);
        Assert.Equal(["Keep.*"], afterKeep.Settings.Patterns);
        Assert.Equal(UpdateStrategy.Patch, afterKeep.Settings.Strategy);

        var replaced = await client.PutAsJsonAsync(
            $"/api/repositories/{repo.Id}", new ManagedRepositoryInput("contoso", "platform", "renamed", Settings: Sample), Ct);

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var afterReplace = (await repos.GetAsync(repo.Id, Ct))!.Settings;
        Assert.Equal(["Repo.*"], afterReplace.Patterns);
        Assert.Equal(UpdateStrategy.Major, afterReplace.Strategy);
    }

    [Fact]
    public async Task Put_with_invalid_settings_is_400_and_keeps_the_stored_repository()
    {
        var repo = NewRepo(new RepositorySettings(true, ["Keep.*"]));
        var (client, repos) = Start(null, repo);

        var response = await client.PutAsJsonAsync(
            $"/api/repositories/{repo.Id}",
            new ManagedRepositoryInput("contoso", "platform", "web-api", Settings: Sample with { Strategy = "Nope" }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Keep.*"], (await repos.GetAsync(repo.Id, Ct))!.Settings.Patterns);
    }

    // ---- /settings -------------------------------------------------------------------

    [Fact]
    public async Task Settings_get_and_put_round_trip()
    {
        var repo = NewRepo();
        var (client, repos) = Start(null, repo);

        var put = await client.PutAsJsonAsync($"/api/repositories/{repo.Id}/settings", Sample, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(Sample.Patterns, (await put.Content.ReadFromJsonAsync<RepositorySettingsDto>(Ct))!.Patterns);

        var got = await client.GetFromJsonAsync<RepositorySettingsDto>($"/api/repositories/{repo.Id}/settings", Ct);
        Assert.Equal("Major", got!.Strategy);
        Assert.True(got.AllowPrerelease);
        Assert.Equal("0 3 * * *", got.Schedule);
        Assert.Equal(Sample.Excludes, got.Excludes);
        Assert.Equal(Sample.Feeds, got.Feeds);
        Assert.Equal(Sample.Ignore, got.Ignore);
        Assert.Equal(["Repo.*"], (await repos.GetAsync(repo.Id, Ct))!.Settings.Patterns);
    }

    [Theory]
    [InlineData("not a cron", "Invalid cron expression")]
    [InlineData("0 0 0 * * *", "Seconds are not supported")]
    [InlineData("*/5 * * * *", "more frequent than every 15 minutes")]
    [InlineData("0 0 31 2 *", "never fires")]
    public async Task Settings_put_with_invalid_cron_is_400_with_a_schedule_error(string cron, string expected)
    {
        var repo = NewRepo();
        var (client, _) = Start(null, repo);

        var response = await client.PutAsJsonAsync($"/api/repositories/{repo.Id}/settings", Sample with { Schedule = cron }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, (await ReadAsync(response)).GetProperty("errors").GetProperty("schedule")[0].GetString());
    }

    [Fact]
    public async Task Settings_put_with_unknown_strategy_is_400_with_a_strategy_error()
    {
        var repo = NewRepo();
        var (client, repos) = Start(null, repo);

        var response = await client.PutAsJsonAsync($"/api/repositories/{repo.Id}/settings", Sample with { Strategy = "Nope" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadAsync(response)).GetProperty("errors").TryGetProperty("strategy", out _));
        Assert.False((await repos.GetAsync(repo.Id, Ct))!.Settings.HasOverrides);
    }

    [Fact]
    public async Task Settings_put_rejects_comma_blank_and_overlong_entries()
    {
        var repo = NewRepo();
        var (client, _) = Start(null, repo);

        var response = await client.PutAsJsonAsync(
            $"/api/repositories/{repo.Id}/settings",
            Sample with { Patterns = ["a,b"], Excludes = [" "], Ignore = [new string('x', 201)] }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("patterns", out _));
        Assert.True(errors.TryGetProperty("excludes", out _));
        Assert.True(errors.TryGetProperty("ignore", out _));
    }

    [Fact]
    public async Task Settings_put_accepts_a_blank_schedule_and_a_null_strategy_as_inherit()
    {
        var repo = NewRepo();
        var (client, repos) = Start(null, repo);

        var response = await client.PutAsJsonAsync(
            $"/api/repositories/{repo.Id}/settings", Sample with { Schedule = "  ", Strategy = null, AllowPrerelease = null }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await repos.GetAsync(repo.Id, Ct))!.Settings;
        Assert.Null(stored.Schedule);
        Assert.Null(stored.Strategy);
        Assert.Null(stored.AllowPrerelease);
    }

    // ---- /effective-settings ---------------------------------------------------------

    [Fact]
    public async Task Effective_settings_with_inheritance_shows_the_union_with_sources()
    {
        var repo = NewRepo(new RepositorySettings(true, ["contoso.*", "Repo.*"], feeds: ["https://repo-feed/index.json"], strategy: UpdateStrategy.Major));
        var (client, _) = Start(null, repo);

        var dto = await client.GetFromJsonAsync<EffectiveSettingsDto>($"/api/repositories/{repo.Id}/effective-settings", Ct);

        Assert.Equal(repo.Id, dto!.RepositoryId);
        Assert.True(dto.InheritGlobal);
        Assert.Equal(
            [("Contoso.*", "Both"), ("Repo.*", "Repository")],
            dto.Patterns.Select(p => (p.Value, p.Source)).ToArray());
        Assert.Equal(
            [("https://global-feed/index.json", "Global"), ("https://repo-feed/index.json", "Repository")],
            dto.Feeds.Select(p => (p.Value, p.Source)).ToArray());
        Assert.Equal(new EffectiveValueDto("Major", "Repository"), dto.Strategy);
        Assert.Equal(new EffectiveValueDto("false", "Global"), dto.AllowPrerelease);
        Assert.Equal(new EffectiveValueDto("0 0 * * *", "Default"), dto.Schedule);
        Assert.Empty(dto.Warnings);
    }

    [Fact]
    public async Task Effective_settings_with_inheritance_off_uses_only_repository_values_and_defaults()
    {
        var global = new TargetingSettings(
            ["Contoso.*"], [], ["https://global-feed/index.json"], new UpdatePolicy(UpdateStrategy.Major, [], true), "0 */6 * * *");
        var repo = NewRepo(new RepositorySettings(false, ["Local.*"], feeds: ["https://local/index.json"]));
        var (client, _) = Start(global, repo);

        var dto = await client.GetFromJsonAsync<EffectiveSettingsDto>($"/api/repositories/{repo.Id}/effective-settings", Ct);

        Assert.False(dto!.InheritGlobal);
        Assert.Equal([("Local.*", "Repository")], dto.Patterns.Select(p => (p.Value, p.Source)).ToArray());
        Assert.Equal(new EffectiveValueDto("Minor", "Default"), dto.Strategy);
        Assert.Equal(new EffectiveValueDto("false", "Default"), dto.AllowPrerelease);
        // The inheritance switch does not cover the schedule.
        Assert.Equal(new EffectiveValueDto("0 */6 * * *", "Global"), dto.Schedule);
    }

    [Fact]
    public async Task Effective_settings_warns_when_inheritance_is_off_and_there_are_no_patterns()
    {
        var repo = NewRepo(new RepositorySettings(false));
        var (client, _) = Start(null, repo);

        var dto = await client.GetFromJsonAsync<EffectiveSettingsDto>($"/api/repositories/{repo.Id}/effective-settings", Ct);

        Assert.Empty(dto!.Patterns);
        Assert.Contains(dto.Warnings, w => w.StartsWith("No package patterns", StringComparison.Ordinal));
    }

    // ---- unknown ids -----------------------------------------------------------------

    [Fact]
    public async Task Unknown_repository_is_404_on_every_new_route()
    {
        var (client, _) = Start();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/repositories/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/repositories/{id}/settings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/repositories/{id}/settings", Sample, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/repositories/{id}/effective-settings", Ct)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/repositories/{id}", new ManagedRepositoryInput("o", "p", "n", Settings: Sample), Ct)).StatusCode);
    }
}
