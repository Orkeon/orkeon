using Jint;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// Budget enforcement end-to-end (the test twin of an internal acceptance probe): a crew declaring
/// <c>.budget({ toolCalls: 1 })</c> whose agent body drives a divergent
/// <c>ctx.llm.act</c> loop must surface a typed <see cref="BudgetExhaustedException"/>
/// from <c>crew.RunAsync</c> — not a generic promise rejection, and not a normal
/// completion after N tool calls (the pre-F1 failure mode).
/// </summary>
public sealed class CrewBudgetRunTests
{
    private static string ToolCallBody(string name, string argsJson)
    {
        var argsEncoded = System.Text.Json.JsonSerializer.Serialize(argsJson);
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" +
               "{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + argsEncoded + "}}]}}]}";
    }

    /// <summary>A turn asking for <c>echo_tool</c> twice: two tool calls recorded within one turn.</summary>
    private static string TwoToolCallsBody()
    {
        var args = System.Text.Json.JsonSerializer.Serialize("{\"text\":\"again\"}");
        string Call(string id) => "{\"id\":\"" + id + "\",\"type\":\"function\",\"function\":{\"name\":\"echo_tool\",\"arguments\":" + args + "}}";
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[" + Call("c1") + "," + Call("c2") + "]}}]}";
    }

    private static JsCrew BuildCrew(Jint.Engine engine, string script)
        => (JsCrew)engine.Evaluate(script).ToObject()!;

    [Fact]
    public async Task Crew_run_with_toolCalls_budget_1_surfaces_typed_BudgetExhaustedException()
    {
        var tool = new CountingTool("echo_tool");
        var provider = new LoopingProvider(ToolCallBody("echo_tool", "{\"text\":\"again\"}"));
        var factory = new Orkeon.Scripting.JsEngineFactory(
            builtInTools: new IBaseTool[] { tool },
            llmProvider: provider);
        using var engine = factory.Create();

        var crew = BuildCrew(engine, """
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .tools(["echo_tool"])
              .body(async (_input, ctx) => {
                const r = await ctx.llm.act("loop forever", { maxIterations: 40 });
                return String(r?.output ?? r);
              })
              .build();
            crewBuilder().name("c").budget({ toolCalls: 1 }).withAgent(a).build();
            """);

        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(
            () => crew.RunAsync(null, CancellationToken.None));

        Assert.Equal(BudgetDimension.ToolCalls, ex.Dimension);
        Assert.Equal(1, tool.CallCount);
    }

    [Fact]
    public async Task Crew_run_without_budget_keeps_legacy_behaviour()
    {
        var tool = new CountingTool("echo_tool");
        var provider = new LoopingProvider(ToolCallBody("echo_tool", "{\"text\":\"again\"}"));
        var factory = new Orkeon.Scripting.JsEngineFactory(
            builtInTools: new IBaseTool[] { tool },
            llmProvider: provider);
        using var engine = factory.Create();

        var crew = BuildCrew(engine, """
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .tools(["echo_tool"])
              .body(async (_input, ctx) => {
                const r = await ctx.llm.act("loop forever", { maxIterations: 3 });
                return String(r?.output ?? r);
              })
              .build();
            crewBuilder().name("c").withAgent(a).build();
            """);

        // No budget → the only bound is maxIterations; the run terminates normally.
        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(3, tool.CallCount);
        Assert.Contains("max tool-call iterations", result.output, StringComparison.Ordinal);
    }

    // ── a delegated run's budget (#189) ─────────────────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// <c>B</c> loops on <c>echo_tool</c> (at most 40 turns); <c>A</c> runs <c>CALL</c> and answers with
    /// what it settled to, <c>budget: &lt;message&gt;</c> for a <c>BudgetExhaustedError</c>. A body run by the
    /// crew's own walk (no input) returns at once, so <c>crew.run</c> only drives <c>A</c>.
    /// </summary>
    private const string DelegatingCrew = """
        const looping = (n) => agentBuilder().name(n).role("R").goal("G").tools(["echo_tool"])
          .body(async (input, ctx) => {
            if (input === undefined) return null;
            const r = await ctx.llm.act("loop forever", { maxIterations: 40 });
            return String(r?.output ?? r);
          }).build();
        const describe = (e) => (e instanceof BudgetExhaustedError ? "budget: " : "other: ") + e.message;
        const a = agentBuilder().name("A").role("R").goal("G")
          .body(async (input, ctx) => {
            try { return "completed: " + await CALL; } catch (e) { return describe(e); }
          }).build();
        globalThis.crew = crewBuilder().name("c")BUDGET.withAgent(a).withAgent(looping("B")).withAgent(looping("C")).build();
        """;

    private static (Jint.Engine Engine, CountingTool Tool, LoopingProvider Provider) NewDelegatingCrew(
        string call, string crewBudget = "", int tokensPerTurn = 0, string? providerBody = null)
    {
        var tool = new CountingTool("echo_tool");
        var provider = new LoopingProvider(providerBody ?? ToolCallBody("echo_tool", "{\"text\":\"again\"}"), tokensPerTurn);
        var engine = new Orkeon.Scripting.JsEngineFactory(builtInTools: new IBaseTool[] { tool }, llmProvider: provider).Create();
        engine.Execute(DelegatingCrew.Replace("CALL", call, StringComparison.Ordinal).Replace("BUDGET", crewBudget, StringComparison.Ordinal));
        return (engine, tool, provider);
    }

    /// <summary>What the crews of the tests below share: <c>describe</c> and the <c>looping</c> agent of <see cref="DelegatingCrew"/>.</summary>
    private const string Prelude = """
        const looping = (n) => agentBuilder().name(n).role("R").goal("G").tools(["echo_tool"])
          .body(async (input, ctx) => {
            if (input === undefined) return null;
            const r = await ctx.llm.act("loop forever", { maxIterations: 40 });
            return String(r?.output ?? r);
          }).build();
        const describe = (e) => (e instanceof BudgetExhaustedError ? "budget: " : "other: ") + e.message;

        """;

    private static async Task<(Jint.Engine Engine, CountingTool Tool)> NewCrewAsync(string script)
    {
        var tool = new CountingTool("echo_tool");
        var provider = new LoopingProvider(ToolCallBody("echo_tool", "{\"text\":\"again\"}"));
        var engine = new Orkeon.Scripting.JsEngineFactory(builtInTools: new IBaseTool[] { tool }, llmProvider: provider).Create();
        await engine.ExecuteAsync(Prelude + script, null, Ct);
        return (engine, tool);
    }

    private static async Task<string> RunCrewAsync(Jint.Engine engine)
    {
        var crew = (JsCrew)(await engine.EvaluateAsync("crew", null, Ct)).ToObject()!;
        return (await crew.RunAsync(null, Ct)).output;
    }

    private static async Task<string> RunAAsync(Jint.Engine engine)
        => (await engine.EvaluateAsync("(async () => await crew.runAgent(\"A\", \"go\"))()", null, Ct)).AsString();

    [Fact]
    public async Task ShouldEndTheDelegatedRunOnTheFourthToolCall_WhenDelegateGivesToolCallsThree()
    {
        // Arrange
        var (engine, tool, _) = NewDelegatingCrew("ctx.delegate(\"B\", \"go\", { budget: { toolCalls: 3 } })");
        using var owned = engine;

        // Act
        var outcome = await RunAAsync(engine);

        // Assert
        Assert.Equal("budget: Tool-call budget exhausted (3/3).", outcome);
        Assert.Equal(3, tool.CallCount);
    }

    [Fact]
    public async Task ShouldEndTheRunOnTheFourthToolCall_WhenRunAgentGivesToolCallsThree()
    {
        // Arrange
        var (engine, tool, _) = NewDelegatingCrew("crew.runAgent(\"B\", \"go\", { budget: { toolCalls: 3 } })");
        using var owned = engine;

        // Act
        var outcome = await RunAAsync(engine);

        // Assert
        Assert.Equal("budget: Tool-call budget exhausted (3/3).", outcome);
        Assert.Equal(3, tool.CallCount);
    }

    [Fact]
    public async Task ShouldStopBeforeTheNextTurn_WhenTheDelegatedTokenBudgetIsSpent()
    {
        // Arrange
        var (engine, _, provider) = NewDelegatingCrew("ctx.delegate(\"B\", \"go\", { budget: { tokens: 50 } })", tokensPerTurn: 25);
        using var owned = engine;

        // Act
        var outcome = await RunAAsync(engine);

        // Assert
        // The third call is the wrap-up with no tools (#218): the looping model answers nothing, so
        // the act still rejects with the budget's error.
        Assert.Equal("budget: Token budget exhausted: 50 tokens sent and received over 2 calls (limit 50)", outcome);
        Assert.Equal(3, provider.ChatCount);
    }

    [Fact]
    public async Task ShouldStopBothRunsAtTheCrewsTokens_WhenTwoDelegatedRunsChargeTheCrew()
    {
        // Arrange: each run allows itself 1000 tokens, the crew 100 for the two of them
        var (engine, tool, provider) = NewDelegatingCrew(
            """
            Promise.allSettled([
              ctx.delegate("B", "go", { budget: { tokens: 1000 }, charge: "crew" }),
              ctx.delegate("C", "go", { budget: { tokens: 1000 }, charge: "crew" }),
            ]).then(rs => rs.map(r => r.status === "rejected" ? describe(r.reason) : r.value).join(" | "))
            """,
            crewBudget: ".budget({ tokens: 100 })",
            tokensPerTurn: 25);
        using var owned = engine;
        var crew = (JsCrew)(await engine.EvaluateAsync("crew", null, Ct)).ToObject()!;

        // Act
        var result = await crew.RunAsync(null, Ct);

        // Assert: the turn of each run in flight when the tally reached 100 may land, never a later
        // one, and no tool it asks for runs: the three turns before it ran theirs (fewer when two turns
        // land together past 75). Each run then makes its one wrap-up call (#218), which answers nothing here
        var parts = result.output.Split(" | ");
        Assert.Equal(2, parts.Length);
        Assert.StartsWith("completed: budget: Token budget exhausted: ", parts[0], StringComparison.Ordinal);
        Assert.StartsWith("budget: Token budget exhausted: ", parts[1], StringComparison.Ordinal);
        Assert.All(parts, p => Assert.EndsWith("(limit 100)", p, StringComparison.Ordinal));
        Assert.Equal(6, provider.ChatCount);
        Assert.InRange(tool.CallCount, 2, 3);
    }

    [Fact]
    public async Task ShouldSpendTheCrewsBudget_WhenChargeIsGivenWithoutBudget()
    {
        // Arrange
        var (engine, tool, _) = NewDelegatingCrew("ctx.delegate(\"B\", \"go\", { charge: \"crew\" })", crewBudget: ".budget({ toolCalls: 2 })");
        using var owned = engine;
        var crew = (JsCrew)(await engine.EvaluateAsync("crew", null, Ct)).ToObject()!;

        // Act
        var result = await crew.RunAsync(null, Ct);

        // Assert
        Assert.Equal("budget: Tool-call budget exhausted (2/2).", result.output);
        Assert.Equal(2, tool.CallCount);
    }

    [Fact]
    public async Task ShouldDeriveTheChildsBudget_WhenAChildIsSpawnedInsideABudgetedDelegatedRun()
    {
        // Arrange: B may delegate one level; its child C derives depth 0 and cannot spawn D. Under the
        // runtime default (depth 2) C could.
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();
        await engine.ExecuteAsync("""
            const b = agentBuilder().name("B").role("R").goal("G").body(async (input, ctx) => {
              ctx.spawnBackground("C", "go");
              return await ctx.receive({ timeout: 60000 });
            }).build();
            const c = agentBuilder().name("C").role("R").goal("G").body(async (input, ctx) => {
              try { ctx.spawnBackground("D", "go"); ctx.send("B", "spawned"); } catch (e) { ctx.send("B", e.message); }
              return "c";
            }).build();
            const d = agentBuilder().name("D").role("R").goal("G").body(async () => "d").build();
            const a = agentBuilder().name("A").role("R").goal("G")
              .body(async (input, ctx) => await ctx.delegate("B", "go", { budget: { delegationDepth: 1 } })).build();
            globalThis.crew = crewBuilder().name("c").withAgent(a).withAgent(b).withAgent(c).withAgent(d).build();
            """, null, Ct);

        // Act
        var outcome = await RunAAsync(engine);

        // Assert
        Assert.Equal("Delegation depth budget exhausted: cannot spawn 'D' (max 0).", outcome);
    }

    [Theory]
    [InlineData("{ budget: { toolCalls: 0 } }", "budget.toolCalls must be a whole number of 1 or more; got 0.")]
    [InlineData("{ budget: { tokens: 2.5 } }", "budget.tokens must be a whole number of 1 or more; got 2.5.")]
    [InlineData("{ budget: { tokens: \"many\" } }", "budget.tokens must be a whole number of 1 or more; got \"many\".")]
    [InlineData("{ budget: { seconds: 30 } }", "budget.seconds is not a budget dimension; expected one of toolCalls, tokens, wallTime, delegationDepth, spawnedAgents.")]
    [InlineData("{ budget: { wallTime: \"soon\" } }", "budget.wallTime must be a positive number of seconds or a duration such as \"90s\" or \"5m\"; got \"soon\".")]
    [InlineData("{ budget: { wallTime: -1 } }", "budget.wallTime must be a positive number of seconds or a duration such as \"90s\" or \"5m\"; got -1.")]
    [InlineData("{ budget: 5 }", "budget must be an object such as { toolCalls: 20, tokens: 50000, wallTime: 300 }.")]
    [InlineData("{ charge: \"team\" }", "charge must be \"crew\"; got team.")]
    [InlineData("{ charge: \"crew\" }", "charge: \"crew\" charges the budget of the run it is called from, and this call runs under none: give the crew a budget, crewBuilder().budget({...}).")]
    public async Task ShouldRefuseTheRun_WhenItsBudgetOptionsAreInvalid(string options, string expected)
    {
        // Arrange
        var (engine, tool, _) = NewDelegatingCrew($"ctx.delegate(\"B\", \"go\", {options})");
        using var owned = engine;

        // Act
        var outcome = await RunAAsync(engine);

        // Assert
        Assert.Equal("other: crew.runAgent / ctx.delegate options: " + expected, outcome);
        Assert.Equal(0, tool.CallCount);
    }

    [Fact]
    public async Task ShouldRunUnbounded_WhenDelegateGivesNoOptions()
    {
        // Arrange
        var (engine, tool, _) = NewDelegatingCrew("ctx.delegate(\"B\", \"go\")");
        using var owned = engine;

        // Act
        var outcome = await RunAAsync(engine);

        // Assert: only maxIterations bounds it, as before
        Assert.StartsWith("completed: ", outcome, StringComparison.Ordinal);
        Assert.Equal(40, tool.CallCount);
    }

    [Fact]
    public async Task ShouldNotChargeTheCrew_WhenTheRunsOwnLimitRefusesACall()
    {
        // Arrange: every turn asks for two calls; the crew allows 4, B 1, C charges the crew straight
        var (engine, tool, _) = NewDelegatingCrew(
            """
            ctx.delegate("B", "go", { budget: { toolCalls: 1 }, charge: "crew" }).catch(describe)
              .then(b => ctx.delegate("C", "go", { charge: "crew" }).catch(describe).then(c => b + " | " + c))
            """,
            crewBudget: ".budget({ toolCalls: 4 })",
            providerBody: TwoToolCallsBody());
        using var owned = engine;

        // Act
        var output = await RunCrewAsync(engine);

        // Assert: B's second call is refused by B and never reaches the crew, so C gets the three left
        Assert.Equal("completed: budget: Agent exceeded 1 tool calls. | budget: Agent exceeded 4 tool calls.", output);
        Assert.Equal(4, tool.CallCount);
    }

    [Fact]
    public async Task ShouldEndTheDelegatedRunOnItsWallTime_WhenItsClockRunsOut()
    {
        // Arrange: A runs under a host budget on a fake clock that each model turn moves by 30 s; B
        // allows itself one minute and charges A's budget, whose clock it reads
        var clock = new FakeTimeProvider();
        var (engine, _, provider) = NewDelegatingCrew("ctx.delegate(\"B\", \"go\", { budget: { wallTime: \"1m\" }, charge: \"crew\" })");
        using var owned = engine;
        provider.OnChat = () => clock.Advance(TimeSpan.FromSeconds(30));
        engine.SetValue("hostBudget", new AgentExecutionBudget
        {
            MaxWallTime = TimeSpan.FromHours(1),
            MaxToolCalls = 1_000,
            MaxTokensConsumed = 100_000,
            TimeProvider = clock,
        });

        // Act
        var outcome = (await engine.EvaluateAsync("(async () => await crew.runAgent(\"A\", \"go\", { budget: hostBudget }))()", null, Ct)).AsString();

        // Assert
        Assert.StartsWith("budget: Wall time exceeded", outcome, StringComparison.Ordinal);
        Assert.Equal(2, provider.ChatCount);
    }

    [Fact]
    public async Task ShouldRefuseTheSecondSpawn_WhenTheDelegatedRunGivesSpawnedAgentsOne()
    {
        // Arrange
        var (engine, _) = await NewCrewAsync("""
            const b = agentBuilder().name("B").role("R").goal("G").body(async (input, ctx) => {
              try { ctx.spawnBackground("D", "go"); ctx.spawnBackground("D", "go"); return "both"; } catch (e) { return describe(e); }
            }).build();
            const d = agentBuilder().name("D").role("R").goal("G").body(async () => "d").build();
            const a = agentBuilder().name("A").role("R").goal("G")
              .body(async (input, ctx) => await ctx.delegate("B", "go", { budget: { spawnedAgents: 1 } })).build();
            globalThis.crew = crewBuilder().name("c").withAgent(a).withAgent(b).withAgent(d).build();
            """);
        using var owned = engine;

        // Act
        var outcome = await RunAAsync(engine);

        // Assert
        Assert.Equal("budget: Agent exceeded 1 spawned sub-agents.", outcome);
    }

    [Fact]
    public async Task ShouldChargeTheCrew_WhenAChildIsSpawnedInsideAChargedRun()
    {
        // Arrange: B allows itself 100 calls and charges the crew's 3; its child C spends them, so D,
        // charging the crew after it, finds none left
        var (engine, tool) = await NewCrewAsync("""
            const b = agentBuilder().name("B").role("R").goal("G").body(async (input, ctx) => {
              if (input === undefined) return null;
              ctx.spawnBackground("C", "go");
              return await ctx.receive({ timeout: 60000 });
            }).build();
            const c = agentBuilder().name("C").role("R").goal("G").tools(["echo_tool"]).body(async (input, ctx) => {
              if (input === undefined) return null;
              try { await ctx.llm.act("loop forever", { maxIterations: 40 }); ctx.send("B", "completed"); } catch (e) { ctx.send("B", describe(e)); }
              return "c";
            }).build();
            const a = agentBuilder().name("A").role("R").goal("G").body(async (input, ctx) => {
              const child = await ctx.delegate("B", "go", { budget: { toolCalls: 100 }, charge: "crew" });
              let after;
              try { after = "completed: " + await ctx.delegate("D", "go", { charge: "crew" }); } catch (e) { after = describe(e); }
              return child + " | " + after;
            }).build();
            globalThis.crew = crewBuilder().name("c").budget({ toolCalls: 3 }).withAgent(a).withAgent(b).withAgent(c).withAgent(looping("D")).build();
            """);
        using var owned = engine;

        // Act
        var output = await RunCrewAsync(engine);

        // Assert
        Assert.Equal("budget: Tool-call budget exhausted (3/3). | budget: Tool-call budget exhausted (3/3).", output);
        Assert.Equal(3, tool.CallCount);
    }

    [Fact]
    public async Task ShouldChargeEveryLevel_WhenAChargedRunDelegatesAChargedRun()
    {
        // Arrange: run -> run -> crew. C allows itself 100 calls, B 2, the crew 3: B's limit ends C, and
        // the crew counted C's two calls, so D gets the one left
        var (engine, tool) = await NewCrewAsync("""
            const b = agentBuilder().name("B").role("R").goal("G").body(async (input, ctx) => {
              if (input === undefined) return null;
              try { return "completed: " + await ctx.delegate("C", "go", { budget: { toolCalls: 100 }, charge: "crew" }); } catch (e) { return describe(e); }
            }).build();
            const a = agentBuilder().name("A").role("R").goal("G").body(async (input, ctx) => {
              const nested = await ctx.delegate("B", "go", { budget: { toolCalls: 2 }, charge: "crew" });
              let after;
              try { after = "completed: " + await ctx.delegate("D", "go", { charge: "crew" }); } catch (e) { after = describe(e); }
              return nested + " | " + after;
            }).build();
            globalThis.crew = crewBuilder().name("c").budget({ toolCalls: 3 }).withAgent(a).withAgent(b).withAgent(looping("C")).withAgent(looping("D")).build();
            """);
        using var owned = engine;

        // Act
        var output = await RunCrewAsync(engine);

        // Assert
        Assert.Equal("budget: Tool-call budget exhausted (2/2). | budget: Tool-call budget exhausted (3/3).", output);
        Assert.Equal(3, tool.CallCount);
    }

    [Fact]
    public async Task ShouldChargeTheCrew_WhenRunAgentChargesFromTheOnlyBodyRunning()
    {
        // Arrange
        var (engine, tool, _) = NewDelegatingCrew("crew.runAgent(\"B\", \"go\", { charge: \"crew\" })", crewBudget: ".budget({ toolCalls: 2 })");
        using var owned = engine;

        // Act
        var output = await RunCrewAsync(engine);

        // Assert
        Assert.Equal("budget: Tool-call budget exhausted (2/2).", output);
        Assert.Equal(2, tool.CallCount);
    }

    [Fact]
    public async Task ShouldRefuseTheCharge_WhenRunAgentIsCalledWhileRunsUnderDifferentBudgetsAreOpen()
    {
        // Arrange: under Promise.all, B runs under its own budget beside A's body under the crew's when
        // crew.runAgent asks to charge: which one is meant cannot be told
        var (engine, tool) = await NewCrewAsync("""
            const b = agentBuilder().name("B").role("R").goal("G").body(async (input, ctx) => {
              if (input === undefined) return null;
              ctx.send("A", "started");
              return await ctx.receive({ timeout: 60000 });
            }).build();
            const a = agentBuilder().name("A").role("R").goal("G").body(async (input, ctx) => {
              const [held, charged] = await Promise.all([
                ctx.delegate("B", "go", { budget: { toolCalls: 5 } }),
                (async () => {
                  await ctx.receive({ timeout: 60000 });
                  try { return "completed: " + await crew.runAgent("C", "go", { charge: "crew" }); }
                  catch (e) { return describe(e); }
                  finally { ctx.send("B", "released"); }
                })(),
              ]);
              return held + " | " + charged;
            }).build();
            globalThis.crew = crewBuilder().name("c").budget({ toolCalls: 10 }).withAgent(a).withAgent(b).withAgent(looping("C")).build();
            """);
        using var owned = engine;

        // Act
        var output = await RunCrewAsync(engine);

        // Assert
        Assert.Equal(
            "released | other: crew.runAgent / ctx.delegate options: charge: \"crew\" from crew.runAgent cannot tell which budget to charge: "
            + "runs under different budgets are open. Call ctx.delegate(agent, input, { charge: \"crew\" }) from the agent's body, "
            + "which charges that body's budget.",
            output);
        Assert.Equal(0, tool.CallCount);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    /// <summary>Always answers with the same tool-call body: a divergent model.</summary>
    private sealed class LoopingProvider : ILlmProvider
    {
        private readonly string _body;
        private readonly int _tokensPerTurn;
        private int _chatCount;
        public LoopingProvider(string body, int tokensPerTurn = 0)
        {
            _body = body;
            _tokensPerTurn = tokensPerTurn;
        }
        public string Name => "fake";
        public LlmConfig? BaseConfig => LlmConfig.Create("fake-model");
        public int ChatCount => Volatile.Read(ref _chatCount);
        /// <summary>Runs on each chat turn, before it answers: a test moves its fake clock there.</summary>
        public Action? OnChat { get; set; }

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse { Content = "", RawResponseBody = _body });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _chatCount);
            OnChat?.Invoke();
            return Task.FromResult(new LlmResponse { Content = "", RawResponseBody = _body, TokensUsed = _tokensPerTurn });
        }
    }

    private sealed class CountingTool : IBaseTool
    {
        public CountingTool(string name) => Name = name;
        public string Name { get; }
        public string Description => "test tool";
        public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<ToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ToolCallResponse(true, new Dictionary<string, object?> { ["ok"] = true }, null));
        }

        public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }
}
