using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Execution;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Interop.AgentCli.Tests.Integration;

/// <summary>
/// The provider on the real <c>claude</c>, signed in, on haiku alone (the owner's rule for these runs). Skipped
/// when no <c>claude</c> is on the PATH. Each test reports the tokens it used.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealClaudeTests : IAsyncDisposable
{
    private static readonly ToolSchema Add = new("add", "Adds two integers and returns their sum.", new Dictionary<string, ParameterSchema>
    {
        ["a"] = new("integer", "The first integer", Required: true),
        ["b"] = new("integer", "The second integer", Required: true),
    });

    private readonly AgentCliProviderModule _module = new(new AgentCliOptions());

    public ValueTask DisposeAsync() => _module.DisposeAsync();

    private ILlmProvider Provider() => _module.Create(LlmConfig.Create("haiku"),
        new LlmProviderModuleContext { LoggerFactory = NullLoggerFactory.Instance, FileSystem = new FakeFileSystemService() });

    private static void SkipWithoutClaude() =>
        Assert.SkipUnless(
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Any(folder => File.Exists(Path.Join(folder, OperatingSystem.IsWindows() ? "claude.exe" : "claude"))),
            "no claude CLI on the PATH");

    private static CancellationTokenSource Guard()
    {
        var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(HangGuards.ProcessStart);
        return guard;
    }

    /// <summary>Writes the tokens a test used to its output, and to the file <c>ORKEON_AGENTCLI_USAGE_LOG</c> names when set.</summary>
    private static void Report(string test, params LlmResponse[] responses)
    {
        var line = Usage(test, responses);
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("ORKEON_AGENTCLI_USAGE_LOG") is { Length: > 0 } log)
            File.AppendAllLines(log, [line]);
    }

    private static string Usage(string test, LlmResponse[] responses) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{test}: prompt {responses.Sum(r => r.PromptTokens ?? 0)}, completion {responses.Sum(r => r.CompletionTokens ?? 0)}, cache read {responses.Sum(r => r.CacheHitTokens ?? 0)}, notional USD {responses.Sum(r => r.Metadata.TryGetValue(AgentCliLlmProvider.NotionalCostKey, out var c) ? (decimal)c : 0m)}");

    private static string ErrorOf(LlmResponse response) =>
        response.Metadata.TryGetValue("error", out var error) ? error.ToString() ?? "" : "";

    [Fact]
    public async Task ShouldAnswerOnLogin_WhenApiKeyIsExported()
    {
        SkipWithoutClaude();
        using var guard = Guard();
        await using var scope = AgentRunScope.Begin("it", "Probe");
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "placeholder-not-a-key");
        LlmResponse response;
        try
        {
            response = await Provider().ChatAsync(
                [new LlmMessage { Role = "user", Content = "Reply with the single word pong, nothing else." }], null, guard.Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }

        Report(nameof(ShouldAnswerOnLogin_WhenApiKeyIsExported), response);
        Assert.Equal("", ErrorOf(response));
        Assert.Contains("pong", response.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.PromptTokens > 0);
        Assert.Equal(0m, response.Metadata[LlmUsageMetadataKeys.Cost]);
    }

    [Fact]
    public async Task ShouldRunOrkeonTool_WhenCliCallsItThroughBridge()
    {
        SkipWithoutClaude();
        using var guard = Guard();
        await using var scope = AgentRunScope.Begin("it", "Adder");
        var provider = Provider();
        var config = LlmConfig.OnProfile() with { Tools = [Add] };
        List<LlmMessage> history =
        [
            new() { Role = "system", Content = "You add numbers with the add tool only, never in your head. You trust the tool's result." },
            new() { Role = "user", Content = "Use the add tool to add 2 and 3, then reply with the sum the tool returned, alone." },
        ];

        var first = await provider.ChatAsync([.. history], config, guard.Token);
        Assert.Equal("", ErrorOf(first));
        using var body = JsonDocument.Parse(first.RawResponseBody!);
        var toolUse = body.RootElement.GetProperty("content").EnumerateArray().Single(block => block.GetProperty("type").GetString() == "tool_use");
        Assert.Equal("add", toolUse.GetProperty("name").GetString());
        var id = toolUse.GetProperty("id").GetString()!;

        history.Add(new LlmMessage { Role = "assistant", Content = first.Content, RawToolCalls = new JsonArray(JsonNode.Parse(toolUse.GetRawText())).ToJsonString() });
        // A result the model could not compute: proof it came from Orkeon, not from the model.
        history.Add(new LlmMessage { Role = "tool", ToolCallId = id, Content = "{\"sum\": 4242}" });
        var second = await provider.ChatAsync([.. history], config, guard.Token);

        Report(nameof(ShouldRunOrkeonTool_WhenCliCallsItThroughBridge), first, second);
        Assert.Equal("", ErrorOf(second));
        Assert.Contains("4242", second.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAnswerAgain_WhenPreviousTurnWasInterrupted()
    {
        SkipWithoutClaude();
        using var guard = Guard();
        await using var scope = AgentRunScope.Begin("it", "Chat");
        var provider = Provider();

        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(guard.Token))
        {
            var interrupted = provider.ChatAsync(
                [new LlmMessage { Role = "user", Content = "Count from 1 to 200, one number per line." }], null, cancel.Token);
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted);
        }

        var response = await provider.ChatAsync(
            [new LlmMessage { Role = "user", Content = "Reply with the single word ready, nothing else." }], null, guard.Token);

        Report(nameof(ShouldAnswerAgain_WhenPreviousTurnWasInterrupted), response);
        Assert.Equal("", ErrorOf(response));
        Assert.Contains("ready", response.Content, StringComparison.OrdinalIgnoreCase);
    }
}
