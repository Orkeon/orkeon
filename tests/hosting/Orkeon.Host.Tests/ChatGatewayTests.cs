using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

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
    public async Task The_acknowledgement_rides_on_admission_and_goes_out_before_the_work()
    {
        // Every chat platform's response window is measured in seconds; a crew is measured in
        // minutes. But acknowledging *before admission* promised work — with a Stop button
        // attached to nothing — that the very next line could refuse as Busy. So the ack goes
        // out the moment the slot is reserved: still before any crew work, never before a
        // refusal.
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
    public async Task A_refused_run_gets_an_answer_but_no_acknowledgement()
    {
        // "Working on it…" plus a Stop button, followed by "we are busy", is a promise
        // followed by its own retraction — and a button that does nothing when pressed.
        var (gateway, runner, _, _) = Build();
        runner.Admits = false;
        runner.Result = new HostedRunResult(HostedRunOutcome.Busy, null, "busy, try again shortly");

        var responder = new RecordingResponder();
        await gateway.HandleAsync(Message("do the thing"), responder, TestContext.Current.CancellationToken);

        var reply = Assert.Single(responder.Sent);
        Assert.Equal("complete", reply.Kind);
        Assert.Contains("busy", reply.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_word_starting_with_a_command_is_a_prompt_not_a_command()
    {
        // "/stopwatch the build" is work to do, not a stop. First-token match only.
        var (gateway, runner, _, _) = Build();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(Message("/stopwatch the build"), responder, TestContext.Current.CancellationToken);

        Assert.Equal("support:/stopwatch the build", Assert.Single(runner.Ran));
    }

    [Fact]
    public async Task Two_messages_racing_into_one_conversation_start_one_run()
    {
        // The old guard was FindRun-then-await: two messages interleaving across the ack both
        // passed it, and one thread got two runs whose answers nobody could tell apart. The
        // claim is now atomic (TryBegin), and this race cannot start a second run.
        var (gateway, runner, _, _) = Build();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.Behaviour = (_, _) => release.Task;

        var first = gateway.HandleAsync(Message("first"), new RecordingResponder(), TestContext.Current.CancellationToken);
        var second = gateway.HandleAsync(Message("second"), new RecordingResponder(), TestContext.Current.CancellationToken);

        // Whichever claimed the conversation runs; the other is refused without running.
        await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        release.TrySetResult();
        await first;
        await second;

        Assert.Single(runner.Ran);
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
        var started = registry.TryStart("support", "test:thread-1")!;
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
        var started = registry.TryStart("support", "test:thread-1")!;
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
