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
