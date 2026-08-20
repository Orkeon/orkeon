using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Host.Gateway;

namespace Orkeon.Host.Tests;

/// <summary>Records what the gateway said, in the order it said it.</summary>
internal sealed class RecordingResponder : IChatResponder
{
    public List<(string Kind, string Text)> Sent { get; } = [];

    public Task AcknowledgeAsync(InboundMessage message, string text, CancellationToken ct)
    {
        Sent.Add(("ack", text));
        return Task.CompletedTask;
    }

    public Task ProgressAsync(InboundMessage message, string text, CancellationToken ct)
    {
        Sent.Add(("progress", text));
        return Task.CompletedTask;
    }

    public Task CompleteAsync(InboundMessage message, string text, CancellationToken ct)
    {
        Sent.Add(("complete", text));
        return Task.CompletedTask;
    }
}

/// <summary>A runner that answers however the test asks it to, without a crew or a model.</summary>
internal sealed class ScriptedRunner : ICrewRunner
{
    public HostedRunResult Result { get; set; } = new(HostedRunOutcome.Completed, "run-1", "done");

    public List<string> Ran { get; } = [];

    public Func<Action<string>?, Action<string>?, Task>? Behaviour { get; set; }

    public async Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Action<string>? onStarted = null,
        CancellationToken cancellationToken = default)
    {
        Ran.Add($"{crewName}:{prompt}");
        onStarted?.Invoke(Result.RunId ?? "run-1");

        if (Behaviour is not null)
            await Behaviour(onProgress, onStarted);

        return Result;
    }
}

/// <summary>
/// GATE-03: the order in which a message becomes a run — authorize, route, acknowledge, work —
/// and the two commands a conversation can send. No channel, no crew, no model.
/// </summary>
public class ChatGatewayTests
{
    private static InboundMessage Message(string text, string sender = "trusted", string conversation = "thread-1") => new()
    {
        Channel = "test",
        ConversationId = conversation,
        SenderId = sender,
        Text = text,
        ReceivedAt = DateTimeOffset.UnixEpoch,
    };

    private static (ChatGateway Gateway, ScriptedRunner Runner, CrewHostRegistry Registry, ThreadIsRunRouter Router) Build(
        params string[] allowed)
    {
        var registry = new CrewHostRegistry(Options.Create(new OrkeonHostOptions
        {
            Crews = [new HostedCrewOptions { Name = "support", Path = "/crews/support.yaml" }],
        }));

        var runner = new ScriptedRunner();
        var router = new ThreadIsRunRouter("support");
        var authorizer = new AllowListChatAuthorizer(allowed.Length == 0 ? ["trusted"] : allowed);

        return (
            new ChatGateway(runner, router, authorizer, registry, NullLogger<ChatGateway>.Instance),
            runner,
            registry,
            router);
    }

    [Fact]
    public async Task An_unknown_sender_never_reaches_a_crew()
    {
        // Refused before routing: no crew, no token, no accepted-request log line.
        var (gateway, runner, _, _) = Build("someone-else");
        var responder = new RecordingResponder();

        await gateway.HandleAsync(Message("do the thing"), responder, TestContext.Current.CancellationToken);

        Assert.Empty(runner.Ran);
        var reply = Assert.Single(responder.Sent);
        Assert.Equal("complete", reply.Kind);
        Assert.Contains("not authorized", reply.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_allow_list_denies_everyone()
    {
        // The opposite default is how a bot invited to a public server spends someone's API
        // budget on strangers.
        var authorizer = new AllowListChatAuthorizer([]);

        Assert.True(authorizer.IsEmpty);
        Assert.False(authorizer.IsAuthorized(Message("hello")));
    }

    [Fact]
    public async Task The_acknowledgement_goes_out_before_the_work_starts()
    {
        // Every chat platform's response window is measured in seconds; a crew is measured in
        // minutes. Acknowledging afterwards would be acknowledging into a closed window.
        var (gateway, runner, _, _) = Build();
        var responder = new RecordingResponder();
        var acknowledgedBeforeRun = false;
        runner.Behaviour = (_, _) =>
        {
            acknowledgedBeforeRun = responder.Sent.Any(s => s.Kind == "ack");
            return Task.CompletedTask;
        };

        await gateway.HandleAsync(Message("do the thing"), responder, TestContext.Current.CancellationToken);

        Assert.True(acknowledgedBeforeRun);
        Assert.Equal("ack", responder.Sent[0].Kind);
        Assert.Equal("complete", responder.Sent[^1].Kind);
        Assert.Equal("support:do the thing", Assert.Single(runner.Ran));
    }

    [Fact]
    public async Task A_conversation_runs_one_thing_at_a_time()
    {
        // Two runs in one thread would give the user two answers with no way to tell which
        // question each belongs to.
        var (gateway, runner, _, router) = Build();
        router.Attach("thread-1", "run-already-here");

        var responder = new RecordingResponder();
        await gateway.HandleAsync(Message("do the thing"), responder, TestContext.Current.CancellationToken);

        Assert.Empty(runner.Ran);
        Assert.Contains("already running", Assert.Single(responder.Sent).Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stop_reaches_the_run_the_conversation_started()
    {
        // The bug this pins: attaching the run id only when the run *finished* would have made
        // /stop permanently unable to find anything to stop.
        var (gateway, runner, registry, router) = Build();
        var started = registry.TryStart("support", "test:thread-1", TestContext.Current.CancellationToken)!;
        runner.Result = new HostedRunResult(HostedRunOutcome.Completed, started.Id, "done");

        var stopping = new RecordingResponder();
        runner.Behaviour = async (_, _) =>
        {
            await gateway.HandleAsync(Message("/stop"), stopping, TestContext.Current.CancellationToken);
        };

        await gateway.HandleAsync(Message("do the thing"), new RecordingResponder(), TestContext.Current.CancellationToken);

        Assert.Contains("Stopping", Assert.Single(stopping.Sent).Text, StringComparison.Ordinal);
        Assert.True(started.Cancellation.IsCancellationRequested);

        // And the conversation is released once the run is over.
        Assert.Null(router.FindRun("thread-1"));
    }

    [Fact]
    public async Task Stop_on_an_idle_conversation_says_so_rather_than_failing()
    {
        var (gateway, _, _, _) = Build();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(Message("/stop"), responder, TestContext.Current.CancellationToken);

        Assert.Contains("Nothing is running", Assert.Single(responder.Sent).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_reports_what_the_conversation_is_doing()
    {
        var (gateway, runner, registry, _) = Build();
        var started = registry.TryStart("support", "test:thread-1", TestContext.Current.CancellationToken)!;
        runner.Result = new HostedRunResult(HostedRunOutcome.Completed, started.Id, "done");

        var status = new RecordingResponder();
        runner.Behaviour = async (_, _) =>
        {
            await gateway.HandleAsync(Message("/status"), status, TestContext.Current.CancellationToken);
        };

        await gateway.HandleAsync(Message("do the thing"), new RecordingResponder(), TestContext.Current.CancellationToken);

        Assert.Contains("support", Assert.Single(status.Sent).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_run_still_answers()
    {
        // Silence in a chat window looks like a broken bot, whatever actually happened.
        var (gateway, runner, _, _) = Build();
        runner.Result = new HostedRunResult(HostedRunOutcome.Failed, "run-1", "the model refused");

        var responder = new RecordingResponder();
        await gateway.HandleAsync(Message("do the thing"), responder, TestContext.Current.CancellationToken);

        Assert.Equal("complete", responder.Sent[^1].Kind);
        Assert.Equal("the model refused", responder.Sent[^1].Text);
    }
}
