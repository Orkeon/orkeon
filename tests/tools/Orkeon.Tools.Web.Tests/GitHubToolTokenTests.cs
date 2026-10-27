using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.DependencyInjection;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// #222 - the github tool's token: read at registration from the setting, then the environment variable
/// the setting names (default GITHUB_TOKEN), then GH_TOKEN; never echoed to the model or a log; and the
/// description that tells the operator what to set when there is none.
/// </summary>
public sealed class GitHubToolTokenTests
{
    // A marker string standing for the credential: what the assertions look for in errors and logs.
    private static readonly string Marker = string.Concat("marker-", "value-", "for-tests-7f3a");

    private const string SettingKey = nameof(GitHubToolOptions.Token);
    private const string VariableKey = nameof(GitHubToolOptions.TokenVariable);
    private const string Primary = GitHubToolOptions.DefaultTokenVariable;
    private const string Fallback = GitHubToolOptions.FallbackTokenVariable;

    [Fact]
    public void ShouldPreferTheSettingOverEveryEnvironmentVariable()
    {
        var found = GitHubToolOptions.ResolveToken(
            Config((SettingKey, "from-settings")),
            Env((Primary, "from-primary"), (Fallback, "from-fallback")));

        Assert.Equal("from-settings", found);
    }

    [Fact]
    public void ShouldReadThePrimaryVariableBeforeTheFallback_WhenNoSettingIsGiven()
    {
        var found = GitHubToolOptions.ResolveToken(
            Config((SettingKey, "  ")),
            Env((Primary, "from-primary"), (Fallback, "from-fallback")));

        Assert.Equal("from-primary", found);
    }

    [Fact]
    public void ShouldReadTheFallback_WhenThePrimaryVariableIsEmpty()
    {
        var found = GitHubToolOptions.ResolveToken(null, Env((Primary, ""), (Fallback, "from-fallback")));

        Assert.Equal("from-fallback", found);
    }

    [Fact]
    public void ShouldReadTheVariableTheSettingNames_InPlaceOfThePrimary()
    {
        var named = Config((VariableKey, "MY_GH"));

        Assert.Equal("from-mine", GitHubToolOptions.ResolveToken(named, Env(("MY_GH", "from-mine"), (Primary, "from-primary"))));
        Assert.Equal("from-fallback", GitHubToolOptions.ResolveToken(named, Env((Primary, "from-primary"), (Fallback, "from-fallback"))));
    }

    [Fact]
    public void ShouldFindNothing_WhenNoSourceHoldsAValue()
    {
        Assert.Null(GitHubToolOptions.ResolveToken(Config(), Env()));
        Assert.Null(GitHubToolOptions.ResolveToken(null, Env()));
    }

    [Fact]
    public void ShouldTellWhereTheCredentialGoes_WhenTheToolHasNone()
    {
        using var tool = new GitHubTool(personalAccessToken: null);

        Assert.Contains("Orkeon:Tools:GitHub:Token", tool.Description, StringComparison.Ordinal);
        Assert.Contains(Primary, tool.Description, StringComparison.Ordinal);
        Assert.Contains("write operations", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNotMentionAMissingCredentialNorQuoteIt_WhenTheToolHasOne()
    {
        using var tool = new GitHubTool(personalAccessToken: Marker);

        Assert.DoesNotContain("Orkeon:Tools:GitHub:Token", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, tool.Schema.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRegisterTheToolWithTheConfiguredCredential()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddSingleton<Orkeon.Application.Interfaces.Security.ISecretProvider>(new MockSecretProvider());
        services.AddSingleton(Config((SettingKey, Marker)));
        services.AddOrkeonWebTools();
        using var sp = services.BuildServiceProvider();

        var github = sp.GetServices<IBaseTool>().OfType<GitHubTool>().Single();

        Assert.DoesNotContain("Orkeon:Tools:GitHub:Token", github.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotEchoTheCredential_WhenTheErrorBodyQuotesIt()
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.Unauthorized, $"{{\"message\":\"Bad credentials: Bearer {Marker}\"}}");
        using var client = new HttpClient(handler, disposeHandler: false);
        var logger = new RecordingLogger();
        using var tool = new GitHubTool(Marker, client, logger);

        var result = await tool.CallAsync(CreateIssue(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        AssertMarkerAbsent(result, logger);
        Assert.Equal($"Bearer {Marker}", handler.LastRequest?.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task ShouldNotEchoTheCredential_WhenTheRequestFailsWithAMessageQuotingIt()
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponseFactory(_ => throw new HttpRequestException($"proxy refused Authorization: Bearer {Marker}"));
        using var client = new HttpClient(handler, disposeHandler: false);
        var logger = new RecordingLogger();
        using var tool = new GitHubTool(Marker, client, logger);

        var result = await tool.CallAsync(CreateIssue(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        AssertMarkerAbsent(result, logger);
        Assert.NotEmpty(logger.Entries);
    }

    [Fact]
    [Trait("BR", "CH-222")]
    public async Task ShouldSendTheCredentialOnTheRequestAlone_WhenTheClientIsShared()
    {
        // The client may be shared with other tools (HttpToolBase's static fallback): a header set on
        // it would reach every host those tools call.
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, "[]");
        using var client = new HttpClient(handler, disposeHandler: false);
        using var tool = new GitHubTool(Marker, client);

        var result = await tool.CallAsync(new ToolCallRequest(
            ToolName: "github",
            Parameters: new Dictionary<string, object?> { ["action"] = "list_issues", ["owner"] = "octocat", ["repo"] = "hello" }),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal($"Bearer {Marker}", handler.LastRequest?.Headers.Authorization?.ToString());
        Assert.Contains(handler.LastRequest!.Headers.Accept, a => a.MediaType == "application/vnd.github.v3+json");
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Empty(client.DefaultRequestHeaders.Accept);
    }

    private static void AssertMarkerAbsent(ToolCallResponse result, RecordingLogger logger)
    {
        Assert.DoesNotContain(Marker, result.Error ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, System.Text.Json.JsonSerializer.Serialize(result.Result), StringComparison.Ordinal);
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(Marker, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        });
    }

    private static ToolCallRequest CreateIssue() => new(
        ToolName: "github",
        Parameters: new Dictionary<string, object?>
        {
            ["action"] = "create_issue",
            ["owner"] = "octocat",
            ["repo"] = "hello",
            ["title"] = "A title",
        });

    private static IConfiguration Config(params (string Key, string Value)[] keys) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(keys.Select(k =>
                new KeyValuePair<string, string?>($"{GitHubToolOptions.SectionName}:{k.Key}", k.Value)))
            .Build();

    private static Func<string, string?> Env(params (string Name, string Value)[] variables)
    {
        var table = variables.ToDictionary(v => v.Name, v => (string?)v.Value, StringComparer.Ordinal);
        return name => table.GetValueOrDefault(name);
    }
}
