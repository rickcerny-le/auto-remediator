using System.Net;
using System.Text;
using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.AzureDevOps;

namespace AutoRemediator.Infrastructure.Tests;

public class AzureDevOpsClientTests
{
    private static readonly ManagedRepository Repo = new(Guid.NewGuid(), "orion180", "platform", "web-api");

    private static AzureDevOpsClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://dev.azure.com/orion180/") });

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
