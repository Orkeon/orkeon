using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Web;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Strongly-typed request for <see cref="GitHubTool"/>.</summary>
public sealed record GitHubRequest
{
    /// <summary>Gets the action to perform.</summary>
    [FieldSchema(Description = "Action to perform: list_issues, create_issue, get_pr, search_repos, get_repo", IsRequired = true)]
    public string Action { get; init; } = "";

    /// <summary>Gets the repository owner (GitHub username or organization).</summary>
    [FieldSchema(Description = "Repository owner (GitHub username or organization)", IsRequired = false)]
    public string Owner { get; init; } = "";

    /// <summary>Gets the repository name.</summary>
    [FieldSchema(Description = "Repository name", IsRequired = false)]
    public string Repo { get; init; } = "";

    /// <summary>Gets the issue or PR number (for get operations).</summary>
    [FieldSchema(Description = "Issue or PR number (for get operations)", IsRequired = false)]
    public int? Number { get; init; }

    /// <summary>Gets the title for create operations.</summary>
    [FieldSchema(Description = "Title for create operations", IsRequired = false)]
    public string? Title { get; init; }

    /// <summary>Gets the body content for create operations.</summary>
    [FieldSchema(Description = "Body content for create operations", IsRequired = false)]
    public string? Body { get; init; }

    /// <summary>Gets the search query string for search operations.</summary>
    [FieldSchema(Description = "Search query string for search operations", IsRequired = false)]
    public string? Query { get; init; }
}

/// <summary>Strongly-typed response for <see cref="GitHubTool"/>.</summary>
public sealed record GitHubResponse
{
    /// <summary>Gets the list of result items.</summary>
    [ReturnSchema(Description = "List of result items")]
    public IReadOnlyList<GitHubItem> Items { get; init; } = [];

    /// <summary>Gets the total count of results.</summary>
    [ReturnSchema(Description = "Total count of results")]
    public int TotalCount { get; init; }

    /// <summary>Gets the action that was performed.</summary>
    [ReturnSchema(Description = "The action that was performed")]
    public string Action { get; init; } = "";

    /// <summary>Gets whether the operation succeeded.</summary>
    [ReturnSchema(Description = "Whether the operation succeeded")]
    public bool Success { get; init; }

    /// <summary>Gets the error message if operation failed.</summary>
    [ReturnSchema(Description = "Error message if operation failed")]
    public string? Error { get; init; }
}

/// <summary>Represents a single GitHub item (issue, PR, or repository).</summary>
public sealed record GitHubItem
{
    /// <summary>Gets the issue or PR number.</summary>
    public int Number { get; init; }

    /// <summary>Gets the title.</summary>
    public string Title { get; init; } = "";

    /// <summary>Gets the URL.</summary>
    public Uri? Url { get; init; }

    /// <summary>Gets the state (open, closed, etc.).</summary>
    public string State { get; init; } = "";

    /// <summary>Gets the body content.</summary>
    public string Body { get; init; } = "";

    /// <summary>Gets the creation date as ISO 8601 string.</summary>
    public string CreatedAt { get; init; } = "";
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Tool for interacting with GitHub API v3 (REST).
/// Supports listing/creating issues, reading PRs, and searching repositories.
/// </summary>
[ToolContract("github",
    Name = "github",
    Description = "Interact with GitHub API v3: list/create issues, read PRs, search repos.",
    Category = "Web Operations")]
public partial class GitHubTool : HttpToolBase<GitHubRequest, GitHubResponse>
{
    private const string BaseUrl = "https://api.github.com";

    private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "list_issues", "create_issue", "get_pr", "search_repos", "get_repo"
    };

    private static readonly HashSet<string> ActionsRequiringOwnerRepo = new(StringComparer.OrdinalIgnoreCase)
    {
        "list_issues", "create_issue", "get_pr", "get_repo"
    };

    private readonly string? _personalAccessToken;

    /// <summary>Initializes a new instance of <see cref="GitHubTool"/>.</summary>
    /// <param name="personalAccessToken">Optional GitHub personal access token for authenticated requests.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public GitHubTool(string? personalAccessToken = null, HttpClient? httpClient = null, ILogger? logger = null)
        : base(httpClient, logger)
    {
        _personalAccessToken = personalAccessToken;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(GitHubRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Action))
            return "Action is required. Supported actions: list_issues, create_issue, get_pr, search_repos, get_repo";

