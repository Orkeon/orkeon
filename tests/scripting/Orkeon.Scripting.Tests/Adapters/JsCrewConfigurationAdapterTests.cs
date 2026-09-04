using Jint;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// Verifies that <see cref="JsCrewConfigurationAdapter"/> reproduces the topology
/// declared in a <c>.ork.ts</c>-style script: agents, tasks, dependencies,
/// assignments, LLM overrides, process type, and manager all survive the round-trip
/// to <c>CrewConfiguration</c> with stable shape that the YAML-side orchestrator can
/// consume.
/// </summary>
public sealed class JsCrewConfigurationAdapterTests
{
    private static readonly string[] ExpectedBuiltinTools = ["file_read", "count_pattern"];
    private static readonly string[] ExpectedSingleTool = ["web_scrape"];
    private static readonly string[] ExpectedTaskTools = ["json_tool", "script_tool"];

    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static JsCrew BuildCrew(Engine engine, string script)
        => (JsCrew)engine.Evaluate(script).ToObject()!;

    [Fact]
    public void Adapt_simple_sequential_crew_preserves_agents_tasks_and_dag()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const planner = agentBuilder()
                .name("planner")
                .role("Planner")
                .goal("Draft a plan")
                .backstory("Strategic")
                .maxIterations(15)
                .verbose(true)
                .allowDelegation(false)
                .build();

            const writer = agentBuilder()
                .name("writer")
                .role("Writer")
                .goal("Write the doc")
                .build();

            const plan = taskBuilder()
                .name("plan")
                .agent(planner)
                .description("Plan the work")
                .expectedOutput("A plan")
                .build();

            const write = taskBuilder()
                .name("write")
                .agent(writer)
                .description("Write the doc")
                .expectedOutput("A doc")
                .withContext(plan)
                .build();

            crewBuilder()
                .name("two-step")
                .goal("Plan and write")
                .process("sequential")
                .verbose(true)
                .withAgent(planner)
                .withAgent(writer)
                .withTask(plan)
                .withTask(write)
                .build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        Assert.Equal("two-step", config.Name);
        Assert.Equal("Plan and write", config.Goal);
        Assert.Equal(ProcessType.Sequential, config.Process);
        Assert.True(config.Verbose);
        Assert.Null(config.ManagerAgentId);

        // Agents
        Assert.Equal(2, config.Agents.Count);
        var plannerCfg = config.Agents.Single(a => a.Role == "Planner");
        var writerCfg = config.Agents.Single(a => a.Role == "Writer");
        Assert.Equal("Draft a plan", plannerCfg.Goal);
        Assert.Equal("Strategic", plannerCfg.Backstory);
        Assert.Equal(15, plannerCfg.MaxIterations);
        Assert.True(plannerCfg.Verbose);
        Assert.False(plannerCfg.AllowDelegation);
        Assert.Equal("Write the doc", writerCfg.Goal);

        // Tasks
        Assert.Equal(2, config.Tasks.Count);
        var planCfg = config.Tasks.Single(t => t.Description == "Plan the work");
        var writeCfg = config.Tasks.Single(t => t.Description == "Write the doc");
        Assert.Equal(plannerCfg.Id, planCfg.AssignedAgentId);
        Assert.Equal(writerCfg.Id, writeCfg.AssignedAgentId);
        Assert.Equal("A plan", planCfg.ExpectedOutput);
        Assert.Equal("A doc", writeCfg.ExpectedOutput);

