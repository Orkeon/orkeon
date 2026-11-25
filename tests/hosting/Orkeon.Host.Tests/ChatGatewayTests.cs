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

    private static (ChatGateway Gateway, ScriptedRunner Runner) BuildRouted()
    {
        var registry = new CrewHostRegistry(Options.Create(new OrkeonHostOptions
        {
            Crews =
            [
                new HostedCrewOptions { Name = "support", Path = "/crews/support.yaml" },
                new HostedCrewOptions { Name = "legal", Path = "/crews/legal.yaml" },
            ],
        }));
        var runner = new ScriptedRunner();
        var routes = new ChatRoutes("support", new Dictionary<string, string> { ["5"] = "legal" });
        return (
            new ChatGateway(
                runner, new ThreadIsRunRouter(routes), new AllowListChatAuthorizer(["trusted"]), registry, NullLogger<ChatGateway>.Instance),
            runner);
    }

    [Fact]
    public async Task ShouldRunTheCrewOfTheRoom_WhenTheRoomHadToBeAskedFor()
    {
        var (gateway, runner) = BuildRouted();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(
            Message("go") with { ResolveRoomAsync = _ => Task.FromResult<string?>("5") }, responder, TestContext.Current.CancellationToken);

        Assert.Equal("legal:go", Assert.Single(runner.Ran));
    }

    [Fact]
    public async Task ShouldRunNoCrew_WhenTheRoomCannotBeFound()
    {
        var (gateway, runner) = BuildRouted();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(
            Message("go") with { ResolveRoomAsync = _ => Task.FromResult<string?>(null) }, responder, TestContext.Current.CancellationToken);

        Assert.Empty(runner.Ran);
        var reply = Assert.Single(responder.Sent);
        Assert.Equal("complete", reply.Kind);
        Assert.Contains("room of this conversation could not be found", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotAskForTheRoom_OfASenderWhoIsNotAllowed_NorOfAKnownRoom()
    {
        var (gateway, runner) = BuildRouted();
        var asked = 0;
        Task<string?> Resolve(CancellationToken ct)
        {
            asked++;
            return Task.FromResult<string?>("5");
        }

        await gateway.HandleAsync(
            Message("go", sender: "stranger") with { ResolveRoomAsync = Resolve }, new RecordingResponder(), TestContext.Current.CancellationToken);
        await gateway.HandleAsync(
            Message("go") with { RoomId = "5", ResolveRoomAsync = Resolve }, new RecordingResponder(), TestContext.Current.CancellationToken);
        await gateway.HandleAsync(Message("plain"), new RecordingResponder(), TestContext.Current.CancellationToken);

        Assert.Equal(0, asked);
        Assert.Equal(["legal:go", "support:plain"], runner.Ran);
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
    public async Task A_message_that_arrives_while_the_host_stops_is_answered_with_that_sentence_and_no_acknowledgement()
    {
        // GAP-35: the runner refuses a run once the stop has begun, and the conversation reads
        // why — no "Working on it…", no Stop button attached to a run that never began.
        var (gateway, runner, _, router) = Build();
        runner.Admits = false;
        runner.Result = new HostedRunResult(HostedRunOutcome.HostStopping, null, CrewRunner.HostStoppingMessage);

        var responder = new RecordingResponder();
        await gateway.HandleAsync(Message("do the thing"), responder, TestContext.Current.CancellationToken);

        var reply = Assert.Single(responder.Sent);
        Assert.Equal("complete", reply.Kind);
        Assert.Equal(CrewRunner.HostStoppingMessage, reply.Text);
        Assert.Null(router.FindRun("thread-1"));
    }

    [Fact]
    public async Task A_literal_slash_command_typed_as_text_is_a_prompt_not_a_command()
    {
        // /stop and /status are registered slash commands the platform's client intercepts;
        // this gateway no longer parses text for them. A literal "/stop" that arrives as
        // plain message content is therefore work to do, like "/stopwatch the build" was.
        var (gateway, runner, _, _) = Build();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(Message("/stop"), responder, TestContext.Current.CancellationToken);

        Assert.Equal("support:/stop", Assert.Single(runner.Ran));
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
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
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
        var started = registry.TryStart("support", "test:thread-1", out _)!;
        runner.Result = new HostedRunResult(HostedRunOutcome.Completed, started.Id, "done");

        string? stopping = null;
        runner.Behaviour = (_, _) =>
        {
            stopping = gateway.Stop("thread-1");
            return Task.CompletedTask;
        };

        await gateway.HandleAsync(Message("do the thing"), new RecordingResponder(), TestContext.Current.CancellationToken);

        Assert.Equal("Stopping.", stopping);
        Assert.True(started.Cancellation.IsCancellationRequested);

        // And the conversation is released once the run is over.
        Assert.Null(router.FindRun("thread-1"));
    }

    [Fact]
    public void Stop_on_an_idle_conversation_says_so_rather_than_failing()
    {
        var (gateway, _, _, _) = Build();

        Assert.Contains("Nothing is running", gateway.Stop("thread-1"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_reports_what_the_conversation_is_doing()
    {
        var (gateway, runner, registry, _) = Build();
        var started = registry.TryStart("support", "test:thread-1", out _)!;
        runner.Result = new HostedRunResult(HostedRunOutcome.Completed, started.Id, "done");

        string? reported = null;
        runner.Behaviour = (_, _) =>
        {
            reported = gateway.Status("thread-1");
            return Task.CompletedTask;
        };

        await gateway.HandleAsync(Message("do the thing"), new RecordingResponder(), TestContext.Current.CancellationToken);

        var reply = reported!;
        Assert.Contains("support", reply, StringComparison.Ordinal);
        // A duration a person reads ("for 0s"), not an ISO timestamp to parse.
        Assert.Contains(" for ", reply, StringComparison.Ordinal);
    }

    private static InboundMessage WithFiles(string text, params string[] names) => Message(text) with
    {
        Attachments = [.. names.Select((name, i) => new InboundAttachment { Id = $"{i}", Name = name })],
    };

    [Theory]
    [InlineData("", "There is nothing to work on")]
    [InlineData("go", null)]
    public async Task Files_are_left_out_and_said_so_where_the_host_keeps_none(string text, string? answered)
    {
        var (gateway, runner, _, _) = Build();
        var responder = new RecordingResponder();
        var source = new FakeAttachmentSource().With("0", "x");

        await gateway.HandleAsync(WithFiles(text, "a.txt") with { AttachmentSource = source }, responder, TestContext.Current.CancellationToken);

        Assert.Equal(answered is null ? 1 : 0, runner.Ran.Count);
        Assert.Empty(source.Opened);
        Assert.Contains("Files are not taken here", responder.Sent[0].Text, StringComparison.Ordinal);
        Assert.Contains(answered ?? "Working", responder.Sent[0].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    public async Task A_message_with_nothing_in_it_is_answered_and_starts_no_run(string text)
    {
        var (gateway, runner, _, router) = Build();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(Message(text), responder, TestContext.Current.CancellationToken);

        Assert.Empty(runner.Ran);
        Assert.Equal(("complete", "There is nothing to work on: the message has no text."), Assert.Single(responder.Sent));
        Assert.True(router.TryBegin("thread-1"));
    }

    [Fact]
    public async Task A_message_of_files_alone_names_them_and_starts_no_run()
    {
        var (gateway, runner, _, _) = Build();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(WithFiles("", "march.pdf", "b.png"), responder, TestContext.Current.CancellationToken);

        Assert.Empty(runner.Ran);
        var (kind, text) = Assert.Single(responder.Sent);
        Assert.Equal("complete", kind);
        Assert.Contains("`march.pdf`, `b.png`", text, StringComparison.Ordinal);
        Assert.Contains("no text", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_text_of_a_message_with_files_runs_and_the_files_not_taken_are_named_once()
    {
        var (gateway, runner, _, _) = Build();
        var responder = new RecordingResponder();

        await gateway.HandleAsync(WithFiles("summarise", "march.pdf"), responder, TestContext.Current.CancellationToken);

        Assert.Equal("support:summarise", Assert.Single(runner.Ran));
        var naming = Assert.Single(responder.Sent, s => s.Text.Contains("`march.pdf`", StringComparison.Ordinal));
        Assert.Equal("ack", naming.Kind);
    }

    [Fact]
    public async Task The_names_of_files_are_made_safe_and_bounded()
    {
        var (gateway, _, _, _) = Build();
        var responder = new RecordingResponder();
        string[] names = ["../`@everyone`\u202E.exe", .. Enumerable.Range(0, 40).Select(i => new string('x', 300) + i)];

        await gateway.HandleAsync(WithFiles("", names), responder, TestContext.Current.CancellationToken);

        var text = Assert.Single(responder.Sent).Text;
        Assert.Contains("`'@everyone'_.exe`", text, StringComparison.Ordinal);
        Assert.Contains("and 31 more", text, StringComparison.Ordinal);
        Assert.True(text.Length < responder.MaxTextLength, $"{text.Length}");
    }

    [Fact]
    public async Task A_stranger_s_files_are_not_named_back()
    {
        var (gateway, _, _, _) = Build("someone-else");
        var responder = new RecordingResponder();

        await gateway.HandleAsync(WithFiles("", "march.pdf"), responder, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("march", Assert.Single(responder.Sent).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_is_handed_its_conversation_which_prepares_the_text()
    {
        var (gateway, runner, _, _) = Build();

        await gateway.HandleAsync(Message(" do the thing "), new RecordingResponder(), TestContext.Current.CancellationToken);

        var conversation = Assert.Single(runner.Conversations);
        Assert.NotNull(conversation);
        Assert.Equal(("test", "thread-1"), (conversation.Channel, conversation.ConversationId));
        var prepared = await conversation.PrepareAsync(TestContext.Current.CancellationToken);
        Assert.Equal("do the thing", prepared.Prompt);
        Assert.Null(prepared.Variables);
        Assert.Empty(prepared.RefusedFiles);
    }

    [Fact]
    public void A_conversation_s_key_is_a_folder_name_whatever_its_identifier_holds()
    {
        static string Key(string channel, string id) => new HostedConversation
        {
            Channel = channel,
            ConversationId = id,
            PrepareAsync = _ => Task.FromResult(new PreparedRun("")),
        }.Key;

        var key = Key("teams", "19:room@thread.tacv2;messageid=1700");

        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.Equal(key, Key("teams", "19:room@thread.tacv2;messageid=1700"));
        Assert.NotEqual(key, Key("discord", "19:room@thread.tacv2;messageid=1700"));
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
