using System.Net;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

public sealed class GitHubToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly GitHubTool _tool;

    public GitHubToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        _tool = new GitHubTool(personalAccessToken: "test-token", httpClient: _httpClient);
    }

    // ── list_issues ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListIssues_ShouldReturnItems_WhenRepoHasIssues()
    {
        var json = """
        [
            {
                "number": 1,
                "title": "Bug report",
                "html_url": "https://github.com/octocat/hello/issues/1",
                "state": "open",
                "body": "Something is broken",
                "created_at": "2026-01-15T10:00:00Z"
            },
            {
                "number": 2,
                "title": "Feature request",
                "html_url": "https://github.com/octocat/hello/issues/2",
                "state": "open",
                "body": "Please add X",
                "created_at": "2026-01-16T12:00:00Z"
            }
        ]
        """;
        SetupResponse(HttpStatusCode.OK, json);

        var result = await CallToolAsync("list_issues", owner: "octocat", repo: "hello");

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("list_issues", dict["action"]);
        Assert.Equal(2, dict["total_count"]);
        Assert.True((bool)dict["success"]!);
    }

    [Fact]
    public async Task ListIssues_ShouldSetCorrectRequestUrl()
    {
        SetupResponse(HttpStatusCode.OK, "[]");

        await CallToolAsync("list_issues", owner: "octocat", repo: "hello-world");

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal("https://api.github.com/repos/octocat/hello-world/issues",
            _mockHandler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ListIssues_ShouldSendAuthorizationHeader_WhenTokenProvided()
    {
        SetupResponse(HttpStatusCode.OK, "[]");

        await CallToolAsync("list_issues", owner: "octocat", repo: "hello");

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal("Bearer", _mockHandler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", _mockHandler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ListIssues_ShouldSendUserAgentHeader()
    {
        SetupResponse(HttpStatusCode.OK, "[]");

        await CallToolAsync("list_issues", owner: "octocat", repo: "hello");

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Contains(_mockHandler.LastRequest.Headers.UserAgent,
            ua => ua.Product?.Name == "Orkeon");
    }

    // ── create_issue ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateIssue_ShouldReturnCreatedItem_WhenSuccessful()
    {
        var responseJson = """
        {
            "number": 42,
            "title": "New issue",
            "html_url": "https://github.com/octocat/hello/issues/42",
            "state": "open",
            "body": "Issue body text",
            "created_at": "2026-03-27T08:00:00Z"
        }
        """;
        SetupResponse(HttpStatusCode.Created, responseJson);

        var result = await CallToolAsync("create_issue",
            owner: "octocat", repo: "hello", title: "New issue", body: "Issue body text");

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("create_issue", dict["action"]);
        Assert.Equal(1, dict["total_count"]);
        Assert.True((bool)dict["success"]!);
    }

    [Fact]
    public async Task CreateIssue_ShouldSendPostRequest_WithJsonBody()
    {
        SetupResponse(HttpStatusCode.Created, """{"number":1,"title":"T","html_url":"","state":"open","body":"","created_at":""}""");

        await CallToolAsync("create_issue",
            owner: "octocat", repo: "hello", title: "My Title", body: "My Body");

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, _mockHandler.LastRequest.Method);
        Assert.Equal("https://api.github.com/repos/octocat/hello/issues",
            _mockHandler.LastRequest.RequestUri?.ToString());

        var requestBody = await _mockHandler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("My Title", requestBody);
        Assert.Contains("My Body", requestBody);
    }

    [Fact]
    public async Task CreateIssue_ShouldReturnError_WhenTitleMissing()
    {
        var result = await CallToolAsync("create_issue", owner: "octocat", repo: "hello");

        Assert.False(result.Success);
        Assert.Contains("Title is required", result.Error);
    }

    // ── get_pr ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPr_ShouldReturnPullRequest_WhenFound()
    {
        var json = """
        {
            "number": 99,
            "title": "Fix typo in README",
            "html_url": "https://github.com/octocat/hello/pull/99",
            "state": "open",
            "body": "Fixed a small typo",
            "created_at": "2026-02-10T14:30:00Z"
        }
        """;
        SetupResponse(HttpStatusCode.OK, json);

        var result = await CallToolAsync("get_pr", owner: "octocat", repo: "hello", number: 99);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("get_pr", dict["action"]);
        Assert.Equal(1, dict["total_count"]);
    }

    [Fact]
    public async Task GetPr_ShouldSetCorrectRequestUrl()
    {
        SetupResponse(HttpStatusCode.OK, """{"number":5,"title":"PR","html_url":"","state":"open","body":"","created_at":""}""");

        await CallToolAsync("get_pr", owner: "octocat", repo: "hello", number: 5);

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal("https://api.github.com/repos/octocat/hello/pulls/5",
            _mockHandler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetPr_ShouldReturnError_WhenNumberMissing()
    {
        var result = await CallToolAsync("get_pr", owner: "octocat", repo: "hello");

        Assert.False(result.Success);
        Assert.Contains("Number is required", result.Error);
    }

    [Fact]
    public async Task GetPr_ShouldReturnError_WhenNotFound()
    {
        SetupResponse(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        var result = await CallToolAsync("get_pr", owner: "octocat", repo: "hello", number: 999);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("404", result.Error!);
    }

    // ── search_repos ─────────────────────────────────────────────────────

    [Fact]
    public async Task SearchRepos_ShouldReturnResults_WhenQueryMatches()
    {
        var json = """
        {
            "total_count": 2,
            "items": [
                {
                    "id": 100,
                    "full_name": "octocat/hello-world",
                    "html_url": "https://github.com/octocat/hello-world",
                    "archived": false,
                    "description": "Hello World repository",
                    "created_at": "2020-01-01T00:00:00Z"
                },
                {
                    "id": 200,
                    "full_name": "octocat/hello-again",
                    "html_url": "https://github.com/octocat/hello-again",
                    "archived": true,
                    "description": null,
                    "created_at": "2021-06-15T00:00:00Z"
                }
            ]
        }
        """;
        SetupResponse(HttpStatusCode.OK, json);

        var result = await CallToolAsync("search_repos", query: "hello");

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("search_repos", dict["action"]);
        Assert.Equal(2, dict["total_count"]);
        Assert.True((bool)dict["success"]!);
    }

    [Fact]
    public async Task SearchRepos_ShouldUrlEncodeQuery()
    {
        SetupResponse(HttpStatusCode.OK, """{"total_count":0,"items":[]}""");

        await CallToolAsync("search_repos", query: "language:csharp stars:>100");

        Assert.NotNull(_mockHandler.LastRequest);
        var url = _mockHandler.LastRequest.RequestUri?.AbsoluteUri;
        Assert.NotNull(url);
        Assert.Contains("search/repositories?q=", url);
        // URL-encoded query: spaces should be encoded as %20
        Assert.DoesNotContain(" ", url.Split("?q=")[1]);
    }

    [Fact]
    public async Task SearchRepos_ShouldReturnError_WhenQueryMissing()
    {
        var result = await CallToolAsync("search_repos");

        Assert.False(result.Success);
        Assert.Contains("Query is required", result.Error);
    }

    // ── get_repo ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRepo_ShouldReturnRepository_WhenFound()
    {
        var json = """
        {
            "id": 1296269,
            "full_name": "octocat/Hello-World",
            "html_url": "https://github.com/octocat/Hello-World",
            "archived": false,
            "description": "My first repository on GitHub!",
            "created_at": "2011-01-26T19:01:12Z"
        }
        """;
        SetupResponse(HttpStatusCode.OK, json);

        var result = await CallToolAsync("get_repo", owner: "octocat", repo: "Hello-World");

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal("get_repo", dict["action"]);
        Assert.Equal(1, dict["total_count"]);
        Assert.True((bool)dict["success"]!);
    }

    [Fact]
    public async Task GetRepo_ShouldReturnError_WhenNotFound()
    {
        SetupResponse(HttpStatusCode.NotFound, """{"message":"Not Found"}""");

        var result = await CallToolAsync("get_repo", owner: "octocat", repo: "nonexistent");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("404", result.Error!);
    }

    // ── Validation tests ─────────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenActionMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "github",
            Parameters: new Dictionary<string, object?>
            {
                ["owner"] = "octocat",
                ["repo"] = "hello"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("action", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenActionIsInvalid()
    {
        var result = await CallToolAsync("invalid_action", owner: "octocat", repo: "hello");

        Assert.False(result.Success);
        Assert.Contains("Unsupported action", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenOwnerMissing_ForActionRequiringIt()
    {
        var request = new ToolCallRequest(
            ToolName: "github",
            Parameters: new Dictionary<string, object?>
            {
                ["action"] = "list_issues",
                ["repo"] = "hello"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Owner is required", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenRepoMissing_ForActionRequiringIt()
    {
        var request = new ToolCallRequest(
            ToolName: "github",
            Parameters: new Dictionary<string, object?>
            {
                ["action"] = "list_issues",
                ["owner"] = "octocat"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Repo is required", result.Error);
    }

    // ── HTTP error handling ──────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenApiReturns401()
    {
        SetupResponse(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");

        var result = await CallToolAsync("get_repo", owner: "octocat", repo: "private-repo");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("401", result.Error!);
    }

    // ── Schema validation ────────────────────────────────────────────────

    [Fact]
    public void ShouldHaveCorrectToolName()
    {
        Assert.Equal("github", _tool.Name);
    }

    [Fact]
    public void ShouldHaveCorrectCategory()
    {
        Assert.Equal("Web Operations", _tool.Category);
    }

    [Fact]
    public void ShouldHaveSchemaWithActionParameter()
    {
        var schema = _tool.Schema;
        Assert.NotNull(schema);
        Assert.True(schema.Parameters.ContainsKey("action"));
        Assert.True(schema.Parameters["action"].Required);
    }

    // ── No PAT ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldWorkWithoutToken_ForPublicApis()
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, "[]");
        var client = new HttpClient(handler, disposeHandler: false);
        var toolWithoutToken = new GitHubTool(personalAccessToken: null, httpClient: client);

        var request = new ToolCallRequest(
            ToolName: "github",
            Parameters: new Dictionary<string, object?>
            {
                ["action"] = "list_issues",
                ["owner"] = "octocat",
                ["repo"] = "hello"
            }
        );

        var result = await toolWithoutToken.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Null(handler.LastRequest?.Headers.Authorization);

        client.Dispose();
        toolWithoutToken.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void SetupResponse(HttpStatusCode statusCode, string content)
    {
        _mockHandler.SetResponse(statusCode, content, "application/json");
    }

    private async Task<ToolCallResponse> CallToolAsync(
        string action,
        string? owner = null,
        string? repo = null,
        int? number = null,
        string? title = null,
        string? body = null,
        string? query = null)
    {
        var parameters = new Dictionary<string, object?> { ["action"] = action };

        if (owner is not null) parameters["owner"] = owner;
        if (repo is not null) parameters["repo"] = repo;
        if (number is not null) parameters["number"] = number;
        if (title is not null) parameters["title"] = title;
        if (body is not null) parameters["body"] = body;
        if (query is not null) parameters[ParamQuery] = query;

        var request = new ToolCallRequest(ToolName: "github", Parameters: parameters);
        return await _tool.CallAsync(request);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
