using System.Net;
using System.Text;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;

namespace AutoRemediator.Infrastructure.Tests;

public class AzureDevOpsClientTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "contoso", "platform", "web-api");

    private static AzureDevOpsClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/contoso/") });

    [Fact]
    public async Task GetBranchHead_returns_object_id()
    {
        var handler = new StubHandler(_ => Json("""
            { "value": [ { "name": "refs/heads/autoremediator/dependency-updates", "objectId": "abc123" } ] }
            """));

        var head = await Client(handler).GetBranchHeadAsync(Repo, "autoremediator/dependency-updates", TestContext.Current.CancellationToken);

        Assert.Equal("abc123", head);
    }

    [Fact]
    public async Task EnsurePullRequest_reuses_active_pr()
    {
        var handler = new StubHandler(req =>
            req.Method == HttpMethod.Get
                ? Json("""{ "value": [ { "pullRequestId": 42 } ] }""")
                : throw new InvalidOperationException("should not POST when an active PR exists"));

        var url = await Client(handler).EnsurePullRequestAsync(Repo, "src", "main", "t", "d", TestContext.Current.CancellationToken);

        Assert.Contains("/pullrequest/42", url);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task EnsurePullRequest_creates_when_none_active()
    {
        var handler = new StubHandler(req =>
            req.Method == HttpMethod.Get
                ? Json("""{ "value": [] }""")
                : Json("""{ "pullRequestId": 7 }"""));

        var url = await Client(handler).EnsurePullRequestAsync(Repo, "src", "main", "t", "d", TestContext.Current.CancellationToken);

        Assert.Contains("/pullrequest/7", url);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery.Contains("/pullrequests"));
    }

    [Fact]
    public async Task PushFiles_posts_to_pushes()
    {
        var handler = new StubHandler(_ => Json("""{ }""", HttpStatusCode.Created));

        await Client(handler).PushFilesAsync(
            Repo, "autoremediator/dependency-updates", "base-commit",
            [new FileChange("/Directory.Packages.props", "<Project/>")], "msg", TestContext.Current.CancellationToken);

        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery.Contains("/pushes"));
    }

    [Fact]
    public async Task GetRepositoryArchive_requests_a_zip_at_the_commit()
    {
        var payload = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 }; // PK.. zip magic
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        });

        await using var archive = await Client(handler).GetRepositoryArchiveAsync(
            Repo, "abc123", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        var query = request.RequestUri!.PathAndQuery;
        Assert.Contains("/items", query);
        Assert.Contains("$format=zip", query);
        Assert.Contains("download=true", query);
        Assert.Contains("versionDescriptor.version=abc123", query);
        Assert.Contains("versionDescriptor.versionType=commit", query);
        Assert.Contains("recursionLevel=Full", query);

        var buffer = new byte[payload.Length];
        Assert.Equal(payload.Length, await archive.ReadAsync(buffer, TestContext.Current.CancellationToken));
        Assert.Equal(payload, buffer);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetRepositoryArchive_propagates_failure(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetRepositoryArchiveAsync(
            Repo, "abc123", TestContext.Current.CancellationToken));
    }

    // ---- Staleness classification (Decision 5) -----------------------------------------

    [Theory]
    [InlineData("GitRefUpdateStaleException")]
    [InlineData("GitRefUpdateOldObjectIdMismatchException")]
    [InlineData("GitItemNotFoundException")]
    public async Task PushFiles_classifies_a_refused_ref_update_as_PushRejected(string typeKey)
    {
        var handler = new StubHandler(_ => Json(
            $$"""{ "$id": "1", "typeKey": "{{typeKey}}", "message": "refused" }""",
            HttpStatusCode.Conflict));

        var ex = await Assert.ThrowsAsync<PushRejectedException>(() => Client(handler).PushFilesAsync(
            Repo, "autoremediator/dependency-updates", "base-commit",
            [new FileChange("/Directory.Packages.props", "<Project/>")], "msg", TestContext.Current.CancellationToken));

        Assert.Equal(typeKey, ex.TypeKey);
    }

    [Fact]
    public async Task PushFiles_keeps_throwing_what_it_throws_today_for_an_unrelated_failure()
    {
        var handler = new StubHandler(_ => Json(
            """{ "$id": "1", "typeKey": "SomeOtherAzureDevOpsException", "message": "boom" }""",
            HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).PushFilesAsync(
            Repo, "autoremediator/dependency-updates", "base-commit",
            [new FileChange("/Directory.Packages.props", "<Project/>")], "msg", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PushFiles_keeps_throwing_HttpRequestException_when_the_body_is_not_json()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>bad gateway</html>", Encoding.UTF8, "text/html"),
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).PushFilesAsync(
            Repo, "autoremediator/dependency-updates", "base-commit",
            [new FileChange("/Directory.Packages.props", "<Project/>")], "msg", TestContext.Current.CancellationToken));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
