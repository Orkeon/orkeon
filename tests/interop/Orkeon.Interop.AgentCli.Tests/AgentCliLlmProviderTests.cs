using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.Exceptions;
using Orkeon.Domain.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Interop.AgentCli.Engine;
using Orkeon.Interop.AgentCli.Sessions;
using Orkeon.Interop.AgentCli.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Interop.AgentCli.Tests;

public sealed class AgentCliLlmProviderTests : IAsyncDisposable
{
    private static readonly ToolSchema Read = new("read", "Reads a file", new Dictionary<string, ParameterSchema>
    {
        ["path"] = new("string", "The file", Required: true),
    });

    private readonly FakeAgentCliLauncher _launcher = new();
    private readonly RecordingLogger _logger = new();
    private readonly List<AgentCliProcessHost> _hosts = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
    }

    private AgentCliLlmProvider Provider(AgentCliOptions? options = null, LlmConfig? config = null)
    {
        options ??= new AgentCliOptions();
        var host = new AgentCliProcessHost(_launcher, new ClaudeCodeEngine(options), options, _logger);
        _hosts.Add(host);
        return new AgentCliLlmProvider(host, config ?? LlmConfig.Create("haiku"), _logger);
    }

    private static LlmMessage User(string text) => new() { Role = "user", Content = text };

    private static readonly LlmMessage System = new() { Role = "system", Content = "You are terse." };

    private static LlmConfig WithTools => LlmConfig.OnProfile() with { Tools = [Read] };

    /// <summary>The first turn of a conversation, ending on a call of <c>read</c>.</summary>
    private async Task<(FakeAgentCliProcess Process, LlmResponse Response)> FirstToolTurnAsync(AgentCliLlmProvider provider)
    {
        var call = provider.ChatAsync([System, User("Read a.txt")], WithTools, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Init());
        await process.EmitAsync(StreamLines.Text("m1", "Reading."));
        await process.EmitAsync(StreamLines.ToolUse("m1", "toolu_1", "read", new JsonObject { ["path"] = "a.txt" }));
        await process.EmitAsync(StreamLines.Delta("tool_use"));
        await process.EmitAsync(StreamLines.Stop());
        return (process, await call);
    }

    private static LlmMessage AssistantTurn(LlmResponse response)
    {
        using var body = JsonDocument.Parse(response.RawResponseBody!);
        var calls = body.RootElement.GetProperty("content").EnumerateArray()
            .Where(block => block.GetProperty("type").GetString() == "tool_use")
            .Select(block => JsonNode.Parse(block.GetRawText()))
            .ToArray();
        return new LlmMessage { Role = "assistant", Content = response.Content, RawToolCalls = new JsonArray(calls).ToJsonString() };
    }

    [Fact]
    public async Task ShouldReturnAnthropicToolUse_WhenMessageStopsOnToolUse()
    {
        await using var scope = AgentRunScope.Begin("a1", "Reader");
        var (process, response) = await FirstToolTurnAsync(Provider());

        Assert.Equal("You are terse.", process.SystemPrompt);
        Assert.Equal("haiku", process.Model);
        Assert.Equal("Reading.", response.Content);
        using var body = JsonDocument.Parse(response.RawResponseBody!);
        var toolUse = body.RootElement.GetProperty("content")[1];
        Assert.Equal("tool_use", toolUse.GetProperty("type").GetString());
        Assert.Equal("read", toolUse.GetProperty("name").GetString());
        Assert.Equal("a.txt", toolUse.GetProperty("input").GetProperty("path").GetString());
        Assert.Equal("tool_use", body.RootElement.GetProperty("stop_reason").GetString());
        Assert.Equal("tool_use", response.Metadata[LlmResponseMetadataKeys.FinishReason]);
    }

    [Fact]
    public async Task ShouldMapUsageAndZeroCost_WhenTurnEnds()
    {
        await using var scope = AgentRunScope.Begin("a1", "Reader");
        var (_, response) = await FirstToolTurnAsync(Provider());

        Assert.Equal(115, response.PromptTokens);
        Assert.Equal(20, response.CompletionTokens);
        Assert.Equal(135, response.TokensUsed);
        Assert.Equal(100, response.CacheHitTokens);
        Assert.Equal(15, response.CacheMissTokens);
        Assert.Equal(5, response.CacheWriteTokens);
        Assert.Equal(0m, response.Metadata[LlmUsageMetadataKeys.Cost]);
        Assert.Equal("agent-cli", response.Metadata["provider"]);
    }

    [Fact]
    public async Task ShouldContinueSession_WhenToolResultExtendsHistory()
    {
        await using var scope = AgentRunScope.Begin("a1", "Reader");
        var provider = Provider();
        var (process, first) = await FirstToolTurnAsync(provider);
        await process.Connection.CallAsync(1, "read", "toolu_1");

        var second = provider.ChatAsync(
            [System, User("Read a.txt"), AssistantTurn(first), new LlmMessage { Role = "tool", ToolCallId = "toolu_1", Content = "hello" }],
            WithTools, TestContext.Current.CancellationToken);
        var answer = await process.Connection.NextAsync();
        await process.EmitAsync(StreamLines.Text("m2", "It says hello."));
        await process.EmitAsync(StreamLines.Delta("end_turn"));
        await process.EmitAsync(StreamLines.Stop());
        await process.EmitAsync(StreamLines.Result("It says hello."));
        var response = await second;

        Assert.Equal("hello", answer["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("It says hello.", response.Content);
        Assert.Single(_launcher.Started);
        Assert.False(process.Stdin.Reader.TryRead(out _));
        Assert.Equal(0.0123m, response.Metadata[AgentCliLlmProvider.NotionalCostKey]);
    }

    [Fact]
    public async Task ShouldWriteUserLine_WhenNewUserMessageFollowsAnswer()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var provider = Provider();
        var first = provider.ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Text("m1", "Hello."));
        await process.EmitAsync(StreamLines.Result("Hello."));
        var hello = await first;

        var second = provider.ChatAsync(
            [User("Hi"), new LlmMessage { Role = "assistant", Content = hello.Content }, User("Bye")],
            null, TestContext.Current.CancellationToken);
        var line = await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Text("m2", "Goodbye."));
        await process.EmitAsync(StreamLines.Result("Goodbye."));

        Assert.Equal("Goodbye.", (await second).Content);
        Assert.Equal("Bye", line["message"]!["content"]!.GetValue<string>());
        Assert.Single(_launcher.Started);
    }

    [Fact]
    public async Task ShouldSeedNewSession_WhenHistoryDoesNotExtendSession()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var provider = Provider();
        var first = provider.ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Text("m1", "Hello."));
        await process.EmitAsync(StreamLines.Result("Hello."));
        await first;

        // The crew rewrote the assistant's answer: the CLI holds another conversation.
        var second = provider.ChatAsync(
            [User("Hi"), new LlmMessage { Role = "assistant", Content = "Rewritten." }, User("Bye")],
            null, TestContext.Current.CancellationToken);
        var seeded = await _launcher.NextAsync();
        var seed = (await seeded.NextInputAsync())["message"]!["content"]!.GetValue<string>();
        await seeded.EmitAsync(StreamLines.Result("Goodbye."));
        await second;

        Assert.Contains("<conversation_history>", seed, StringComparison.Ordinal);
        Assert.Contains("Rewritten.", seed, StringComparison.Ordinal);
        Assert.EndsWith("Bye", seed, StringComparison.Ordinal);
        Assert.Equal(2, _launcher.Started.Count);
    }

    [Fact]
    public async Task ShouldReturnApiError_WhenResultIsError()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var call = Provider().ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Result("overloaded", isError: true, status: 529));
        var response = await call;

        Assert.Equal("APIError", response.Metadata[LlmResponseMetadataKeys.ErrorType]);
        Assert.Contains("HTTP 529", (string)response.Metadata[LlmResponseMetadataKeys.Error], StringComparison.Ordinal);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task ShouldReturnError_WhenCliEnds()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var call = Provider().ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        process.StandardErrorTail = "not logged in";
        process.End(1);
        var response = await call;

        Assert.Contains("exit code 1", (string)response.Metadata[LlmResponseMetadataKeys.Error], StringComparison.Ordinal);
        Assert.Contains("not logged in", (string)response.Metadata[LlmResponseMetadataKeys.Error], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldThrowRateLimit_WhenSubscriptionLimitRejects()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var call = Provider().ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.RateLimit("rejected", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()));

        var error = await Assert.ThrowsAsync<LlmRateLimitException>(() => call);

        Assert.Contains("five_hour", error.Message, StringComparison.Ordinal);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task ShouldRefuse_WhenCliRunsOnAnApiKey()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var call = Provider().ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Init(apiKeySource: "ANTHROPIC_API_KEY"));
        var response = await call;

        Assert.Contains("rather than its login", (string)response.Metadata[LlmResponseMetadataKeys.Error], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldInterruptAndClose_WhenCallCancelled()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var call = Provider().ChatAsync([User("Hi")], null, cancel.Token);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        var interrupt = await process.NextInputAsync();
        Assert.Equal("interrupt", interrupt["request"]!["subtype"]!.GetValue<string>());
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task ShouldSeedNewSession_WhenCliCompactedItself()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var provider = Provider();
        var first = provider.ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.CompactBoundary());
        await process.EmitAsync(StreamLines.Result("Hello."));
        var hello = await first;

        var second = provider.ChatAsync(
            [User("Hi"), new LlmMessage { Role = "assistant", Content = hello.Content }, User("Bye")],
            null, TestContext.Current.CancellationToken);
        var seeded = await _launcher.NextAsync();
        await seeded.NextInputAsync();
        await seeded.EmitAsync(StreamLines.Result("Goodbye."));
        await second;

        Assert.True(process.Disposed);
        Assert.True(_logger.HasEntry(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("compacted", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ShouldEndProcesses_WhenRunEnds()
    {
        var scope = AgentRunScope.Begin("a1", "Chat");
        FakeAgentCliProcess process;
        try
        {
            var call = Provider().ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
            process = await _launcher.NextAsync();
            await process.NextInputAsync();
            await process.EmitAsync(StreamLines.Result("Hello."));
            await call;
            Assert.False(process.Disposed);
        }
        finally
        {
            await scope.EndAsync();
        }

        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task ShouldCloseSession_WhenCalledOutsideAnyRun()
    {
        var call = Provider().ChatAsync([User("Hi")], null, TestContext.Current.CancellationToken);
        var process = await _launcher.NextAsync();
        await process.NextInputAsync();
        await process.EmitAsync(StreamLines.Result("Hello."));

        Assert.Equal("Hello.", (await call).Content);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task ShouldEvictIdleSession_WhenMaxProcessesReached()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var provider = Provider(new AgentCliOptions { MaxProcesses = 1 });

        var first = provider.ChatAsync([User("One")], null, TestContext.Current.CancellationToken);
        var one = await _launcher.NextAsync();
        await one.NextInputAsync();
        await one.EmitAsync(StreamLines.Result("1"));
        await first;

        var second = provider.ChatAsync([User("Two")], null, TestContext.Current.CancellationToken);
        var two = await _launcher.NextAsync();
        await two.NextInputAsync();
        await two.EmitAsync(StreamLines.Result("2"));
        await second;

        Assert.True(one.Disposed);
        Assert.False(two.Disposed);
    }

    [Fact]
    public async Task ShouldWarnOncePerRun_WhenOptionCannotBeSent()
    {
        await using var scope = AgentRunScope.Begin("a1", "Chat");
        var provider = Provider();
        var config = LlmConfig.OnProfile() with { Temperature = 0.2 };

        for (var turn = 0; turn < 2; turn++)
        {
            var call = provider.ChatAsync([User("Hi " + turn)], config, TestContext.Current.CancellationToken);
            var process = await _launcher.NextAsync();
            await process.NextInputAsync();
            await process.EmitAsync(StreamLines.Result("ok"));
            await call;
        }

        Assert.Single(_logger.Entries, entry => entry.EventId.Id == 110 && entry.Message.Contains("'temperature'", StringComparison.Ordinal));
    }
}