        if (!ValidActions.Contains(request.Action))
            return $"Unsupported action: {request.Action}. Supported actions: list_issues, create_issue, get_pr, search_repos, get_repo";

        if (ActionsRequiringOwnerRepo.Contains(request.Action))
        {
            if (string.IsNullOrWhiteSpace(request.Owner))
                return $"Owner is required for action '{request.Action}'";
            if (string.IsNullOrWhiteSpace(request.Repo))
                return $"Repo is required for action '{request.Action}'";
        }

        if (string.Equals(request.Action, "get_pr", StringComparison.OrdinalIgnoreCase) && request.Number is null)
            return "Number is required for action 'get_pr'";

        if (string.Equals(request.Action, "create_issue", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(request.Title))
            return "Title is required for action 'create_issue'";

        if (string.Equals(request.Action, "search_repos", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(request.Query))
            return "Query is required for action 'search_repos'";

        return null;
    }

    /// <inheritdoc />
    protected override Task<GitHubResponse> ExecuteTypedAsync(
        GitHubRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<GitHubResponse> ExecuteTypedCoreAsync()
        {
            ConfigureDefaultHeaders();

            try
            {
#pragma warning disable CA1308 // request.Action is normalized to the lowercase switch keys ("list_issues", ...), a required match form, not a comparison normalization
                return request.Action.ToLowerInvariant() switch
#pragma warning restore CA1308
                {
                    "list_issues" => await ListIssuesAsync(request, cancellationToken).ConfigureAwait(false),
                    "create_issue" => await CreateIssueAsync(request, cancellationToken).ConfigureAwait(false),
                    "get_pr" => await GetPullRequestAsync(request, cancellationToken).ConfigureAwait(false),
                    "search_repos" => await SearchRepositoriesAsync(request, cancellationToken).ConfigureAwait(false),
                    "get_repo" => await GetRepositoryAsync(request, cancellationToken).ConfigureAwait(false),
                    _ => new GitHubResponse
                    {
                        Success = false,
                        Action = request.Action,
                        Error = $"Unsupported action: {request.Action}"
                    }
                };
            }
            catch (HttpRequestException ex)
            {
                LogGitHubRequestFailed(ex, request.Action);
                return new GitHubResponse
                {
                    Success = false,
                    Action = request.Action,
                    Error = $"GitHub API request failed: {ex.Message}"
                };
            }
        }
    }

    private void ConfigureDefaultHeaders()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("Orkeon");

        if (!_httpClient.DefaultRequestHeaders.Accept.Any(a =>
                a.MediaType == "application/vnd.github.v3+json"))
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        }

