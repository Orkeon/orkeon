using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>Scripted <see cref="IForgeAssistant"/>: a queue of replies, and every request kept.</summary>
internal sealed class ScriptedAssistant : IForgeAssistant
{
    private readonly Queue<ForgeAssistantReply> _replies = new();

    /// <summary>Every request received, in call order.</summary>
    public List<ForgeAssistantRequest> Requests { get; } = [];

    /// <summary>Queues a conversation turn.</summary>
    /// <param name="message">What the assistant says.</param>
    /// <param name="usage">What the turn cost; default is a free turn.</param>
    public ScriptedAssistant Says(string message, ForgeUsageSnapshot usage = default)
    {
        _replies.Enqueue(new ForgeAssistantReply { Message = message, Usage = usage });
        return this;
    }

    /// <summary>Queues a <c>brief_submit</c>.</summary>
    /// <param name="json">The submitted brief.</param>
    /// <param name="usage">What the turn cost; default is a free turn.</param>
    public ScriptedAssistant SubmitsBrief(string json, ForgeUsageSnapshot usage = default)
    {
        _replies.Enqueue(new ForgeAssistantReply { BriefJson = json, Usage = usage });
        return this;
    }

    /// <summary>Queues a <c>blueprint_submit</c>.</summary>
    public ScriptedAssistant SubmitsBlueprint(string json)
    {
        _replies.Enqueue(new ForgeAssistantReply { BlueprintJson = json });
        return this;
    }

    /// <summary>The mid-turn readings each queued reply announces before it returns.</summary>
    public List<ForgeUsageSnapshot> Steps { get; } = [];

    /// <inheritdoc />
    public Task<ForgeAssistantReply> NextAsync(
        ForgeAssistantRequest request,
        Action<ForgeUsageSnapshot>? spent,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var reply = _replies.Count > 0 ? _replies.Dequeue() : new ForgeAssistantReply();

        // A real turn is several model calls. The double reports the turn's spend in two
        // instalments so a caller that only listens to the RETURN value fails the test.
        if (spent is not null && reply.Usage.TotalTokens > 0)
        {
            var half = new ForgeUsageSnapshot(
                reply.Usage.PromptTokens / 2, reply.Usage.CompletionTokens / 2, reply.Usage.EstimatedTokens / 2);
            Steps.Add(half);
            spent(half);
            Steps.Add(reply.Usage);
            spent(reply.Usage);
        }

        return Task.FromResult(reply);
    }
}

/// <summary>Scripted <see cref="IForgeUserChannel"/>: queued messages, decisions and blueprints, then EOF.</summary>
internal sealed class ScriptedUserChannel(params string[] messages) : IForgeUserChannel
{
    private readonly Queue<string> _messages = new(messages);
    private readonly Queue<string> _decisions = new();
    private readonly Queue<string> _blueprints = new();

    /// <summary>Queues an arbitration answer.</summary>
    public ScriptedUserChannel Decides(string decision)
    {
        _decisions.Enqueue(decision);
        return this;
    }

    /// <summary>Queues the blueprint an <c>edit</c> decision will hand back.</summary>
    public ScriptedUserChannel Edits(string blueprintJson)
    {
        _blueprints.Enqueue(blueprintJson);
        return this;
    }

    /// <inheritdoc />
    public Task<string?> ReadUserMessageAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_messages.Count > 0 ? _messages.Dequeue() : null);

    /// <inheritdoc />
    public Task<string?> ReadDecisionAsync(IReadOnlyList<string> options, CancellationToken cancellationToken) =>
        Task.FromResult(_decisions.Count > 0 ? _decisions.Dequeue() : null);

    /// <inheritdoc />
    public Task<string?> ReadBlueprintAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_blueprints.Count > 0 ? _blueprints.Dequeue() : null);
}

