using System.Text.Json;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-03: a background or spawned child becomes an addressable instance: crew.instances(), crew.send(id, message), crew.stop(id).</summary>
public sealed class AgentInstanceRegistryTests
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    private const string Act = "{ permissionMode: \"bypassPermissions\" }";
    private const string Wait = "{ permissionMode: \"bypassPermissions\", waitFor: \"children\", maxIterations: 20 }";

    // A finished instance a1: child "a" started in the background, the root waited for its completion.
    private const string FinishedA = "ctx.spawnBackground(\"a\", \"child-a\"); await ctx.llm.act(\"w\", " + Wait + ");";

    private static string Script(string rootBody, string crewOptions = "", string extra = "", string agents = ".withAgent(kid(\"a\"))")
        => "const kid = (n) => agentBuilder().name(n).role(\"R\").goal(\"G\")"
            + ".body(async (input, ctx) => (await ctx.llm.act(String(input), " + Act + ")).output).build();\n"
            + extra + "\n"
            + "const root = agentBuilder().name(\"root\").role(\"R\").goal(\"G\").body(async (input, ctx) => {" + rootBody + "}).build();\n"
            + "const c = crewBuilder().name(\"c\")" + crewOptions + ".withAgent(root)" + agents + ".build();\nc;";

    private static async Task<(JsCrew Crew, Task<string> Run)> StartAsync(ILlmProvider provider, string js)
    {
        var engine = new JsEngineFactory(llmProvider: provider).Create();
        JsCrew? crew = null;
        engine.SetValue("gateCount", new Func<int>(() => crew!.ConcurrencyGate.CurrentCount));
        crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        return (crew, OutputAsync(crew));
    }

    private static async Task<string> OutputAsync(JsCrew crew)
    {
        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);
        return result.tasks[0].output!.ToString()!;
    }

    private static async Task<string> RunAsync(ILlmProvider provider, string js)
    {
        var (_, run) = await StartAsync(provider, js);
        return await run.WaitAsync(Safety, TestContext.Current.CancellationToken);
    }

    private static async Task SignalsAsync(Task run, params Task[] signals)
    {
        var all = Task.WhenAll(signals).WaitAsync(Safety);
        if (await Task.WhenAny(all, run) == run) await run;
        await all;
    }

    private static List<string> Dialogue(LlmMessage[] chat)
        => chat.Where(m => m.Role != "system").Select(m => $"{m.Role}:{m.Content}").ToList();

    private static LlmMessage[] ChatEndingWith(SlowLlmProvider p, string lastUser)
        => p.Chats.Single(c => c[^1].Role == "user" && c[^1].Content == lastUser);

    private static bool Sent(SlowLlmProvider p, string lastUser)
        => p.Chats.Any(c => c[^1].Role == "user" && c[^1].Content == lastUser);

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldResumeWithPriorContext_WhenSendCalledOnFinishedInstance()
    {
        var provider = new SlowLlmProvider();

        var output = await RunAsync(provider, Script(FinishedA + "return await c.send(\"a1\", \"more\");"));

        Assert.Equal("R:more", output);
        var first = provider.Chats.First(c => c[^1].Role == "user" && c[^1].Content == "child-a");
        var resumed = ChatEndingWith(provider, "more");
        Assert.Equal(["user:child-a", "assistant:R:child-a", "user:more"], Dialogue(resumed));
        Assert.Equal(first.Where(m => m.Role == "system").Select(m => m.Content), resumed.Where(m => m.Role == "system").Select(m => m.Content));
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldAppendResultToTranscript_WhenFollowUpCompletes()
    {
        var provider = new SlowLlmProvider();

        var output = await RunAsync(provider, Script(FinishedA
            + "const r1 = await c.send(\"a1\", \"more\"); const r2 = await c.send(\"a1\", \"more2\"); return JSON.stringify([r1, r2]);"));

        Assert.Equal("[\"R:more\",\"R:more2\"]", output);
        Assert.Equal(
            ["user:child-a", "assistant:R:child-a", "user:more", "assistant:R:more", "user:more2"],
            Dialogue(ChatEndingWith(provider, "more2")));
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldRefuseSend_WhenInstanceIdUnknown()
    {
        var output = await RunAsync(new SlowLlmProvider(), Script(
            "try { await c.send(\"a99\", \"x\"); return \"sent\"; } catch (e) { return String(e.message ?? e); }"));

        Assert.Contains("a99", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldListInstanceIdsAndStates_WhenAgentsSpawned()
    {
        // spawn_agent calls: one instance, the child sees its own id.
        var fake = new FakeToolCallingLlmProvider(
            FakeToolCallingLlmProvider.ScriptStep.Call("spawn_agent", "{\"agent\":\"b\",\"input\":\"work\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("B-out"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("ROOT"));
        var spawned = await RunAsync(fake, Script(
            "await ctx.llm.act(\"start\", " + Act + "); return JSON.stringify({ inst: c.instances(), seen, bId: bAgent.id });",
            ".budget({ delegationDepth: 3 })",
            "const seen = []; const bAgent = agentBuilder().name(\"b\").role(\"R\").goal(\"G\").body(async (input, ctx) => {"
                + " seen.push(ctx.instanceId); return (await ctx.llm.act(String(input), " + Act + ")).output; }).build();",
            ".withAgent(bAgent)"));

        using var doc = JsonDocument.Parse(spawned);
        var inst = Assert.Single(doc.RootElement.GetProperty("inst").EnumerateArray());
        Assert.Matches("^a[0-9]+$", inst.GetProperty("id").GetString());
        Assert.Equal(doc.RootElement.GetProperty("bId").GetString(), inst.GetProperty("agent").GetString());
        Assert.Equal("completed", inst.GetProperty("state").GetString());
        Assert.Equal([inst.GetProperty("id").GetString()], doc.RootElement.GetProperty("seen").EnumerateArray().Select(e => e.GetString()));

        // background tickets: one completed, one failed.
        var slow = new SlowLlmProvider();
        slow.FailOn("child-b");
        var listed = await RunAsync(slow, Script(
            "ctx.spawnBackground(\"a\", \"child-a\"); ctx.spawnBackground(\"b\", \"child-b\");"
                + " await ctx.llm.act(\"w\", " + Wait + "); return JSON.stringify({ inst: c.instances(), aId: ka.id, bId: kb.id });",
            "",
            "const ka = kid(\"a\"); const kb = kid(\"b\");",
            ".withAgent(ka).withAgent(kb)"));

        using var doc2 = JsonDocument.Parse(listed);
        var rows = doc2.RootElement.GetProperty("inst").EnumerateArray()
            .Select(e => (e.GetProperty("id").GetString(), e.GetProperty("agent").GetString(), e.GetProperty("state").GetString())).ToList();
        Assert.Equal(
            [("a1", doc2.RootElement.GetProperty("aId").GetString(), "completed"), ("a2", doc2.RootElement.GetProperty("bId").GetString(), "failed")],
            rows);
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldCancelAndFreeSlot_WhenStopCalledOnRunningInstance()
    {
        var provider = new SlowLlmProvider();
        provider.Hold("child-a");
        provider.Hold("p1");
        var (crew, run) = await StartAsync(provider, Script(
            "ctx.spawnBackground(\"a\", \"child-a\"); await ctx.llm.act(\"p1\", " + Act + ");"
            + " const running = c.instances().map(i => i.state); const before = gateCount();"
            + " const stopped = c.stop(\"a1\"); const after = gateCount(); const state = c.instances().map(i => i.state);"
            + " await ctx.llm.act(\"w2\", " + Wait + ");"
            + " return JSON.stringify({ running, before, stopped, after, state, again: c.stop(\"a1\"), unknown: c.stop(\"zz\"), final: c.instances().map(i => i.state) });"));
        await SignalsAsync(run, provider.Entered("child-a"), provider.Entered("p1"));
        provider.Release("p1");

        using var doc = JsonDocument.Parse(await run.WaitAsync(Safety, TestContext.Current.CancellationToken));
        var r = doc.RootElement;
        Assert.Equal(["running"], r.GetProperty("running").EnumerateArray().Select(e => e.GetString()));
        Assert.True(r.GetProperty("stopped").GetBoolean());
        Assert.True(r.GetProperty("before").GetInt32() < 4);
        Assert.Equal(r.GetProperty("before").GetInt32(), r.GetProperty("after").GetInt32());
        Assert.Equal(["stopped"], r.GetProperty("state").EnumerateArray().Select(e => e.GetString()));
        Assert.False(r.GetProperty("again").GetBoolean());
        Assert.False(r.GetProperty("unknown").GetBoolean());
        Assert.Equal(["stopped"], r.GetProperty("final").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(4, crew.ConcurrencyGate.CurrentCount);
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldEvictOldestTerminal_WhenMaxInstancesExceeded()
    {
        var provider = new SlowLlmProvider();
        foreach (var k in new[] { "child-x", "child-y", "hold-root" }) provider.Hold(k);
        var (_, run) = await StartAsync(provider, Script(
            "ctx.spawnBackground(\"a\", \"child-a\"); await ctx.llm.act(\"w\", " + Wait + ");"
            + " ctx.spawnBackground(\"x\", \"child-x\"); const afterX = c.instances().map(i => i.id);"
            + " ctx.spawnBackground(\"y\", \"child-y\"); const afterY = c.instances().map(i => i.id);"
            + " let evicted = \"sent\"; try { await c.send(\"a1\", \"m\"); } catch (e) { evicted = String(e.message ?? e); }"
            + " await ctx.llm.act(\"hold-root\", " + Act + ");"
            + " return JSON.stringify({ afterX, afterY, evicted });",
            ".maxInstances(2)",
            "",
            ".withAgent(kid(\"a\")).withAgent(kid(\"x\")).withAgent(kid(\"y\"))"));
        await SignalsAsync(run, provider.Entered("child-x"), provider.Entered("child-y"), provider.Entered("hold-root"));
        foreach (var k in new[] { "child-x", "child-y", "hold-root" }) provider.Release(k);

        using var doc = JsonDocument.Parse(await run.WaitAsync(Safety, TestContext.Current.CancellationToken));
        var r = doc.RootElement;
        Assert.Equal(["a1", "a2"], r.GetProperty("afterX").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["a2", "a3"], r.GetProperty("afterY").EnumerateArray().Select(e => e.GetString()));
        Assert.NotEqual("sent", r.GetProperty("evicted").GetString());
        Assert.Contains("a1", r.GetProperty("evicted").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldRefuseSecondSend_WhenFirstSendStillRunning()
    {
        var provider = new SlowLlmProvider();
        provider.Hold("child-a");
        provider.Hold("child-x");
        provider.Hold("child-y");
        var (_, run) = await StartAsync(provider, Script(
            "ctx.spawnBackground(\"a\", \"child-a\");"
            + " let onRunning = \"sent\"; try { await c.send(\"a1\", \"early\"); } catch (e) { onRunning = String(e.message ?? e); }"
            + " await ctx.llm.act(\"w\", " + Wait + ");"
            + " ctx.spawnBackground(\"x\", \"child-x\"); ctx.spawnBackground(\"y\", \"child-y\");"
            + " const first = c.send(\"a1\", \"one\");"
            + " let second = \"sent\"; try { await c.send(\"a1\", \"two\"); } catch (e) { second = String(e.message ?? e); }"
            + " return JSON.stringify({ onRunning, second, first: await first });",
            ".concurrency(2)",
            "",
            ".withAgent(kid(\"a\")).withAgent(kid(\"x\")).withAgent(kid(\"y\"))"));
        await SignalsAsync(run, provider.Entered("child-a"));
        provider.Release("child-a");
        await SignalsAsync(run, Task.WhenAny(provider.Entered("child-x"), provider.Entered("child-y")));
        provider.Release("child-x");
        provider.Release("child-y");

        using var doc = JsonDocument.Parse(await run.WaitAsync(Safety, TestContext.Current.CancellationToken));
        var r = doc.RootElement;
        Assert.NotEqual("sent", r.GetProperty("onRunning").GetString());
        Assert.NotEqual("sent", r.GetProperty("second").GetString());
        Assert.Equal("R:one", r.GetProperty("first").GetString());
        Assert.False(Sent(provider, "early"));
        Assert.False(Sent(provider, "two"));
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldTakeGatePermitAndRecordSpawn_WhenSendRuns()
    {
        // The crew gate: with the gate full, the send waits until a slot frees.
        var gated = new SlowLlmProvider();
        gated.Hold("child-x");
        gated.Hold("child-y");
        var (_, gateRun) = await StartAsync(gated, Script(
            FinishedA + "ctx.spawnBackground(\"x\", \"child-x\"); ctx.spawnBackground(\"y\", \"child-y\"); return await c.send(\"a1\", \"more\");",
            ".concurrency(2)",
            "",
            ".withAgent(kid(\"a\")).withAgent(kid(\"x\")).withAgent(kid(\"y\"))"));
        await SignalsAsync(gateRun, Task.WhenAny(gated.Entered("child-x"), gated.Entered("child-y")));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(Sent(gated, "more"));
        gated.Release("child-x");
        gated.Release("child-y");
        Assert.Equal("R:more", await gateRun.WaitAsync(Safety, TestContext.Current.CancellationToken));

        // The per-agent semaphore: held by the host, the send waits.
        var serial = new SlowLlmProvider();
        serial.Hold("p2");
        var (crew, serialRun) = await StartAsync(serial, Script(FinishedA + "await ctx.llm.act(\"p2\", " + Act + "); return await c.send(\"a1\", \"more\");"));
        await SignalsAsync(serialRun, serial.Entered("p2"));
        var semaphore = crew.GetInstanceSemaphore(crew.Resolve("a"));
        await semaphore.WaitAsync(TestContext.Current.CancellationToken);
        serial.Release("p2");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(Sent(serial, "more"));
        semaphore.Release();
        Assert.Equal("R:more", await serialRun.WaitAsync(Safety, TestContext.Current.CancellationToken));

        // The spawn budget (MaxSpawnedAgents, 3 by default): the spawn of a1 and two sends fit, the next send is refused.
        using var doc = JsonDocument.Parse(await RunAsync(new SlowLlmProvider(), Script(
            FinishedA + "let ok = 0; let refused = null;"
            + " for (let i = 0; i < 10; i++) { try { await c.send(\"a1\", \"m\" + i); ok++; } catch (e) { refused = String(e.message ?? e); break; } }"
            + " return JSON.stringify({ ok, refused });")));
        Assert.Equal(2, doc.RootElement.GetProperty("ok").GetInt32());
        Assert.Contains("spawn", doc.RootElement.GetProperty("refused").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldKeepPreviousTranscript_WhenFollowUpFailsOrIsCancelled()
    {
        // Cancelled follow-ups are not driven here: a failed one is, through a scripted provider failure.
        var provider = new FakeToolCallingLlmProvider(
            FakeToolCallingLlmProvider.ScriptStep.Call("spawn_agent", "{\"agent\":\"a\",\"input\":\"go\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("A1"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("root done"),
            FakeToolCallingLlmProvider.ScriptStep.Fail(),
            FakeToolCallingLlmProvider.ScriptStep.Answer("A2"));

        var output = await RunAsync(provider, Script(
            "await ctx.llm.act(\"start\", " + Act + "); const id = c.instances()[0].id;"
            + " let err = null; try { await c.send(id, \"bad\"); } catch (e) { err = String(e.message ?? e); }"
            + " const good = await c.send(id, \"good\"); return JSON.stringify({ err, good });",
            ".budget({ delegationDepth: 3 })"));

        using var doc = JsonDocument.Parse(output);
        Assert.NotNull(doc.RootElement.GetProperty("err").GetString());
        Assert.Equal("A2", doc.RootElement.GetProperty("good").GetString());
        Assert.Equal(["user:go", "assistant:A1", "user:good"], Dialogue(provider.Turns[4]));
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldStoreTrimmedTranscript_WhenActEndsInterrupted()
    {
        var provider = new FakeToolCallingLlmProvider(
            FakeToolCallingLlmProvider.ScriptStep.Call("spawn_agent", "{\"agent\":\"a\",\"input\":\"go\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Call("spawn_agent", "{\"agent\":\"a\",\"input\":\"x\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("root done"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("after"));

        var output = await RunAsync(provider, Script(
            "await ctx.llm.act(\"start\", " + Act + "); return await c.send(c.instances()[0].id, \"more\");",
            ".budget({ delegationDepth: 3 })",
            "const ka = agentBuilder().name(\"a\").role(\"R\").goal(\"G\").body(async (input, ctx) => (await ctx.llm.act(String(input),"
                + " { permissionMode: \"bypassPermissions\", onToolCall: () => { ctx.llm.interrupt(); } })).output).build();",
            ".withAgent(ka)"));

        Assert.Equal("after", output);
        Assert.True(provider.Turns.Count >= 4); // a further turn is crew member a running its own body after the root script
        var resumed = provider.Turns[3];
        Assert.Equal(["user:go", "user:more"], Dialogue(resumed));
        Assert.DoesNotContain(resumed, m => m.RawToolCalls is not null || m.Role == "tool");
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public async Task ShouldKeepTranscript_WhenActEndsExhaustedAtMaxIterations()
    {
        var provider = new FakeToolCallingLlmProvider(
            FakeToolCallingLlmProvider.ScriptStep.Call("spawn_agent", "{\"agent\":\"a\",\"input\":\"go\"}"),
            FakeToolCallingLlmProvider.ScriptStep.Call("no_such_tool", "{}"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("root done"),
            FakeToolCallingLlmProvider.ScriptStep.Answer("after"));

        var output = await RunAsync(provider, Script(
            "await ctx.llm.act(\"start\", " + Act + "); const state = c.instances()[0].state;"
            + " return JSON.stringify({ state, reply: await c.send(c.instances()[0].id, \"more\") });",
            ".budget({ delegationDepth: 3 })",
            "const ka = agentBuilder().name(\"a\").role(\"R\").goal(\"G\").body(async (input, ctx) => (await ctx.llm.act(String(input),"
                + " { permissionMode: \"bypassPermissions\", maxIterations: 1 })).output).build();",
            ".withAgent(ka)"));

        using var doc = JsonDocument.Parse(output);
        Assert.Equal("completed", doc.RootElement.GetProperty("state").GetString());
        Assert.Equal("after", doc.RootElement.GetProperty("reply").GetString());
        var seeded = Dialogue(provider.Turns[3]);
        Assert.Equal("user:go", seeded[0]);
        Assert.Equal("user:more", seeded[^1]);
        Assert.True(seeded.Count > 2, "the exhausted act's turns are part of the seed");
    }

    [Fact]
    [Trait("BR", "RT-03")]
    public void ShouldBoundTranscriptByMessagesAndCharacters_WhenLong()
    {
        // Caps: AgentInstanceRegistry.MaxTranscriptMessages and MaxTranscriptCharacters.
        var many = Enumerable.Range(0, AgentInstanceRegistry.MaxTranscriptMessages + 10)
            .Select(i => new LlmMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = "m" + i }).ToList();
        var byCount = AgentInstanceRegistry.BoundTranscript(many);
        Assert.Equal(AgentInstanceRegistry.MaxTranscriptMessages, byCount.Count);
        Assert.Equal(many.Skip(10).Select(m => m.Content), byCount.Select(m => m.Content));

        var chunk = AgentInstanceRegistry.MaxTranscriptCharacters / 4 + 1;
        var big = Enumerable.Range(0, 6)
            .Select(i => new LlmMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = i + new string('x', chunk) }).ToList();
        var byChars = AgentInstanceRegistry.BoundTranscript(big);
        Assert.InRange(byChars.Sum(m => m.Content.Length), 1, AgentInstanceRegistry.MaxTranscriptCharacters);
        Assert.Equal(big[^1].Content, byChars[^1].Content);
        Assert.Equal(big.Skip(big.Count - byChars.Count).Select(m => m.Content), byChars.Select(m => m.Content));
        Assert.True(byChars.Count < big.Count);
    }

    private static async Task<string> RunWithOptionsAsync(ILlmProvider provider, ScriptAgentInstanceOptions options, string js)
    {
        var ports = new ScriptingHostPorts { AgentInstances = options };
        var engine = new JsEngineFactory(llmProvider: provider, hostPorts: ports).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(js, null, TestContext.Current.CancellationToken)).ToObject()!;
        return await OutputAsync(crew).WaitAsync(Safety, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "R6")]
    public async Task ShouldTrimTranscript_WhenConfiguredMessageCapIsLower()
    {
        var provider = new SlowLlmProvider();

        var output = await RunWithOptionsAsync(provider, new ScriptAgentInstanceOptions { MaxTranscriptMessages = 2 },
            Script(FinishedA + "await c.send(\"a1\", \"more\"); return await c.send(\"a1\", \"more2\");"));

        Assert.Equal("R:more2", output);
        Assert.Equal(["user:more", "assistant:R:more", "user:more2"], Dialogue(ChatEndingWith(provider, "more2")));
    }

    [Fact]
    [Trait("BR", "R6")]
    public async Task ShouldTrimTranscript_WhenConfiguredCharacterCapIsLower()
    {
        var provider = new SlowLlmProvider();

        var output = await RunWithOptionsAsync(provider, new ScriptAgentInstanceOptions { MaxTranscriptCharacters = 10 },
            Script(FinishedA + "return await c.send(\"a1\", \"more\");"));

        Assert.Equal("R:more", output);
        Assert.Equal(["assistant:R:child-a", "user:more"], Dialogue(ChatEndingWith(provider, "more")));
    }

    [Fact]
    [Trait("BR", "R6")]
    public void ShouldRefuseOptions_WhenATranscriptCapIsNotPositive()
    {
        // No Validate exists on ScriptingLimitsOptions to mimic: ArgumentOutOfRangeException naming the property.
        var messages = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ScriptAgentInstanceOptions { MaxTranscriptMessages = 0 }.Validate());
        Assert.Equal(nameof(ScriptAgentInstanceOptions.MaxTranscriptMessages), messages.ParamName);

        var characters = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ScriptAgentInstanceOptions { MaxTranscriptCharacters = -1 }.Validate());
        Assert.Equal(nameof(ScriptAgentInstanceOptions.MaxTranscriptCharacters), characters.ParamName);

        new ScriptAgentInstanceOptions().Validate();
    }
}