        if (!string.IsNullOrWhiteSpace(_personalAccessToken) &&
            _httpClient.DefaultRequestHeaders.Authorization is null)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _personalAccessToken);
        }
    }

    private async Task<GitHubResponse> ListIssuesAsync(GitHubRequest request, CancellationToken ct)
    {
        var url = $"{BaseUrl}/repos/{request.Owner}/{request.Repo}/issues";
        var json = await _httpClient.GetStringAsync(new Uri(url), ct).ConfigureAwait(false);
        var items = ParseItemArray(json);

        LogGitHubAction("list_issues", request.Owner, request.Repo, items.Count);

        return new GitHubResponse
        {
            Success = true,
            Action = "list_issues",
            Items = items,
            TotalCount = items.Count
        };
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Request content ownership is transferred to HttpClient.PostAsync.")]
    private async Task<GitHubResponse> CreateIssueAsync(GitHubRequest request, CancellationToken ct)
    {
        var url = $"{BaseUrl}/repos/{request.Owner}/{request.Repo}/issues";
        var payload = new Dictionary<string, string?> { ["title"] = request.Title, ["body"] = request.Body };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(new Uri(url), content, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new GitHubResponse
            {
                Success = false,
                Action = "create_issue",
                Error = $"GitHub API returned {(int)response.StatusCode}: {responseBody}"
            };
        }

        var item = ParseSingleItem(responseBody);

        LogGitHubAction("create_issue", request.Owner, request.Repo, 1);

        return new GitHubResponse
        {
            Success = true,
            Action = "create_issue",
            Items = item is not null ? [item] : [],
            TotalCount = item is not null ? 1 : 0
        };
    }

    private async Task<GitHubResponse> GetPullRequestAsync(GitHubRequest request, CancellationToken ct)
    {
        var url = $"{BaseUrl}/repos/{request.Owner}/{request.Repo}/pulls/{request.Number}";

        var response = await _httpClient.GetAsync(new Uri(url), ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new GitHubResponse
            {
                Success = false,
                Action = "get_pr",
                Error = $"GitHub API returned {(int)response.StatusCode}: {responseBody}"
            };
        }

        var item = ParseSingleItem(responseBody);

        LogGitHubAction("get_pr", request.Owner, request.Repo, 1);

        return new GitHubResponse
        {
            Success = true,
            Action = "get_pr",
            Items = item is not null ? [item] : [],
            TotalCount = item is not null ? 1 : 0
        };
    }

    private async Task<GitHubResponse> SearchRepositoriesAsync(GitHubRequest request, CancellationToken ct)
    {
        var encodedQuery = Uri.EscapeDataString(request.Query!);
        var url = $"{BaseUrl}/search/repositories?q={encodedQuery}";

        var json = await _httpClient.GetStringAsync(new Uri(url), ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var totalCount = root.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
        var items = new List<GitHubItem>();

        if (root.TryGetProperty("items", out var itemsArray))
        {
            foreach (var element in itemsArray.EnumerateArray())
            {
                items.Add(ParseRepoItem(element));
            }
        }

        LogGitHubAction("search_repos", request.Owner, request.Repo, items.Count);

        return new GitHubResponse
        {
            Success = true,
            Action = "search_repos",
            Items = items,
            TotalCount = totalCount
        };
    }

    private async Task<GitHubResponse> GetRepositoryAsync(GitHubRequest request, CancellationToken ct)
    {
        var url = $"{BaseUrl}/repos/{request.Owner}/{request.Repo}";

        var response = await _httpClient.GetAsync(new Uri(url), ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new GitHubResponse
            {
                Success = false,
                Action = "get_repo",
                Error = $"GitHub API returned {(int)response.StatusCode}: {responseBody}"
            };
        }

        using var doc = JsonDocument.Parse(responseBody);
        var item = ParseRepoItem(doc.RootElement);

        LogGitHubAction("get_repo", request.Owner, request.Repo, 1);

        return new GitHubResponse
        {
            Success = true,
            Action = "get_repo",
            Items = [item],
            TotalCount = 1
        };
    }

    // ── JSON parsing helpers ──────────────────────────────────────────────

    private static List<GitHubItem> ParseItemArray(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var items = new List<GitHubItem>();

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            items.Add(ParseIssueOrPrItem(element));
        }

        return items;
    }

    private static GitHubItem? ParseSingleItem(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseIssueOrPrItem(doc.RootElement);
    }

    private static GitHubItem ParseIssueOrPrItem(JsonElement element)
    {
        return new GitHubItem
        {
            Number = element.TryGetProperty("number", out var n) ? n.GetInt32() : 0,
            Title = element.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            Url = ParseHtmlUrl(element),
            State = element.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "",
            Body = element.TryGetProperty("body", out var b) && b.ValueKind != JsonValueKind.Null
                ? b.GetString() ?? ""
                : "",
            CreatedAt = element.TryGetProperty("created_at", out var c) ? c.GetString() ?? "" : ""
        };
    }

    private static GitHubItem ParseRepoItem(JsonElement element)
    {
        return new GitHubItem
        {
            Number = element.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Title = element.TryGetProperty("full_name", out var fn) ? fn.GetString() ?? "" : "",
            Url = ParseHtmlUrl(element),
            State = element.TryGetProperty("archived", out var a) && a.GetBoolean() ? "archived" : "active",
            Body = element.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null
                ? d.GetString() ?? ""
                : "",
            CreatedAt = element.TryGetProperty("created_at", out var c) ? c.GetString() ?? "" : ""
        };
    }

    /// <summary>
    /// Reads the <c>html_url</c> property and parses it as an absolute <see cref="Uri"/>,
    /// returning <see langword="null"/> when the property is absent or not a valid absolute URI.
    /// </summary>
    private static Uri? ParseHtmlUrl(JsonElement element)
    {
        if (!element.TryGetProperty("html_url", out var u))
            return null;

        return Uri.TryCreate(u.GetString(), UriKind.Absolute, out var parsed) ? parsed : null;
    }

    // ── Logging ──────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "GitHub API: {Action} on {Owner}/{Repo} returned {Count} items")]
    private partial void LogGitHubAction(string action, string owner, string repo, int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "GitHub API request failed for action {Action}")]
    private partial void LogGitHubRequestFailed(Exception ex, string action);
}
