using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoRemediator.Contracts.Dtos;
using AutoRemediator.Domain;

namespace AutoRemediator.Api.Tests;

public class SettingsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TargetingSettingsDto Valid = new(
        ["Contoso.*", "Fabrikam.*"], ["Contoso.Legacy"], ["https://feed/v3/index.json"], "Patch", ["Tailspin.*"], true, "0 6 * * 1");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct)).RootElement;

    [Fact]
    public async Task Put_round_trips_every_field()
    {
        var store = new MemorySettingsStore();
        var client = factory.CreateClient(new MemoryRepositoryStore(), store);

        var put = await client.PutAsJsonAsync("/api/settings", Valid, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var got = await client.GetFromJsonAsync<TargetingSettingsDto>("/api/settings", Ct);
        Assert.NotNull(got);
        Assert.Equal(Valid.Patterns, got.Patterns);
        Assert.Equal(Valid.Excludes, got.Excludes);
        Assert.Equal(Valid.Feeds, got.Feeds);
        Assert.Equal("Patch", got.Strategy);
        Assert.Equal(Valid.Ignore, got.Ignore);
        Assert.True(got.AllowPrerelease);
        Assert.Equal("0 6 * * 1", got.DefaultSchedule);
        Assert.Equal(UpdateStrategy.Patch, (await store.GetAsync(Ct)).Policy.Strategy);
    }

    [Fact]
    public async Task Old_client_body_without_defaultSchedule_is_accepted()
    {
        var client = factory.CreateClient(new MemoryRepositoryStore(), new MemorySettingsStore());
        var body = """{"patterns":["Contoso.*"],"excludes":[],"feeds":[],"strategy":"minor","ignore":[],"allowPrerelease":false}""";

        var response = await client.PutAsync("/api/settings", new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var got = await client.GetFromJsonAsync<TargetingSettingsDto>("/api/settings", Ct);
        Assert.Null(got!.DefaultSchedule);
        Assert.Equal("Minor", got.Strategy);
    }

    [Fact]
    public async Task Unknown_strategy_is_400_with_a_strategy_error_and_nothing_is_saved()
    {
        var store = new MemorySettingsStore();
        var client = factory.CreateClient(new MemoryRepositoryStore(), store);

        var response = await client.PutAsJsonAsync("/api/settings", Valid with { Strategy = "Reckless" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await ReadAsync(response)).GetProperty("errors").GetProperty("strategy").EnumerateArray());
        Assert.Empty((await store.GetAsync(Ct)).Patterns);
    }

    [Theory]
    [InlineData("not a cron", "Invalid cron expression")]
    [InlineData("0 0 0 * * *", "Seconds are not supported")]
    [InlineData("*/5 * * * *", "more frequent than every 15 minutes")]
    [InlineData("0 0 31 2 *", "never fires")]
    public async Task Invalid_defaultSchedule_is_400_with_a_defaultSchedule_error(string cron, string expected)
    {
        var client = factory.CreateClient(new MemoryRepositoryStore(), new MemorySettingsStore());

        var response = await client.PutAsJsonAsync("/api/settings", Valid with { DefaultSchedule = cron }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = (await ReadAsync(response)).GetProperty("errors").GetProperty("defaultSchedule")[0].GetString();
        Assert.Contains(expected, message);
    }

    [Fact]
    public async Task List_entries_with_commas_or_blank_are_rejected_per_field()
    {
        var client = factory.CreateClient(new MemoryRepositoryStore(), new MemorySettingsStore());

        var response = await client.PutAsJsonAsync("/api/settings", Valid with { Patterns = ["a,b"], Feeds = [" "] }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("patterns", out _));
        Assert.True(errors.TryGetProperty("feeds", out _));
    }
}