/// <summary>
/// The four F3 stages under the real engine (SPEC-ORKEON-FORGE §4, §7, §8): the dry cycle
/// end to end, the repair loop, the submission caps, and the resume short-circuits — all
/// over scripted seams, no LLM, no process.
/// </summary>
public sealed class ForgeStagesTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-stages-" + Guid.NewGuid().ToString("N"));

    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _output.Dispose();
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private ForgeEngine Engine(ForgeSession session, params IForgeStageRunner[] runners) =>
        new(session, new ForgeEventWriter(_output, new FakeOrkeonClock()), runners, new FakeOrkeonClock());

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line)),
    ];

    private IReadOnlyList<string> Kinds() => [.. Events().Select(e => e.GetProperty("kind").GetString()!)];

    private static IForgeStageRunner[] DryRunners(ScriptedAssistant assistant, IForgeUserChannel channel, string? need = null) =>
    [
        new BriefStage(assistant, channel, need),
        new BlueprintStage(assistant),
        new RenderStage(),
        new ValidateStage(ForgeDocuments.KnownTools),
    ];

    [Fact]
    public async Task The_dry_cycle_interviews_generates_validates_and_pauses_before_the_test()
    {
        var assistant = new ScriptedAssistant()
            .Says("Quel est le fournisseur ?")
            .SubmitsBrief(ForgeDocuments.ValidBrief)
            .SubmitsBlueprint(ForgeDocuments.ValidBlueprint);
        var channel = new ScriptedUserChannel("exemple.fr, chaque matin");
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, DryRunners(assistant, channel, "je veux résumer les offres"))
            .RunAsync(stopBefore: ForgeState.Test, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Paused, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ForgeState.Test, session.State);
        Assert.Equal(ForgeSessionStatus.Active, session.Status);

        // The conversation flowed: the first turn carried the typed need, the answer the user's reply.
        Assert.Equal("je veux résumer les offres", assistant.Requests[0].UserMessage);
        Assert.Equal("exemple.fr, chaque matin", assistant.Requests[1].UserMessage);

        // The artifacts exist and the crew is rendered per-entity.
        Assert.True(File.Exists(Path.Combine(session.Directory, ForgeSession.BriefFileName)));
        Assert.True(File.Exists(Path.Combine(session.Directory, "crew", "agents", "collecteur.yaml")));

        // The stream tells the whole story, ending paused.
        Assert.Equal(
            ["session.started", "stage.entered", "assistant.message", "brief.ready",
             "stage.entered", "blueprint.ready", "stage.entered", "file.written", "file.written",
             "file.written", "file.written", "file.written", "stage.entered", "validation.result",
             "session.finished"],
            Kinds());
        Assert.Equal("paused", Events()[^1].GetProperty("status").GetString());

        // The session title came from the brief's goal.
        Assert.StartsWith("Résumer chaque matin", session.Document.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bad_tool_goes_through_one_repair_and_then_validates()
    {
        var badBlueprint = ForgeDocuments.ValidBlueprint.Replace("web_scrape", "invented_tool", StringComparison.Ordinal);
        var assistant = new ScriptedAssistant()
            .SubmitsBrief(ForgeDocuments.ValidBrief)
            .SubmitsBlueprint(badBlueprint)
            .SubmitsBlueprint(ForgeDocuments.ValidBlueprint);
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, DryRunners(assistant, new ScriptedUserChannel()))
            .RunAsync(stopBefore: ForgeState.Test, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Paused, result.Outcome);

        // The repair request carried the validator's words and the previous blueprint.
        var repair = assistant.Requests[^1];
        Assert.Equal(ForgeAssistantPhase.Blueprint, repair.Phase);
        Assert.NotNull(repair.PreviousBlueprint);
        Assert.Contains(repair.Errors!, e => e.Contains("invented_tool", StringComparison.Ordinal));

        // repair.started was evented; the repair state was cleaned once validation passed.
        Assert.Contains("repair.started", Kinds());
        Assert.False(File.Exists(Path.Combine(session.Directory, ForgeSession.RepairFileName)));
        Assert.Equal(0, session.Document.RepairAttempts);
        Assert.Equal(2, session.Document.Budget.ConsumedIterations);
    }

    [Fact]
    public async Task Validation_still_failing_after_two_repairs_fails_with_its_own_code()
    {
        var badBlueprint = ForgeDocuments.ValidBlueprint.Replace("web_scrape", "invented_tool", StringComparison.Ordinal);
        var assistant = new ScriptedAssistant()
            .SubmitsBrief(ForgeDocuments.ValidBrief)
            .SubmitsBlueprint(badBlueprint)
            .SubmitsBlueprint(badBlueprint)
            .SubmitsBlueprint(badBlueprint);
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, DryRunners(assistant, new ScriptedUserChannel()))
            .RunAsync(stopBefore: ForgeState.Test, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Failed, result.Outcome);
        Assert.Contains(ForgeErrorCodes.ValidationFailed, session.Document.Error, StringComparison.Ordinal);

        var finalError = Events().Last(e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal(ForgeErrorCodes.ValidationFailed, finalError.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Three_invalid_briefs_fail_the_interview_with_its_own_code()
    {
        var assistant = new ScriptedAssistant()
            .SubmitsBrief("{}")
            .SubmitsBrief("{}")
            .SubmitsBrief("{}");
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, DryRunners(assistant, new ScriptedUserChannel()))
            .RunAsync(stopBefore: ForgeState.Test, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Failed, result.Outcome);
        Assert.Contains(ForgeErrorCodes.BriefIncomplete, session.Document.Error, StringComparison.Ordinal);

        // The second and third turns carried the schema errors back, verbatim.
        Assert.All(assistant.Requests.Skip(1), r => Assert.NotEmpty(r.Errors!));
    }

    [Fact]
    public async Task A_closed_channel_interrupts_instead_of_answering_for_the_user()
    {
        var assistant = new ScriptedAssistant().Says("Quel est l'objectif ?");
        var session = ForgeSession.Create(_workspace, "veille");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Engine(session, DryRunners(assistant, new ScriptedUserChannel()))
                .RunAsync(stopBefore: ForgeState.Test, cancellationToken: TestContext.Current.CancellationToken));

        // Interrupted, not finished: the session is still at the interview, resumable.
        Assert.Equal(ForgeSessionStatus.Active, session.Status);
    }

    [Fact]
    public async Task A_resumed_session_with_a_brief_does_not_re_interview()
    {
        var first = new ScriptedAssistant().SubmitsBrief(ForgeDocuments.ValidBrief);
        var session = ForgeSession.Create(_workspace, "veille");
        await Engine(session, [new BriefStage(first, new ScriptedUserChannel())])
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille", out var resumed, out var error), error);
        var second = new ScriptedAssistant().SubmitsBlueprint(ForgeDocuments.ValidBlueprint);

        var result = await Engine(resumed!, DryRunners(second, new ScriptedUserChannel()))
            .RunAsync(resumed: true, stopBefore: ForgeState.Test, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Paused, result.Outcome);

        // The brief phase never called the assistant again: its only request was the blueprint.
        var request = Assert.Single(second.Requests);
        Assert.Equal(ForgeAssistantPhase.Blueprint, request.Phase);
        Assert.Equal("Résumer chaque matin les nouvelles offres du fournisseur", request.Brief!.Goal);
    }

    /// <summary>
    /// The owner's report, on a screenshot of step 1 mid-interview: still no live view of
    /// the tokens exchanged. The meter was emitted by the ENGINE, once a stage returned — and an
    /// interview is one stage of up to twenty-four turns, each of which is itself several
    /// model calls (the assistant reads the workspace before answering). So it stood at zero
    /// for the whole conversation and then jumped in one step.
    /// <para>
    /// It now reports on every model call: the scripted assistant announces its spend in two
    /// instalments, and a caller that only reads the RETURN value fails this test.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_meter_moves_on_every_model_call_not_once_at_the_end()
    {
        var assistant = new ScriptedAssistant()
            .Says("Quel est le fournisseur ?", new ForgeUsageSnapshot(900, 120, 0))
            .SubmitsBrief(ForgeDocuments.ValidBrief, new ForgeUsageSnapshot(300, 40, 0));
        var session = ForgeSession.Create(_workspace, "veille");

        await Engine(session, new BriefStage(assistant, new ScriptedUserChannel("exemple.fr")))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        var totals = Events()
            .Where(e => e.GetProperty("kind").GetString() == "cost.updated")
            .Select(e => e.GetProperty("tokens").GetInt64())
            .ToList();

        // Two calls per turn, two turns — plus the engine's closing reading at the boundary.
        // Cumulative and monotonic: a meter that resets between turns is not a meter.
        Assert.Equal([510, 1020, 1190, 1360, 1360], totals);

        var last = Events().Last(e => e.GetProperty("kind").GetString() == "cost.updated");
        Assert.Equal(1200, last.GetProperty("promptTokens").GetInt64());
        Assert.Equal(160, last.GetProperty("completionTokens").GetInt64());

        // And the budget was charged ONCE, by the engine, for the whole stage: the live
        // readings add the pending spend without ever paying for it a second time.
        Assert.Equal(1360, session.Document.Budget.ConsumedTokens);
    }
}