        // Dependency: `write` was wired with `.withContext(plan)`.
        Assert.Empty(planCfg.Dependencies);
        Assert.Single(writeCfg.Dependencies);
        Assert.Equal(planCfg.Id, writeCfg.Dependencies[0]);
    }

    [Fact]
    public void Adapt_hierarchical_crew_surfaces_manager_id()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const mgr = agentBuilder().name("mgr").role("Manager").goal("Coordinate").build();
            const w = agentBuilder().name("w").role("Worker").goal("Execute").build();
            const t = taskBuilder().name("t").agent(w).description("do it").expectedOutput("done").build();
            crewBuilder()
                .name("h")
                .process("hierarchical")
                .manager(mgr)
                .withAgent(w)
                .withTask(t)
                .build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        Assert.Equal(ProcessType.Hierarchical, config.Process);
        Assert.NotNull(config.ManagerAgentId);
        var managerCfg = config.Agents.Single(a => a.Role == "Manager");
        Assert.Equal(managerCfg.Id, config.ManagerAgentId);
    }

    [Fact]
    public void Adapt_carries_llm_overrides_from_with_into_LlmConfig()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const tunedLlm = llm.openai({ model: "gpt-4o" }).with({
                temperature: 0.3,
                maxTokens: 5000,
            });
            const a = agentBuilder()
                .name("a")
                .role("R")
                .goal("G")
                .llm(tunedLlm)
                .build();
            const t = taskBuilder().name("t").agent(a).description("d").expectedOutput("o").build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var agent = Assert.Single(config.Agents);
        Assert.NotNull(agent.LlmConfig);
        Assert.Equal("gpt-4o", agent.LlmConfig!.Model);
        Assert.Equal(0.3, agent.LlmConfig.Temperature);
        Assert.Equal(5000, agent.LlmConfig.MaxTokens);
    }

    [Fact]
    public void Adapt_synthesizes_goal_when_script_did_not_call_goal()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").build();
            const t = taskBuilder().name("t").agent(a).description("d").expectedOutput("o").build();
            crewBuilder().name("synth").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        // Validator rejects empty Goal; the adapter falls back to a name-derived synthesis
        // rather than forcing every script to declare one.
        Assert.False(string.IsNullOrWhiteSpace(config.Goal));
        Assert.Contains("synth", config.Goal);
    }

    [Fact]
    public void Adapt_expect_schema_is_captured_into_task_Context()
    {
        // The DSL's .expect({...}) supplies a JSON schema for downstream validation,
        // but TaskDeliverable.Validate() rejects a Path-less deliverable. Until the
        // DSL gains a proper .deliverable(...) builder we surface the schema as a
        // serialized string under TaskConfiguration.Context so it stays observable
        // (validator + tooling can read it) without tripping the deliverable contract.
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").build();
            const t = taskBuilder()
                .name("t")
                .agent(a)
                .description("d")
                .expectedOutput("o")
                .expect({
                    type: "object",
                    required: ["status"],
                    properties: { status: { type: "string" } },
                })
                .build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var taskCfg = Assert.Single(config.Tasks);
        // No deliverable emitted (no Path → YAML-loader-equivalent behavior).
        Assert.Null(taskCfg.Deliverable);
        Assert.True(taskCfg.Context.TryGetValue("expect_schema", out var schemaRaw));
        var schema = Assert.IsType<string>(schemaRaw);
        Assert.Contains("status", schema);
    }

    [Fact]
    public void Adapt_tools_method_collects_builtin_names_into_AgentConfiguration_Tools()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder()
                .name("a")
                .role("R")
                .goal("G")
                .tools(["file_read", "count_pattern"])
                .build();
            const t = taskBuilder().name("t").agent(a).description("d").expectedOutput("o").build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        var agent = Assert.Single(config.Agents);
        Assert.Equal(ExpectedBuiltinTools, agent.Tools);
    }

    [Fact]
    public void Adapt_tools_single_string_overload_works()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").tools("web_scrape").build();
            const t = taskBuilder().name("t").agent(a).description("d").expectedOutput("o").build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);
        Assert.Equal(ExpectedSingleTool, config.Agents[0].Tools);
    }

    [Fact]
    public void Adapt_deliverable_block_produces_TaskDeliverable_with_inline_schema()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").build();
            const t = taskBuilder()
                .name("t")
                .agent(a)
                .description("d")
                .expectedOutput("o")
                .deliverable({
                    path: "/output/report.json",
                    source: "structured_output",
                    format: "json",
                    sanitize: false,
                    schema: {
                        type: "object",
                        required: ["status"],
                        properties: { status: { type: "string" } },
                    },
                })
                .build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);
        var task = Assert.Single(config.Tasks);

        Assert.NotNull(task.Deliverable);
        Assert.Equal("/output/report.json", task.Deliverable!.Path);
        Assert.Equal(Domain.Task.ValueObjects.DeliverableSource.StructuredOutput, task.Deliverable.Source);
        Assert.Equal("json", task.Deliverable.Format);
        Assert.False(task.Deliverable.Sanitize);
        Assert.False(string.IsNullOrWhiteSpace(task.Deliverable.SchemaInline));
        Assert.Contains("status", task.Deliverable.SchemaInline!);
    }

    [Fact]
    public void Adapt_deliverable_block_supercedes_expect_schema_for_Context()
    {
        // When both .expect() and .deliverable() are present, the deliverable
        // owns the schema and Context stays clean (no expect_schema entry).
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").build();
            const t = taskBuilder()
                .name("t")
                .agent(a)
                .description("d")
                .expectedOutput("o")
                .expect({ type: "object" })
                .deliverable({
                    path: "/output/r.md",
                    source: "final_message",
                })
                .build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);
        var task = Assert.Single(config.Tasks);

        Assert.NotNull(task.Deliverable);
        Assert.Equal("/output/r.md", task.Deliverable!.Path);
        Assert.False(task.Context.ContainsKey("expect_schema"));
    }

    [Fact]
    public void Adapt_deliverable_rejects_unknown_source_string()
    {
        var engine = NewEngine();
        // Build succeeds (script-side) — adapter rejects at materialisation time.
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").build();
            const t = taskBuilder()
                .name("t")
                .agent(a)
                .description("d")
                .expectedOutput("o")
                .deliverable({ path: "/output/x", source: "telegraph" })
                .build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var ex = Assert.Throws<InvalidOperationException>(
            () => JsCrewConfigurationAdapter.ToConfiguration(crew));
        Assert.Contains("telegraph", ex.Message);
    }

    [Fact]
    public void Adapt_emits_fresh_AgentIds_and_TaskIds_per_call()
    {
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("R").goal("G").build();
            const t = taskBuilder().name("t").agent(a).description("d").expectedOutput("o").build();
            crewBuilder().name("c").withAgent(a).withTask(t).build();
            """);

        var c1 = JsCrewConfigurationAdapter.ToConfiguration(crew);
        var c2 = JsCrewConfigurationAdapter.ToConfiguration(crew);

        // The adapter is meant to be a pure transformation, so two invocations must produce
        // independent identifier sets (no shared state). This guards against accidental
        // reuse of AgentIds across orchestrator runs (which would collide in repositories).
        Assert.NotEqual(c1.Agents[0].Id, c2.Agents[0].Id);
        Assert.NotEqual(c1.Tasks[0].Id, c2.Tasks[0].Id);
    }

    [Fact]
    public void Adapt_passes_validation_from_YamlCrewDefinitionLoader_validator()
    {
        // The CrewFactory pipeline calls ICrewDefinitionLoader.Validate on whatever
        // CrewConfiguration the adapter produces (in the .ork.ts dispatch path the
        // YAML loader is the one registered in DI, so it gets to vet our output).
        // This test pins the shape we produce against that validator's expectations.
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const a = agentBuilder().name("a").role("Analyst").goal("Analyze").build();
            const t = taskBuilder().name("t").agent(a).description("Investigate").expectedOutput("Report").build();
            crewBuilder().name("validate-me").goal("Make sure validation passes").withAgent(a).withTask(t).build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        Assert.False(string.IsNullOrWhiteSpace(config.Name));
        Assert.False(string.IsNullOrWhiteSpace(config.Goal));
        Assert.NotEmpty(config.Agents);
        Assert.NotEmpty(config.Tasks);
        Assert.All(config.Agents, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Role));
            Assert.False(string.IsNullOrWhiteSpace(a.Goal));
        });
        Assert.All(config.Tasks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.False(string.IsNullOrWhiteSpace(t.ExpectedOutput));
            Assert.NotNull(t.AssignedAgentId);
            // Every dependency must reference a real task in the same configuration.
            foreach (var dep in t.Dependencies)
                Assert.Contains(config.Tasks, x => x.Id.Equals(dep));
        });
    }

    [Fact]
    public void Adapt_maps_memory_humanInput_asyncExecution_and_task_tools()
    {
        // The four YAML-parity gaps EX-01 closed, plus first-class script tools:
        // instance names reach the config, and CollectScriptTools hands the loader
        // the instances to register before strict resolution runs.
        var engine = NewEngine();
        var crew = BuildCrew(engine, """
            const indicator = toolBuilder()
                .name("script_tool")
                .description("Computes a number")
                .withSchema({ type: "object", properties: { n: { type: "number", description: "n" } }, required: ["n"] })
                .execute((input) => ({ doubled: input.n * 2 }))
                .build();

            const analyst = agentBuilder()
                .name("analyst").role("Analyst").goal("Analyze")
                .withAutonomousTool(indicator)
                .build();

            const work = taskBuilder()
                .name("work").agent(analyst)
                .description("Do the work").expectedOutput("A result")
                .humanInput(true)
                .asyncExecution(true)
                .tools(["json_tool", indicator])
                .build();

            crewBuilder()
                .name("gaps").goal("Close the gaps")
                .memory(true)
                .withAgent(analyst)
                .withTask(work)
                .build();
            """);

        var config = JsCrewConfigurationAdapter.ToConfiguration(crew);

        Assert.True(config.Memory);
        var task = Assert.Single(config.Tasks);
        Assert.True(task.HumanInput);
        Assert.True(task.AsyncExecution);
        Assert.Equal(ExpectedTaskTools, task.RequiredTools);
        var agent = Assert.Single(config.Agents);
        Assert.Contains("script_tool", agent.Tools);

        var scriptTools = JsCrewConfigurationAdapter.CollectScriptTools(crew);
        var tool = Assert.Single(scriptTools);
        Assert.Equal("script_tool", tool.Name);
    }
}
