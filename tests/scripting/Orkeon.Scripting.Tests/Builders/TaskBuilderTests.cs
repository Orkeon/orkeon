using Jint;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Builders;

public sealed class TaskBuilderTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(Engine engine, string js)
        => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public void TaskBuilder_minimum_required_fields_builds()
    {
        var task = Eval<JsTask>(NewEngine(), """
            const a = agentBuilder().name("A").role("R").goal("G").build();
            taskBuilder()
              .description("Investigate quantum computing")
              .agent(a)
              .expectedOutput("3-bullet summary")
              .build();
            """);

        Assert.Equal("Investigate quantum computing", task.description);
        Assert.Equal("3-bullet summary", task.expectedOutput);
        Assert.Equal(26, task.id.Length);
    }

    [Fact]
    public void TaskBuilder_missing_agent_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                taskBuilder().description("x").expectedOutput("y").build();
                """),
            "agent");

        Assert.NotNull(ex);
    }

    [Fact]
    public void TaskBuilder_missing_description_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                const a = agentBuilder().name("A").role("R").goal("G").build();
                taskBuilder().agent(a).expectedOutput("y").build();
                """),
            "description");

        Assert.NotNull(ex);
    }

    [Fact]
    public void TaskBuilder_withContext_chains_tasks()
    {
        var task = Eval<JsTask>(NewEngine(), """
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const t1 = taskBuilder().description("first").agent(a).expectedOutput("o1").build();
            const t2 = taskBuilder().description("second").agent(a).expectedOutput("o2").withContext(t1).build();
            t2;
            """);

        Assert.Single(task.Context);
        Assert.Equal("first", task.Context[0].description);
    }

    [Fact]
    public void TaskBuilder_withContexts_accepts_array()
    {
        var task = Eval<JsTask>(NewEngine(), """
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const t1 = taskBuilder().description("first").agent(a).expectedOutput("o1").build();
            const t2 = taskBuilder().description("second").agent(a).expectedOutput("o2").build();
            taskBuilder().description("third").agent(a).expectedOutput("o3").withContexts([t1, t2]).build();
            """);

        Assert.Equal(2, task.Context.Count);
    }

    private const string Triage = """
        const triage = taskBuilder().name("triage").decision({
            questions: {
                category: { choice: { bug: "a defect", billing: "an invoice question" } },
                urgency: { score: ["low", "medium", "high"], instructions: "How urgent is it" },
                refund_requested: { noul: "the customer asks for a refund" },
            },
            minConfidence: 0.5,
            state: ["intake"],
            longContext: true,
            fallback: { category: "bug" },
        }).build();
        """;

    private const string Intake = """
        const writer = agentBuilder().name("writer").role("Writer").goal("Write").build();
        const intake = taskBuilder().name("intake").agent(writer)
            .description("Read the ticket").expectedOutput("A summary").build();
        """;

    private static CrewConfiguration Configure(string script, string process = "sequential")
    {
        var engine = new JsEngineFactory().Create();
        var manager = process is "hierarchical" or "consensual" ? ".manager(chef)" : string.Empty;
        var crew = (JsCrew)engine.Evaluate($$"""
            {{Intake}}
            {{script}}
            const chef = agentBuilder().name("chef").role("Chef").goal("Lead").build();
            crewBuilder().name("support").goal("Handle tickets").process("{{process}}")
                .withAgent(writer).withAgent(chef){{manager}}
                .withTasks(allTasks).build();
            """).ToObject()!;
        return JsCrewConfigurationAdapter.ToConfiguration(crew);
    }

    private static string Refusal(string script, string process = "sequential")
        => Assert.ThrowsAny<Exception>(() => Configure(script, process)).Message;

    private const string WhenTasks = """
        const refund = taskBuilder().name("refund").agent(writer)
            .description("Handle the refund").expectedOutput("A refund")
            .when("triage", { category: ["billing"], urgency: ">= 0.7", "category.bug": ">= 0.25" }).build();
        const other = taskBuilder().name("other").agent(writer)
            .description("Handle the rest").expectedOutput("An answer")
            .when("triage", "otherwise").build();
        const allTasks = [intake, triage, refund, other];
        """;

    [Fact]
    [Trait("BR", "UC-01")]
    public void ShouldBuildDecisionTask_WhenScriptCallsDecisionWithoutAgent()
    {
        var config = Configure(Triage + "const allTasks = [intake, triage];");

        var task = config.Tasks.Single(t => t.Key == "triage");
        Assert.Null(task.AssignedAgentId);
        var decision = Assert.IsType<TaskDecision>(task.Decision);
        Assert.Equal(3, decision.Questions.Count);
        var category = Assert.IsType<ChoiceQuestionSpec>(decision.Question("category"));
        Assert.Equal("a defect", category.Options["bug"]);
        Assert.Equal("an invoice question", category.Options["billing"]);
        var urgency = Assert.IsType<ScoreQuestionSpec>(decision.Question("urgency"));
        Assert.Equal(["low", "medium", "high"], urgency.Levels);
        Assert.Equal("How urgent is it", urgency.Instructions);
        var refund = Assert.IsType<YesNoQuestionSpec>(decision.Question("refund_requested"));
        Assert.Equal("the customer asks for a refund", refund.Statement);
        Assert.Equal(0.5, decision.ConfidenceFloor);
        Assert.True(decision.LongContext);
        Assert.Equal(["intake"], decision.StateFrom);
        Assert.Equal("bug", decision.Fallback["category"]);
    }

    [Fact]
    [Trait("BR", "UC-02")]
    public void ShouldBuildConditions_WhenScriptCallsWhenWithOptionsThresholdOrOtherwise()
    {
        var config = Configure(Triage + WhenTasks);
        var decision = config.Tasks.Single(t => t.Key == "triage").Decision!;

        var condition = Assert.IsType<TaskCondition>(config.Tasks.Single(t => t.Key == "refund").Condition);
        var expected = TaskCondition.Create("triage",
        [
            ConditionClause.OneOf(decision, "category", ["billing"]),
            ConditionClause.Threshold(decision, "urgency", null, ConditionComparison.GreaterOrEqual, 0.7),
            ConditionClause.Threshold(decision, "category", "bug", ConditionComparison.GreaterOrEqual, 0.25),
        ]);
        Assert.Equal(expected, condition);

        var otherwise = Assert.IsType<TaskCondition>(config.Tasks.Single(t => t.Key == "other").Condition);
        Assert.True(otherwise.IsOtherwise);
        Assert.Equal("triage", otherwise.DecisionTask);
    }

    [Fact]
    [Trait("BR", "UC-02")]
    public void ShouldAddDecisionAndStateTasksToDependencies_WhenScriptDeclaresWhenAndState()
    {
        var config = Configure(Triage + WhenTasks);

        var triage = config.Tasks.Single(t => t.Key == "triage");
        Assert.Contains(config.Tasks.Single(t => t.Key == "intake").Id, triage.Dependencies);
        Assert.Contains(triage.Id, config.Tasks.Single(t => t.Key == "refund").Dependencies);
        Assert.Contains(triage.Id, config.Tasks.Single(t => t.Key == "other").Dependencies);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseScript_WhenAgentAndDecisionBothSet_NamingTask()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").agent(writer)
                .description("Triage").expectedOutput("A triage")
                .decision({ questions: { category: { choice: { bug: "a defect" } } } }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Contains("triage", message, StringComparison.Ordinal);
        Assert.Contains("decision", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseScript_WhenDecisionStateNamesNoTaskOfTheCrew()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                state: ["ghost"],
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Equal("Task 'triage' decision.state names 'ghost', which is no task of the crew.", message);
    }

    private const string Q = "questions: { category: { choice: { bug: \"a defect\" } }, urgency: { score: [\"low\", \"high\"] } }";

    [Theory]
    [Trait("BR", "BR-07")]
    [InlineData(Q, "", "ghost", "{ category: [\"bug\"] }", "sequential", "refund", "when")]
    [InlineData(Q, "", "triage", "{ severity: [\"bug\"] }", "sequential", "refund", "when")]
    [InlineData(Q, "", "triage", "{ category: [\"nope\"] }", "sequential", "refund", "when")]
    [InlineData(Q, "", "triage", "{ \"category.nope\": \">= 0.5\" }", "sequential", "refund", "when")]
    [InlineData(Q, "", "triage", "{ category: [\"bug\"] }", "hierarchical", "triage", "decision")]
    [InlineData(Q, "", "triage", "{ category: [\"bug\"] }", "consensual", "triage", "decision")]
    [InlineData(Q, "fallback: { ghost: \"bug\" },", "triage", "{ category: [\"bug\"] }", "sequential", "triage", "decision.fallback")]
    public void ShouldRefuseScript_WhenWhenNamesUnknownTaskOrProcessUnsupported_NamingTask(
        string questions, string extra, string whenTask, string clauses, string process, string task, string part)
    {
        var message = Refusal($$"""
            const triage = taskBuilder().name("triage").decision({ {{questions}}, {{extra}} }).build();
            const refund = taskBuilder().name("refund").agent(writer)
                .description("Handle the refund").expectedOutput("A refund")
                .when("{{whenTask}}", {{clauses}}).build();
            const allTasks = [intake, triage, refund];
            """, process);

        Assert.Contains(task, message, StringComparison.Ordinal);
        Assert.Contains(part, message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "BR-07")]
    [InlineData("minConfidence: 1.5,", "urgency: \">= 0.7\"", "triage", "decision")]
    [InlineData("minConfidence: 0.5,", "urgency: \">= 1.5\"", "refund", "when")]
    public void ShouldRefuseScript_WhenThresholdOrFloorOutOfRange_NamingTask(
        string floor, string clause, string task, string part)
    {
        var message = Refusal($$"""
            const triage = taskBuilder().name("triage").decision({
                questions: { urgency: { score: ["low", "high"] } },
                {{floor}}
            }).build();
            const refund = taskBuilder().name("refund").agent(writer)
                .description("Handle the refund").expectedOutput("A refund")
                .when("triage", { {{clause}} }).build();
            const allTasks = [intake, triage, refund];
            """);

        Assert.Contains(task, message, StringComparison.Ordinal);
        Assert.Contains(part, message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "BR-07")]
    [InlineData("null")]
    [InlineData("{ nested: 1 }")]
    [InlineData("3")]
    public void ShouldRefuseScript_WhenWhenClauseValueIsNotAStringOrList_NamingTask(string value)
    {
        var message = Refusal($$"""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
            }).build();
            const refund = taskBuilder().name("refund").agent(writer)
                .description("Handle the refund").expectedOutput("A refund")
                .when("triage", { category: {{value}} }).build();
            const allTasks = [intake, triage, refund];
            """);

        Assert.StartsWith("Task 'refund' when on 'triage': ", message, StringComparison.Ordinal);
        Assert.Contains("'category'", message, StringComparison.Ordinal);
        Assert.Contains("string", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseScript_WhenWhenKeyHasTooManySegments_NamingTask()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
            }).build();
            const refund = taskBuilder().name("refund").agent(writer)
                .description("Handle the refund").expectedOutput("A refund")
                .when("triage", { "category.bug.extra": ">= 0.5" }).build();
            const allTasks = [intake, triage, refund];
            """);

        Assert.Contains("refund", message, StringComparison.Ordinal);
        Assert.Contains("when", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseScript_WhenTwoDecisionTasksShareAName_NamingTask()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
            }).build();
            const triage2 = taskBuilder().name("triage").decision({
                questions: { urgency: { score: ["low", "high"] } },
            }).build();
            const allTasks = [intake, triage, triage2];
            """);

        Assert.Contains("Task 'triage'", message, StringComparison.Ordinal);
        Assert.Contains("twice", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseScript_WhenOptionClauseValueIsAList_NamingTask()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect", billing: "an invoice" } } },
            }).build();
            const refund = taskBuilder().name("refund").agent(writer)
                .description("Handle the refund").expectedOutput("A refund")
                .when("triage", { "category.bug": ["billing"] }).build();
            const allTasks = [intake, triage, refund];
            """);

        Assert.Contains("Task 'refund'", message, StringComparison.Ordinal);
        Assert.Contains("category.bug", message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "BR-07")]
    [InlineData("choice: { bug: \"a defect\" }, score: [\"low\", \"high\"]")]
    [InlineData("choice: { bug: \"a defect\" }, noul: \"a statement\"")]
    [InlineData("score: [\"low\", \"high\"], noul: \"a statement\"")]
    public void ShouldRefuseScript_WhenQuestionDeclaresMoreThanOneKind_NamingTask(string question)
    {
        var message = Refusal($$"""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { {{question}} } },
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Contains("Task 'triage'", message, StringComparison.Ordinal);
        Assert.Contains("decision.questions.category", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseScript_WhenMinConfidenceIsNotANumber_NamingTask()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                minConfidence: "high",
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.StartsWith("Task 'triage' decision", message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "BR-07")]
    [InlineData("true")]
    [InlineData("\"0.7\"")]
    public void ShouldRefuseMinConfidence_WhenNotANumber(string value)
    {
        var message = Refusal($$"""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                minConfidence: {{value}},
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.StartsWith("Task 'triage' decision:", message, StringComparison.Ordinal);
        Assert.Contains("minConfidence", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldRefuseLongContext_WhenNotABool()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                longContext: "yes",
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.StartsWith("Task 'triage' decision:", message, StringComparison.Ordinal);
        Assert.Contains("longContext", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-03")]
    public void ShouldRefuseTask_WhenWhenIsCalledTwice()
    {
        var ex = Assert.ThrowsAny<Exception>(() => NewEngine().Evaluate("""
            const writer = agentBuilder().name("writer").role("Writer").goal("Write").build();
            taskBuilder().name("refund").agent(writer)
                .description("Handle the refund").expectedOutput("A refund")
                .when("triage", { category: ["bug"] })
                .when("triage", "otherwise").build();
            """));

        Assert.Contains("InvalidScriptException", ex.GetType().Name + ex.InnerException?.GetType().Name, StringComparison.Ordinal);
        Assert.Contains("refund", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-30")]
    public void ShouldRefuseDecision_WhenStateIsNotAnArray()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                state: "intake",
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Contains("Task 'triage'", message, StringComparison.Ordinal);
        Assert.Contains("state", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-30")]
    public void ShouldRefuseDecision_WhenFallbackIsNotAnObject()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                fallback: "bug",
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Contains("Task 'triage'", message, StringComparison.Ordinal);
        Assert.Contains("fallback", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-31")]
    public void ShouldRefuseQuestion_WhenChoiceIsNotAnObjectOfStrings()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: 3 } } },
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Contains("Task 'triage'", message, StringComparison.Ordinal);
        Assert.Contains("choice", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-31")]
    public void ShouldRefuseQuestion_WhenScoreIsNotAnArray()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { urgency: { score: "low" } },
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Contains("Task 'triage'", message, StringComparison.Ordinal);
        Assert.Contains("score", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-17")]
    public void ShouldRefuseDecision_WhenConfidenceFloorIsNaN()
    {
        var message = Refusal("""
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                minConfidence: NaN,
            }).build();
            const allTasks = [intake, triage];
            """);

        Assert.Contains("Task 'triage'", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "R-28")]
    public void ShouldResolveState_WhenUnnamedTaskIsTargeted()
    {
        const string description = "Collect every detail of the customer ticket before the triage starts, in full";
        var config = Configure($$"""
            const raw = taskBuilder().agent(writer)
                .description("{{description}}").expectedOutput("Details").build();
            const triage = taskBuilder().name("triage").decision({
                questions: { category: { choice: { bug: "a defect" } } },
                state: ["{{description}}"],
            }).build();
            const allTasks = [intake, raw, triage];
            """);

        var raw = config.Tasks.Single(t => t.Description == description);
        var triage = config.Tasks.Single(t => t.Key == "triage");
        Assert.Equal(description, raw.Key);
        Assert.Contains(raw.Id, triage.Dependencies);
    }

    [Fact]
    [Trait("BR", "R-32")]
    public void ShouldRefuseCrew_WhenTwoTasksShareAName()
    {
        var message = Refusal("""
            const first = taskBuilder().name("report").agent(writer)
                .description("Write the first report").expectedOutput("A report").build();
            const second = taskBuilder().name("report").agent(writer)
                .description("Write the second report").expectedOutput("A report").build();
            const allTasks = [intake, first, second];
            """);

        Assert.Contains("'report'", message, StringComparison.Ordinal);
    }
}
