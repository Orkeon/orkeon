using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Host.Files;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Api;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests.Files;

/// <summary>What a conversation reads when its run ends: the answer, what is in the outbox, and nothing sent on its own.</summary>
public sealed class EndOfRunTests : IDisposable
{
    private static readonly ConversationRef Alice = new("discord", "1001");

    private readonly HostApiHarness _harness = new();
    private readonly List<IHost> _hosts = [];
    private readonly ScriptedRunner _runner = new();
    private readonly RecordingResponder _responder = new();
    private readonly ThreadIsRunRouter _router = new("support");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_harness.Root, "conversations");

    private string Outbox => Path.Combine(Root, Alice.Key, "out");

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Dispose();
        _harness.Dispose();
    }

    private (ChatGateway Gateway, ConversationFiles Files) Build(Action<Dictionary<string, object?>>? files = null)
    {
        var host = _harness.Build(
            new HeldLlmProvider("unused"),
            port: 1,
            configure: (root, api) =>
            {
                api["Enabled"] = false;
                var section = new Dictionary<string, object?> { ["Enabled"] = true, ["Root"] = Root };
                files?.Invoke(section);
                ((Dictionary<string, object?>)((Dictionary<string, object?>)root["Orkeon"]!)["Host"]!)["Files"] = section;
            });
        _hosts.Add(host);
        var kept = host.Services.GetRequiredService<ConversationFiles>();
        return (
            new ChatGateway(
                _runner, _router, new AllowListChatAuthorizer(["alice"]), host.Services.GetRequiredService<CrewHostRegistry>(),
                NullLogger<ChatGateway>.Instance, kept),
            kept);
    }

    private Task SayAsync(ChatGateway gateway) => gateway.HandleAsync(
        new InboundMessage { Channel = Alice.Channel, ConversationId = Alice.ConversationId, SenderId = "alice", Text = "go", ReceivedAt = DateTimeOffset.UnixEpoch },
        _responder,
        Ct);

    private string[] Completed => [.. _responder.Sent.Where(s => s.Kind == "complete").Select(s => s.Text)];

    public static TheoryData<int> Outcomes =>
        [(int)HostedRunOutcome.Completed, (int)HostedRunOutcome.Failed, (int)HostedRunOutcome.Cancelled, (int)HostedRunOutcome.WaitingForInput];

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task ShouldNameTheFilesAndSendNone_HoweverTheRunEnds(int outcome)
    {
        var (gateway, _) = Build();
        _runner.Result = new HostedRunResult((HostedRunOutcome)outcome, "run-1", "the answer") { OutboxFiles = [new OutboxFile("/outbox/r.csv", 5)] };

        await SayAsync(gateway);

        var answer = Assert.Single(Completed);
        Assert.StartsWith("the answer", answer, StringComparison.Ordinal);
        Assert.Contains("`/outbox/r.csv` (5 bytes)", answer, StringComparison.Ordinal);
        Assert.Contains("/get", answer, StringComparison.Ordinal);
        Assert.Empty(_responder.Files);
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task ShouldKeepTheWholeText_AndStillNameTheFiles_WhenTheAnswerIsTooLong(int outcome)
    {
        var (gateway, _) = Build();
        var text = new string('t', 5000);
        _runner.Result = new HostedRunResult((HostedRunOutcome)outcome, "run-1", text) { OutboxFiles = [new OutboxFile("/outbox/r.csv", 5)] };

        await SayAsync(gateway);

        var answer = Assert.Single(Completed);
        Assert.InRange(answer.Length, 1500, 2000);
        Assert.Contains("truncated", answer, StringComparison.Ordinal);
        Assert.Contains("`/outbox/r.csv`", answer, StringComparison.Ordinal);
        Assert.Contains("`/outbox/reply-run-1.md`", answer, StringComparison.Ordinal);
        Assert.Equal(text, await File.ReadAllTextAsync(Path.Combine(Outbox, "reply-run-1.md"), Ct));
        Assert.Empty(_responder.Files);
    }

    [Fact]
    public async Task ShouldKeepTheReply_WhateverTheExtensionsTaken()
    {
        var (gateway, _) = Build(s => s["AllowedExtensions"] = (string[])["pdf"]);
        _runner.Result = new HostedRunResult(HostedRunOutcome.Completed, "run-1", new string('t', 5000));

        await SayAsync(gateway);

        Assert.True(File.Exists(Path.Combine(Outbox, "reply-run-1.md")));
    }

    [Fact]
    public async Task ShouldSayTheLimit_WhenTheReplyCannotBeKept()
    {
        var (gateway, _) = Build(s => s["MaxFileBytes"] = 100);
        _runner.Result = new HostedRunResult(HostedRunOutcome.Completed, "run-1", new string('t', 5000)) { OutboxFiles = [new OutboxFile("/outbox/r.csv", 5)] };

        await SayAsync(gateway);

        var answer = Assert.Single(Completed);
        Assert.InRange(answer.Length, 1500, 2000);
        Assert.Contains("larger than 100 bytes", answer, StringComparison.Ordinal);
        Assert.Contains("`/outbox/r.csv`", answer, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Outbox, "reply-run-1.md")));
    }

    [Fact]
    public async Task ShouldLeaveRoomForTheAnswer_WhenThereAreManyFiles()
    {
        var (gateway, _) = Build();
        _runner.Result = new HostedRunResult(HostedRunOutcome.Completed, "run-1", new string('t', 1900))
        {
            OutboxFiles = [.. Enumerable.Range(0, 60).Select(i => new OutboxFile($"/outbox/{new string('n', 100)}{i}.csv", 5))],
        };

        await SayAsync(gateway);

        var answer = Assert.Single(Completed);
        Assert.InRange(answer.Length, 1500, 2000);
        Assert.InRange(answer.Count(c => c == 't'), 1000, 1950);
        Assert.Contains("more", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotPromiseTheCommand_WhenTheChannelHasNone()
    {
        var (gateway, _) = Build();
        _responder.TakesFileCommands = false;
        _runner.Result = new HostedRunResult(HostedRunOutcome.Completed, "run-1", "ok") { OutboxFiles = [new OutboxFile("/outbox/r.csv", 5)] };

        await SayAsync(gateway);

        Assert.Contains("`/outbox/r.csv`", Completed[0], StringComparison.Ordinal);
        Assert.Contains("does not send", Completed[0], StringComparison.Ordinal);
        Assert.DoesNotContain("/get", Completed[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheNextMessage_UntilTheEndIsSettled()
    {
        var (gateway, files) = Build();
        _runner.Result = new HostedRunResult(HostedRunOutcome.Completed, "run-1", new string('t', 5000));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inUse = files.UseAsync(Alice, crew: null, async (_, _) =>
        {
            entered.SetResult();
            await held.Task;
            return true;
        }, Ct);
        await entered.Task.WaitAsync(Polling.DefaultTimeout, Ct);

        var first = SayAsync(gateway);
        Assert.Single(_runner.Ran);
        await SayAsync(gateway);

        Assert.Contains("already running", Assert.Single(Completed), StringComparison.Ordinal);
        Assert.Single(_runner.Ran);
        held.SetResult();
        await inUse;
        await first.WaitAsync(Polling.DefaultTimeout, Ct);
        Assert.Contains("reply-run-1.md", Completed[^1], StringComparison.Ordinal);
        await SayAsync(gateway);
        Assert.Equal(2, _runner.Ran.Count);
    }

    [Fact]
    public async Task ShouldFreeTheConversation_WhenTheReplyCannotBeWrittenAtAll()
    {
        var (gateway, _) = Build();
        Directory.CreateDirectory(Root);
        await File.WriteAllTextAsync(Path.Combine(Root, Alice.Key), "in the way", Ct);
        _runner.Result = new HostedRunResult(HostedRunOutcome.Completed, "run-1", new string('t', 5000));

        await SayAsync(gateway);
        await SayAsync(gateway);

        Assert.Equal(2, _runner.Ran.Count);
        Assert.All(Completed, t => Assert.Contains("could not be kept", t, StringComparison.Ordinal));
    }
}
