using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Jint;
using Jint.Native;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Security;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-04: <c>act(prompt, {onToolCall})</c> is called before each tool call, before the permission gate.</summary>
public sealed class ActToolCallbacksTests
{
    private sealed record Run(
        FakeToolCallingLlmProvider Provider, FakeRecordingTool Tool, StubSequencePermissionGate Gate, StubToolApprovalChannel Channel, List<string> Log, string Output, int Seen, string Recorded)
    {
        public string ToolResultOfTurn2 => Provider.Turns[1][^1].Content;
    }

    private static string Counting(string answer)
        => "(call) => { globalThis.seen = (globalThis.seen ?? 0) + 1; return '" + answer + "'; }";

    private static async Task<Run> RunAsync(string onToolCallJs, Func<IReadOnlyDictionary<string, object?>, bool>? gateDenies = null,
        ToolApprovalDecision channelAnswer = ToolApprovalDecision.AllowOnce, string? onToolResultJs = null, bool registerGate = true, string calledTool = "file_read")
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var log = new List<string>();
        var tool = new FakeRecordingTool("file_read", log);
        var channel = new StubToolApprovalChannel { Decision = channelAnswer };
        var gate = new StubSequencePermissionGate(new ModePermissionGate(channel), log, gateDenies);
        var provider = new FakeToolCallingLlmProvider(calledTool, "{\"path\":\"/x\"}");
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, registerGate ? new JsLlmActGuards { PermissionGate = gate } : new JsLlmActGuards());

        var options = await engine.EvaluateAsync("({ permissionMode: \"default\", onToolCall: " + onToolCallJs
            + (onToolResultJs is null ? "" : ", onToolResult: " + onToolResultJs) + " })", null, TestContext.Current.CancellationToken);
        var result = await facade.ActAsync(engine, "read it", options);
        var seen = (int)(await engine.EvaluateAsync("globalThis.seen ?? 0", null, TestContext.Current.CancellationToken)).AsNumber();
        var recorded = (await engine.EvaluateAsync("globalThis.rec === undefined ? '' : String(globalThis.rec)", null, TestContext.Current.CancellationToken)).AsString();
        return new Run(provider, tool, gate, channel, log, result.Get("output").AsString(), seen, recorded);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldRunTool_WhenOnToolCallAllows()
    {
        var run = await RunAsync(Counting("allow"));

        Assert.Equal(1, run.Seen);
        Assert.Equal(["/x"], run.Tool.Inputs);
        Assert.Equal("final: " + run.ToolResultOfTurn2, run.Output);
        Assert.Equal("{\"ok\":true}", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldRefuseWithoutGateNorCallback_WhenToolIsNotOffered()
    {
        var run = await RunAsync(Counting("allow"), calledTool: "read");

        Assert.Equal(0, run.Seen);
        Assert.Empty(run.Gate.Paths);
        Assert.Empty(run.Tool.Inputs);
        Assert.Equal("ERROR: tool 'read' is not available to this agent.", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldBlockWithReason_WhenOnToolCallDenies()
    {
        var run = await RunAsync("(call) => ({ deny: 'no' })");

        Assert.Empty(run.Tool.Inputs);
        Assert.Equal("DENIED: no", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldGateOnlyTheRewrittenInput_WhenOnToolCallRewrites()
    {
        var run = await RunAsync("(call) => ({ input: { path: '/y' } })");

        Assert.Equal(["/y"], run.Gate.Paths);
        Assert.Equal(["/y"], run.Tool.Inputs);
    }

    [Fact]
    [Trait("BR", "CH-303")]
    public async Task ShouldRunTheCall_WhenTheGateRefusesTheRawSpellingTheCallbackCanonicalises()
    {
        var run = await RunAsync("(call) => ({ input: { path: '/workspace/a.ts' } })", gateDenies: args => Equals(args["path"], "/x"));

        Assert.Equal(["/workspace/a.ts"], run.Gate.Paths);
        Assert.Equal(["/workspace/a.ts"], run.Tool.Inputs);
    }

    [Fact]
    [Trait("BR", "CH-303")]
    public async Task ShouldNotConsultTheGate_WhenOnToolCallDenies()
    {
        var run = await RunAsync("(call) => ({ deny: 'no' })");

        Assert.Empty(run.Gate.Paths);
        Assert.Equal("DENIED: no", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "CH-307")]
    public async Task ShouldDenyWithoutConsultingTheGate_WhenOnToolCallReturnsContext()
    {
        var run = await RunAsync("(call) => ({ context: 'note' })");

        Assert.Empty(run.Gate.Paths);
        Assert.Empty(run.Tool.Inputs);
        Assert.Equal("DENIED: onToolCall returned no valid verdict", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldDenyWithMessage_WhenOnToolCallThrows()
    {
        var run = await RunAsync("(call) => { throw new Error('boom'); }");

        Assert.Empty(run.Tool.Inputs);
        Assert.Equal("DENIED: boom", run.ToolResultOfTurn2);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    [Trait("BR", "CH-303")]
    public async Task ShouldRunCallbackBeforeGate_WhenBothApply()
    {
        var allowed = await RunAsync(Counting("allow"));
        var denied = await RunAsync(Counting("allow"), gateDenies: _ => true);

        Assert.Equal(1, allowed.Seen);
        Assert.Equal(1, denied.Seen);
        Assert.Empty(denied.Tool.Inputs);
        Assert.Equal(["gate:/x", "tool:/x"], allowed.Log);
        Assert.Equal(["gate:/x"], denied.Log);
        Assert.Equal("DENIED: gate says no", denied.ToolResultOfTurn2);
    }

    [Theory]
    [Trait("BR", "RT-04")]
    [InlineData(ToolApprovalDecision.AllowOnce, 1, true)]
    [InlineData(ToolApprovalDecision.Deny, 0, false)]
    public async Task ShouldHandOffToApprovalChannel_WhenCallbackAnswersAsk(ToolApprovalDecision answer, int toolRuns, bool toolResultReachesModel)
    {
        var run = await RunAsync(Counting("ask"), channelAnswer: answer);

        var ask = Assert.Single(run.Channel.Asks);
        Assert.Equal("file_read", ask.ToolName);
        Assert.Equal(toolRuns, run.Tool.Inputs.Count);
        Assert.Equal(toolResultReachesModel, run.ToolResultOfTurn2 == "{\"ok\":true}");
        Assert.Equal(!toolResultReachesModel, run.ToolResultOfTurn2.StartsWith("DENIED:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldPassResultToOnToolResult_WhenToolCompletes()
    {
        var run = await RunAsync(Counting("allow"),
            onToolResultJs: "(call, result) => { globalThis.rec = (globalThis.rec ?? []).concat([call.name + '|' + JSON.stringify(call.input) + '|' + JSON.stringify(result)]); }");

        Assert.Equal(["/x"], run.Tool.Inputs);
        Assert.Equal("{\"ok\":true}", run.ToolResultOfTurn2);
        Assert.Equal(1, run.Seen);
        Assert.Equal("file_read|{\"path\":\"/x\"}|{\"ok\":true}", run.Recorded);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldReplaceContextLine_WhenOnToolResultReturnsContext()
    {
        var replaced = await RunAsync(Counting("allow"), onToolResultJs: "(call, result) => ({ context: 'summary' })");
        var untouched = await RunAsync(Counting("allow"), onToolResultJs: "(call, result) => { globalThis.rec = 'ran'; }");

        Assert.Equal("summary", replaced.ToolResultOfTurn2);
        Assert.Equal("{\"ok\":true}", untouched.ToolResultOfTurn2);
        Assert.Equal("ran", untouched.Recorded);
    }

    [Theory]
    [Trait("BR", "RT-04")]
    [InlineData("(call) => {}")]
    [InlineData("(call) => null")]
    [InlineData("(call) => ({ input: 'x' })")]
    [InlineData("(call) => ({ input: [1] })")]
    [InlineData("(call) => ({ input: null })")]
    public async Task ShouldDenyAndNotReject_WhenOnToolCallReturnsUnknownVerdict(string onToolCallJs)
    {
        var run = await RunAsync(onToolCallJs);

        Assert.Empty(run.Tool.Inputs);
        Assert.StartsWith("DENIED:", run.ToolResultOfTurn2, StringComparison.Ordinal);
        Assert.Contains("onToolCall", run.ToolResultOfTurn2, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldDeny_WhenCallbackAsksAndNoPermissionGateRegistered()
    {
        var run = await RunAsync(Counting("ask"), registerGate: false);

        Assert.Empty(run.Tool.Inputs);
        Assert.StartsWith("DENIED:", run.ToolResultOfTurn2, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-04")]
    public async Task ShouldStayOffEngineThread_WhenCallbacksRunInLoop()
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var probe = new FakeOverlapProbe();
        engine.SetValue("__probe", new Action<string>(probe.InCallback));
        var log = new List<string>();
        var tool = new FakeProbedTool("file_read", log, probe);
        var gate = new StubSequencePermissionGate(new ModePermissionGate(new StubToolApprovalChannel()), log, null);
        var provider = new FakeToolCallingLlmProvider("file_read", "{\"path\":\"/x\"}");
        var facade = new JsLlmFacade(
            engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { PermissionGate = gate });

        var options = await engine.EvaluateAsync(
            "({ permissionMode: 'default', onToolCall: (c) => { __probe('call'); return 'allow'; }, onToolResult: (c, r) => { __probe('result'); } })",
            null, TestContext.Current.CancellationToken);
        await facade.ActAsync(engine, "read it", options);

        Assert.Equal(["call", "tool", "result"], probe.Events);
        Assert.Equal(1, probe.MaxActive);
    }









    // #327: onContextPressure. Two reads of a large result, then an answer; the callback runs before
    // the second and third model turns once the threshold is passed.
    private sealed record PressureRun(FakeToolCallingLlmProvider Provider, string Recorded, string Output);

    private static async Task<PressureRun> RunPressureAsync(string onContextPressureJs, string settingsJs, ToolAccess access = ToolAccess.Read)
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var tool = new StubLargeResultTool("file_read", 8_000, access);
        var provider = new FakeToolCallingLlmProvider(
            FakeToolCallingLlmProvider.ScriptStep.Call("file_read", "{\"path\":\"/a\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Call("file_read", "{\"path\":\"/b\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("done"));
        var facade = new JsLlmFacade(engine, provider, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards());

        var options = await engine.EvaluateAsync(
            "({ onContextPressure: " + onContextPressureJs + ", contextPressure: " + settingsJs + " })", null, TestContext.Current.CancellationToken);
        var result = await facade.ActAsync(engine, "read both", options);
        var recorded = (await engine.EvaluateAsync("globalThis.rec === undefined ? '' : JSON.stringify(globalThis.rec)", null, TestContext.Current.CancellationToken)).AsString();
        return new PressureRun(provider, recorded, result.Get("output").AsString());
    }

    private const string Recording = "(p) => { globalThis.rec = (globalThis.rec ?? []).concat([p]); ";

    private static int ToolChars(LlmMessage[] turn) => turn.Where(m => m.Role == "tool").Sum(m => m.Content.Length);

    [Fact]
    [Trait("BR", "CH-327")]
    public async Task ShouldDropTheResultAndSaySo_WhenOnContextPressureAnswersDrop()
    {
        var run = await RunPressureAsync(Recording + "return { 'call-1': 'drop' }; }", "{ thresholdTokens: 0, keepRecent: 0 }");

        Assert.Equal("done", run.Output);
        var turn2 = run.Provider.Turns[1];
        var pruned = turn2.Single(m => m.Role == "tool").Content;
        Assert.StartsWith("[pruned to save context: the result of file_read {\"path\":\"/a\"}", pruned, StringComparison.Ordinal);
        Assert.Contains("Call file_read again with the same input", pruned, StringComparison.Ordinal);

        // The pressure the script saw before the prune, against the tool share of what the model was sent after it.
        using var payloads = System.Text.Json.JsonDocument.Parse(run.Recorded);
        var first = payloads.RootElement[0];
        Assert.Equal("call-1", first.GetProperty("calls")[0].GetProperty("id").GetString());
        Assert.Equal("file_read", first.GetProperty("calls")[0].GetProperty("name").GetString());
        Assert.Equal("/a", first.GetProperty("calls")[0].GetProperty("input").GetProperty("path").GetString());
        var toolBefore = first.GetProperty("usedTokensByKind").GetProperty("tool").GetInt32();
        Assert.True(ToolChars(turn2) / 4 < toolBefore, $"tool tokens {ToolChars(turn2) / 4} after the prune, {toolBefore} before");

        // A pruned call is never offered again: the second pressure names call-2 alone.
        var second = payloads.RootElement[1].GetProperty("calls");
        Assert.Equal("call-2", Assert.Single(second.EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    [Trait("BR", "CH-327")]
    public async Task ShouldKeepTheHeadAndSaySo_WhenOnContextPressureAnswersHead()
    {
        var run = await RunPressureAsync("(p) => ({ 'call-1': 'head' })", "{ thresholdTokens: 0, keepRecent: 0, headChars: 100 }");

        var pruned = run.Provider.Turns[1].Single(m => m.Role == "tool").Content;
        Assert.StartsWith("{\"text\":\"xxxx", pruned, StringComparison.Ordinal);
        Assert.Contains("\n[pruned to save context: ", pruned, StringComparison.Ordinal);
        Assert.Contains("characters cut here. Call file_read {\"path\":\"/a\"} again for the full result.]", pruned, StringComparison.Ordinal);
        Assert.True(pruned.Length < 400);
    }

    [Theory]
    [Trait("BR", "CH-327")]
    [InlineData("(p) => { throw new Error('decision failed'); }")]
    [InlineData("(p) => undefined")]
    [InlineData("(p) => 'drop'")]
    [InlineData("(p) => ({ 'call-1': 'keep' })")]
    public async Task ShouldPruneNothing_WhenOnContextPressureFailsOrKeeps(string callback)
    {
        var run = await RunPressureAsync(callback, "{ thresholdTokens: 0, keepRecent: 0 }");

        Assert.Equal("done", run.Output);
        Assert.All(run.Provider.Turns[2].Where(m => m.Role == "tool"), m => Assert.True(m.Content.Length > 8_000));
    }

    [Fact]
    [Trait("BR", "CH-327")]
    public async Task ShouldNotAsk_WhenTheLoopIsBelowTheThresholdOrEveryResultIsRecent()
    {
        var below = await RunPressureAsync(Recording + "return { 'call-1': 'drop' }; }", "{}");
        var recent = await RunPressureAsync(Recording + "return { 'call-1': 'drop' }; }", "{ thresholdTokens: 0, keepRecent: 100 }");

        Assert.Equal("", below.Recorded);
        Assert.Equal("", recent.Recorded);
        Assert.All(below.Provider.Turns[2].Where(m => m.Role == "tool"), m => Assert.True(m.Content.Length > 8_000));
    }

    [Fact]
    [Trait("BR", "CH-327")]
    public async Task ShouldNotOfferTheResultAgain_WhenOnContextPressureKeepsIt()
    {
        var run = await RunPressureAsync(Recording + "return { 'call-1': 'keep' }; }", "{ thresholdTokens: 0, keepRecent: 0 }");

        using var payloads = System.Text.Json.JsonDocument.Parse(run.Recorded);
        Assert.Equal(2, payloads.RootElement.GetArrayLength());
        var second = payloads.RootElement[1].GetProperty("calls");
        Assert.Equal("call-2", Assert.Single(second.EnumerateArray()).GetProperty("id").GetString());
        Assert.All(run.Provider.Turns[2].Where(m => m.Role == "tool"), m => Assert.True(m.Content.Length > 8_000));
    }

    [Theory]
    [Trait("BR", "CH-327")]
    [InlineData(ToolAccess.Edit)]
    [InlineData(ToolAccess.Execute)]
    [InlineData(ToolAccess.Unspecified)]
    public async Task ShouldNotOfferTheResult_WhenTheToolIsNotARead(ToolAccess access)
    {
        var run = await RunPressureAsync(Recording + "return { 'call-1': 'drop' }; }", "{ thresholdTokens: 0, keepRecent: 0 }", access);

        Assert.Equal("done", run.Output);
        Assert.Equal("", run.Recorded);
    }

    // #461: prune. Three reads of a large result, then an answer; the host's pruning - the crew loops' stage,
    // resolved from AddOrkeonApplication - is asked before each model turn. A result is offered once a model turn
    // answered it: read /a before the third turn, /b before the fourth.
    private sealed record PruneRun(
        FakeToolCallingLlmProvider Provider, StubPruneDecisionModel Model, FakeAgentPruneObserver Observer, RecordingLogger Logger, JsValue Result)
    {
        public string Output => Result.Get("output").AsString();

        public JsValue Pruned => Result.Get("pruned");

        public int PrunedCount(string field) => (int)Pruned.Get(field).AsNumber();
    }

    // A fill share this small judges from the first read on: the window is the 200,000-token default.
    private const string Eager = "{ fillShare: 0.0001, keepRecentMessages: 0 }";

    private static async Task<PruneRun> RunPruneAsync(
        string optionsJs,
        Orkeon.Application.Crew.Compaction.ToolResultPruningOptions? configured = null,
        ToolAccess access = ToolAccess.Read,
        Orkeon.Application.Crew.Execution.DecisionCircuit? circuit = null,
        Action<StubPruneDecisionModel, JsLlmFacade>? arrange = null,
        Orkeon.Domain.Autonomous.AgentExecutionBudget? budget = null,
        int tokensPerTurn = 0)
    {
        using var engine = new Engine(options => options.Constraints.PromiseTimeout = HangGuards.EnginePromise);
        var model = new StubPruneDecisionModel();
        var observer = new FakeAgentPruneObserver();
        using var loggers = new RecordingLoggerFactory();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(loggers);
        services.AddLogging();
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IDecisionModel>(model);
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IAgentPruneObserver>(observer);
        if (circuit is not null)
            services.AddSingleton(circuit);
        if (configured is not null)
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(configured));
        services.AddOrkeonApplication();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var pruner = scope.ServiceProvider.GetRequiredService<Orkeon.Application.Crew.Compaction.IConversationPruner>();

        var tool = new StubLargeResultTool("file_read", 8_000, access);
        var llm = new FakeToolCallingLlmProvider(
            FakeToolCallingLlmProvider.ScriptStep.Call("file_read", "{\"path\":\"/a\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Call("file_read", "{\"path\":\"/b\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Call("file_read", "{\"path\":\"/c\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("done"))
        {
            TokensPerTurn = tokensPerTurn,
        };
        var facade = new JsLlmFacade(engine, llm, CancellationToken.None, new IBaseTool[] { tool }, new JsLlmActGuards { Budget = budget },
            new JsLlmObservability { AgentName = "Reader" }, pruner);
        arrange?.Invoke(model, facade);

        var options = await engine.EvaluateAsync("(" + optionsJs + ")", null, TestContext.Current.CancellationToken);
        var result = await facade.ActAsync(engine, "read both", options);
        return new PruneRun(llm, model, observer, loggers.Logger, result);
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldDropTheOlderReadsAndSaySo_WhenPruneIsTrue()
    {
        // Arrange: the configured numbers judge from the first read on.
        var configured = new Orkeon.Application.Crew.Compaction.ToolResultPruningOptions { FillShare = 0.0001, KeepRecentMessages = 0 };

        // Act
        var run = await RunPruneAsync("{ prune: true }", configured);

        // Assert
        Assert.Equal("done", run.Output);
        Assert.StartsWith("[pruned to save context: the result of file_read {\"path\":\"/a\"}", run.Provider.Turns[2].First(m => m.Role == "tool").Content, StringComparison.Ordinal);
        Assert.Equal(2, run.Model.Requests.Count);
        Assert.Equal((2, 2, 0, 0, 2), (run.PrunedCount("offered"), run.PrunedCount("judged"), run.PrunedCount("kept"), run.PrunedCount("cut"), run.PrunedCount("dropped")));
        Assert.True(run.PrunedCount("freedTokens") > 3_000);
        Assert.Equal(2, run.Observer.Notices.Count);
        Assert.All(run.Observer.Notices, n => Assert.Equal("Reader", n.AgentRole));
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldTellTheDecisionModelThePromptAsTheTask_WhenPruneJudges()
    {
        // Act
        var run = await RunPruneAsync("{ prune: " + Eager + " }");

        // Assert
        Assert.All(run.Model.Requests, r => Assert.Contains("read both", r.State.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldMakeNoDecisionCall_WhenTheCallSetsNoPruneEvenWithPruningEnabled()
    {
        // Arrange: Orkeon:Compaction:Prune:Enabled is the crew loops' switch.
        var configured = new Orkeon.Application.Crew.Compaction.ToolResultPruningOptions { Enabled = true, FillShare = 0.0001, KeepRecentMessages = 0 };

        // Act
        var run = await RunPruneAsync("{}", configured);

        // Assert
        Assert.Equal("done", run.Output);
        Assert.Empty(run.Model.Requests);
        Assert.True(run.Pruned.IsUndefined());
        Assert.All(run.Provider.Turns[3].Where(m => m.Role == "tool"), m => Assert.True(m.Content.Length > 8_000));
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldJudgeOnTheCallsNumbers_WhenPruneIsAnObject()
    {
        // Act: the configured fill share (0.4 of 200,000 tokens) is never reached; the call's own is.
        var configured = await RunPruneAsync("{ prune: true }");
        var overridden = await RunPruneAsync("{ prune: " + Eager + " }");

        // Assert
        Assert.Empty(configured.Model.Requests);
        Assert.True(configured.Pruned.IsUndefined());
        Assert.Equal(2, overridden.PrunedCount("dropped"));
    }

    [Theory]
    [Trait("BR", "CH-461")]
    [InlineData(ToolAccess.Edit)]
    [InlineData(ToolAccess.Execute)]
    public async Task ShouldOfferNoResult_WhenTheToolIsNotARead(ToolAccess access)
    {
        // Act
        var run = await RunPruneAsync("{ prune: " + Eager + " }", access: access);

        // Assert
        Assert.Equal("done", run.Output);
        Assert.Empty(run.Model.Requests);
        Assert.True(run.Pruned.IsUndefined());
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldSayWhatWasPruned_WhenTheActIsExhausted()
    {
        // Act
        var run = await RunPruneAsync("{ maxIterations: 3, prune: " + Eager + " }");

        // Assert
        Assert.True(run.Result.Get("exhausted").AsBoolean());
        Assert.Equal(1, run.PrunedCount("dropped"));
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldSayWhatWasPruned_WhenTheActIsInterrupted()
    {
        // Arrange: the script interrupts while the second pruning is asked.
        void InterruptOnSecond(StubPruneDecisionModel model, JsLlmFacade facade) =>
            model.OnDecide = n => { if (n == 2) facade.interrupt(); };

        // Act
        var run = await RunPruneAsync("{ prune: " + Eager + " }", arrange: InterruptOnSecond);

        // Assert
        Assert.True(run.Result.Get("interrupted").AsBoolean());
        Assert.Equal(1, run.PrunedCount("dropped"));
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldSayWhatWasPruned_WhenTheTokenBudgetEndsTheAct()
    {
        // Arrange: the third call spends the budget; the wrap-up answers.
        var budget = new Orkeon.Domain.Autonomous.AgentExecutionBudget { MaxToolCalls = 10, MaxTokensConsumed = 150 };

        // Act
        var run = await RunPruneAsync("{ prune: " + Eager + " }", budget: budget, tokensPerTurn: 60);

        // Assert
        Assert.True(run.Result.Get("budgetExhausted").AsBoolean());
        Assert.Equal(1, run.PrunedCount("dropped"));
    }

    [Fact]
    [Trait("BR", "CH-461")]
    public async Task ShouldAskNothingAndWarnOnce_WhenTheDecisionCircuitIsOff()
    {
        // Act
        var run = await RunPruneAsync("{ prune: " + Eager + " }", circuit: Orkeon.Application.Crew.Execution.DecisionCircuit.Off);

        // Assert
        Assert.Equal("done", run.Output);
        Assert.Empty(run.Model.Requests);
        Assert.True(run.Pruned.IsUndefined());
        var warning = Assert.Single(run.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("decision circuit is off", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-461")]
    [InlineData("{ prune: true, onContextPressure: (p) => ({}) }", "onContextPressure")]
    [InlineData("{ prune: true, contextPressure: { thresholdTokens: 0 } }", "contextPressure")]
    public async Task ShouldRefuseTheCall_WhenPruneComesWithTheScriptsOwnPruning(string optionsJs, string other)
    {
        // Act
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => RunPruneAsync(optionsJs));

        // Assert
        Assert.Contains("prune", refused.Message, StringComparison.Ordinal);
        Assert.Contains(other, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-461")]
    [InlineData("{ prune: { fillShare: 0 } }", "fillShare")]
    [InlineData("{ prune: { fillShare: 1.5 } }", "fillShare")]
    [InlineData("{ prune: { keepRecentMessages: -1 } }", "keepRecentMessages")]
    [InlineData("{ prune: { headChars: 0 } }", "headChars")]
    [InlineData("{ prune: 'yes' }", "prune")]
    public async Task ShouldRefuseTheCall_WhenAPruneNumberIsOutOfItsBounds(string optionsJs, string named)
    {
        // Act
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => RunPruneAsync(optionsJs));

        // Assert
        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
    }
}
