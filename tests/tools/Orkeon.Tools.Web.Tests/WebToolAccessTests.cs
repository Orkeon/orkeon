using System.Text.Json;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// #219 - the access class of one call of <c>github</c> and <c>http_api</c>, decided from its arguments
/// (<see cref="IBaseTool.AccessFor"/>), and the static class of <c>web_search</c>.
/// </summary>
public sealed class WebToolAccessTests
{
    [Theory]
    [InlineData("list_issues")]
    [InlineData("get_pr")]
    [InlineData("search_repos")]
    [InlineData("GET_REPO")]
    public void GitHubShouldClassifyAReadActionAsRead(string action)
    {
        using var github = new GitHubTool();
        IBaseTool tool = github;

        Assert.Equal(ToolAccess.Read, tool.AccessFor(Args("action", action)));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(JsonArgs("action", action)));
    }

    [Theory]
    [InlineData("create_issue")]
    [InlineData("create_pr")]
    [InlineData("delete_repo")]
    [InlineData("")]
    public void GitHubShouldClassifyAWriteOrUnknownActionAsExecute(string action)
    {
        using var github = new GitHubTool();
        IBaseTool tool = github;

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Args("action", action)));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(JsonArgs("action", action)));
    }

    [Fact]
    public void GitHubShouldClassifyAMissingActionAsExecuteAndDeclareExecute()
    {
        using var github = new GitHubTool();
        IBaseTool tool = github;

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(new Dictionary<string, object?>()));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Args("action", null)));
        Assert.Equal(ToolAccess.Execute, tool.Access);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("get")]
    [InlineData("Head")]
    [InlineData("OPTIONS")]
    public void HttpApiShouldClassifyAReadMethodAsRead(string method)
    {
        using var http = new HttpApiTool();
        IBaseTool tool = http;

        Assert.Equal(ToolAccess.Read, tool.AccessFor(Args("method", method)));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(JsonArgs("method", method)));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("put")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("")]
    public void HttpApiShouldClassifyAnyOtherMethodAsExecute(string method)
    {
        using var http = new HttpApiTool();
        IBaseTool tool = http;

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(Args("method", method)));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(JsonArgs("method", method)));
    }

    [Fact]
    public void HttpApiShouldClassifyAMissingMethodAsItsDefaultGet()
    {
        using var http = new HttpApiTool();
        IBaseTool tool = http;

        Assert.Equal(ToolAccess.Read, tool.AccessFor(Args("url", "https://api.example.com")));
        Assert.Equal(ToolAccess.Read, tool.AccessFor(Args("method", null)));
        Assert.Equal(ToolAccess.Execute, tool.Access);
    }

    // #219 review: a read method that carries a method-override header or a body can change state on
    // the server (a GET tunnelling a DELETE), so the call is not a read.
    [Theory]
    [InlineData("X-HTTP-Method-Override")]
    [InlineData("x-http-method")]
    [InlineData("X-Method-Override")]
    [InlineData("X-HTTP-METHOD-OVERRIDE")]
    public void HttpApiShouldClassifyAReadMethodWithAMethodOverrideHeaderAsExecute(string header)
    {
        using var http = new HttpApiTool();
        IBaseTool tool = http;
        var dictionaryHeaders = new Dictionary<string, object?>
        {
            ["method"] = "GET",
            ["headers"] = new Dictionary<string, object> { [header] = "DELETE" },
        };
        var jsonHeaders = new Dictionary<string, object?>
        {
            ["method"] = JsonSerializer.SerializeToElement("GET"),
            ["headers"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { [header] = "DELETE" }),
        };
        var stringHeaders = new Dictionary<string, object?>
        {
            ["headers"] = JsonSerializer.Serialize(new Dictionary<string, string> { [header] = "DELETE" }),
        };

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(dictionaryHeaders));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(jsonHeaders));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(stringHeaders));
    }

    [Fact]
    public void HttpApiShouldKeepAReadMethodWithOrdinaryHeadersARead()
    {
        using var http = new HttpApiTool();
        IBaseTool tool = http;
        var args = new Dictionary<string, object?>
        {
            ["method"] = "GET",
            ["headers"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Accept"] = "application/json" }),
            ["body"] = null,
        };

        Assert.Equal(ToolAccess.Read, tool.AccessFor(args));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void HttpApiShouldClassifyAReadMethodWithABodyAsExecute(string method)
    {
        using var http = new HttpApiTool();
        IBaseTool tool = http;

        Assert.Equal(ToolAccess.Execute, tool.AccessFor(new Dictionary<string, object?> { ["method"] = method, ["body"] = "{\"drop\":true}" }));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(new Dictionary<string, object?> { ["method"] = method, ["body"] = JsonSerializer.SerializeToElement("x") }));
        Assert.Equal(ToolAccess.Execute, tool.AccessFor(new Dictionary<string, object?> { ["body"] = "x" }));
    }

    [Fact]
    public void WebSearchShouldDeclareRead()
    {
        using var search = new WebSearchTool(new MockSecretProvider());
        IBaseTool tool = search;

        Assert.Equal(ToolAccess.Read, tool.Access);
    }

    private static Dictionary<string, object?> Args(string name, string? value) => new() { [name] = value };

    private static Dictionary<string, object?> JsonArgs(string name, string value) =>
        new() { [name] = JsonSerializer.SerializeToElement(value) };
}
