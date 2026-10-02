using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Crew;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Interfaces;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Parsing;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// The crew's planner (GAP-31). It used to be shown ids — "Agent {id}", "Task {id}" — and could only
/// decide an order. It reads the crew now: each task by number, in the order the run takes them,
/// with its description, expected output, dependencies (by number) and agent (role, goal, tools),
/// bounded, never an id; it writes one step-by-step plan per task, by number; a reply it cannot read
/// is asked for once more, then the crew runs without a plan; only a provider that fails stops it.
/// </summary>
public class CrewPlannerTests
{
    private static readonly IExecutionPlanParser s_parser = new ExecutionPlanParser();

    /// <summary>Answers each call with the next reply, and records what it was asked.</summary>
    private sealed class TestLlmProvider(params string[] replies) : ILlmProvider
    {
        private readonly Queue<string> _replies = new(replies);

        public string Name => "TestProvider";

        public LlmProviderCapabilities Capabilities { get; init; } = LlmProviderCapabilities.Unknown;

        public LlmConfig? BaseConfig { get; init; }

        public int CompletionTokens { get; init; } = 50;

        public List<string> Prompts { get; } = [];

        public List<LlmConfig?> Configs { get; } = [];

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prompts.Add(prompt);
            Configs.Add(config);
            return System.Threading.Tasks.Task.FromResult(new LlmResponse
            {
                Content = _replies.Count > 1 ? _replies.Dequeue() : _replies.Count == 1 ? _replies.Peek() : string.Empty,
                Model = TestModelName,
                TokensUsed = 100,
                CompletionTokens = CompletionTokens,
            });
        }

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The planner asks with GenerateAsync.");
    }

    /// <summary>A provider whose every call fails, the way the HTTP providers report it.</summary>
    private sealed class FailingLlmProvider(string error) : ILlmProvider
    {
        public string Name => "failing";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(Failure());

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(Failure());

        private LlmResponse Failure() => new()
        {
            Content = string.Empty,
            Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = error },
        };
    }

    private static readonly PlanningAgent s_writer = new("Writer", "Write the report", ["file_write", "web_search"]);
    private static readonly PlanningAgent s_reviewer = new("Reviewer", "Review the report", []);

    private static PlanningTask NewTask(string description, string expectedOutput, PlanningAgent? agent, params TaskId[] dependencies)
        => new(TaskId.Create(), description, expectedOutput, dependencies, agent);

    private static PlanningContext Context(ProcessType process, IReadOnlyList<PlanningTask> tasks, IReadOnlyDictionary<string, string>? variables = null)
        => new("Write a short report", process, tasks, [s_writer, s_reviewer], variables ?? new Dictionary<string, string>());

    /// <summary>Two tasks: an outline, then a draft that depends on it, both on the writer.</summary>
    private static (PlanningContext Context, PlanningTask Outline, PlanningTask Draft) TwoTasks(ProcessType? process = null)
    {
        var outline = NewTask("Outline the report on AI agents", "An outline", s_writer);
        var draft = NewTask("Draft the report", "A draft", s_writer, outline.Id);
        return (Context(process ?? ProcessType.Sequential, [outline, draft]), outline, draft);
    }

    private const string PlanBoth = """{"plans":[{"task":1,"plan":"1. List the sections."},{"task":2,"plan":"1. Write each section."}]}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── construction ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLlmProvider()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CrewPlanner.Create(null!, s_parser));
        Assert.Equal("planningLlm", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullParser()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => CrewPlanner.Create(new TestLlmProvider(), null!));
        Assert.Equal("parser", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenCreatingPlanWithNullContext()
    {
        var planner = CrewPlanner.Create(new TestLlmProvider(PlanBoth), s_parser);

        await Assert.ThrowsAsync<ArgumentNullException>(() => planner.CreatePlanAsync(null!, Ct));
    }

    // ── the call ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseLowTemperatureForConsistency_WhenCreatingPlanAsync()
    {
        var llm = new TestLlmProvider(PlanBoth);

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(TwoTasks().Context, Ct);

        Assert.Equal(0.3f, Assert.Single(llm.Configs)!.Temperature);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_planning_call_names_no_model_so_it_runs_on_the_planning_providers_own()
    {
        // GAP-18: the plan used to be asked of OpenAI's default model on whatever vendor the
        // crew's planning provider is. It names none now; the provider sends its own. Nor does it
        // set MaxTokens: a ceiling above the model's own would fail the call (GAP-31).
        var llm = new TestLlmProvider(PlanBoth);

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(TwoTasks().Context, Ct);

        var config = Assert.Single(llm.Configs)!;
        Assert.Equal(string.Empty, config.Model);
        Assert.Null(config.MaxTokens);
    }

    public static TheoryData<ResponseFormatSupport, string?> ReplyFormats() => new()
    {
        { ResponseFormatSupport.JsonSchema, "json_schema" },
        { ResponseFormatSupport.JsonObject, "json_object" },
        { ResponseFormatSupport.None, null },
    };

    [Theory]
    [MemberData(nameof(ReplyFormats))]
    public async System.Threading.Tasks.Task The_reply_format_follows_what_the_provider_can_constrain(ResponseFormatSupport support, string? expected)
    {
        var llm = new TestLlmProvider(PlanBoth) { Capabilities = new LlmProviderCapabilities { ResponseFormat = support } };

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(TwoTasks().Context, Ct);

        var format = Assert.Single(llm.Configs)!.ResponseFormat;
        Assert.Equal(expected, format?.Type);
        if (support == ResponseFormatSupport.JsonSchema)
            Assert.Contains("\"plans\"", format!.Schema!.Schema, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_cancelled_token_cancels_the_planning_call()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var llm = new TestLlmProvider(PlanBoth);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CrewPlanner.Create(llm, s_parser).CreatePlanAsync(TwoTasks().Context, cts.Token));
        Assert.Empty(llm.Prompts);
    }

    /// <summary>
    /// GAP-29: the planner read the content of a failed call — empty — and reported that the plan
    /// was missing its tasks. The provider's own reason (a refused key, an elapsed timeout) never
    /// appeared: it is what the failure says now, and a failed provider still stops the run.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task A_planning_call_that_failed_says_why()
    {
        var planner = CrewPlanner.Create(
            new FailingLlmProvider("DeepSeek API error: Unauthorized - Authentication Fails, Your api key is invalid"),
            s_parser);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => planner.CreatePlanAsync(TwoTasks().Context, Ct));

        Assert.Contains("Your api key is invalid", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("is missing from the plan", failure.Message, StringComparison.Ordinal);
    }

    // ── what the planner reads ───────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task The_prompt_numbers_each_task_with_its_text_dependencies_and_agent_and_names_no_id()
    {
        var (context, outline, draft) = TwoTasks();
        var llm = new TestLlmProvider(PlanBoth);

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(context, Ct);

        var prompt = Assert.Single(llm.Prompts);
        Assert.StartsWith("You are the planner of an agent crew.", prompt, StringComparison.Ordinal);
        Assert.Contains("Write a short report", prompt, StringComparison.Ordinal);
        var task1 = prompt.IndexOf("Task 1\nDescription: Outline the report on AI agents\nExpected output: An outline\nDepends on: none\n", StringComparison.Ordinal);
        var task2 = prompt.IndexOf("Task 2\nDescription: Draft the report\nExpected output: A draft\nDepends on: task 1\n", StringComparison.Ordinal);
        Assert.True(task1 >= 0 && task2 > task1, prompt);
        Assert.Contains("Agent: Writer (goal: Write the report; tools: file_write, web_search)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(outline.Id.ToString(), prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(draft.Id.ToString(), prompt, StringComparison.Ordinal);
        Assert.Contains("{\"plans\": [{\"task\": 1, \"plan\":", prompt, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> ModesThatChooseTheAgent() => new()
    {
        { "Hierarchical", "assigned by the manager" },
        { "Autonomous", "assigned by the manager" },
        { "Consensual", "every agent answers" },
    };

    [Theory]
    [MemberData(nameof(ModesThatChooseTheAgent))]
    public async System.Threading.Tasks.Task Where_the_mode_chooses_who_runs_a_task_the_prompt_says_so_and_lists_the_agents_once(string mode, string phrase)
    {
        var llm = new TestLlmProvider(PlanBoth);

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(TwoTasks(ProcessType.From(mode)).Context, Ct);

        var prompt = Assert.Single(llm.Prompts);
        Assert.Equal(2, CountOf(prompt, $"Agent: {phrase}"));
        Assert.Equal(1, CountOf(prompt, "- Writer (goal: Write the report; tools: file_write, web_search)"));
        Assert.Equal(1, CountOf(prompt, "- Reviewer (goal: Review the report; tools: none)"));
    }

    [Fact]
    public async System.Threading.Tasks.Task A_task_without_an_agent_is_chosen_at_run_time_in_the_modes_that_honour_one()
    {
        var unassigned = NewTask("Check the facts", "A list of checked facts", agent: null);
        var llm = new TestLlmProvider("""{"plans":[{"task":1,"plan":"1. Check."}]}""");

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(Context(ProcessType.Sequential, [unassigned]), Ct);

        var prompt = Assert.Single(llm.Prompts);
        Assert.Contains("Agent: chosen at run time", prompt, StringComparison.Ordinal);
        Assert.Contains("- Reviewer (goal: Review the report; tools: none)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task Long_texts_are_cut_with_a_mark_and_an_agent_shows_fifteen_tools_at_most()
    {
        var description = "D" + new string('d', 32_766) + "Z";
        var tools = Enumerable.Range(1, 25).Select(i => $"tool_{i:00}").ToList();
        var agent = new PlanningAgent("Researcher", "G" + new string('g', 2_046) + "Z", tools);
        var task = new PlanningTask(TaskId.Create(), description, "E" + new string('e', 4_094) + "Z", [], agent);
        var llm = new TestLlmProvider("""{"plans":[{"task":1,"plan":"1. Research."}]}""");

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(Context(ProcessType.Sequential, [task]), Ct);

        var prompt = Assert.Single(llm.Prompts);
        Assert.Contains(description[..PlanningDefaults.MaxTaskDescriptionChars] + PlanningDefaults.TruncationMarker, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(description[..(PlanningDefaults.MaxTaskDescriptionChars + 1)], prompt, StringComparison.Ordinal);
        Assert.Contains(task.ExpectedOutput[..PlanningDefaults.MaxExpectedOutputChars] + PlanningDefaults.TruncationMarker, prompt, StringComparison.Ordinal);
        Assert.Contains(agent.Goal[..PlanningDefaults.MaxAgentGoalChars] + PlanningDefaults.TruncationMarker, prompt, StringComparison.Ordinal);
        Assert.Equal(PlanningDefaults.MaxToolNamesPerAgent, tools.Count(tool => prompt.Contains(tool, StringComparison.Ordinal)));
        Assert.Contains("(+10 more)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_runs_variables_are_shown_within_their_bound()
    {
        var variables = new Dictionary<string, string>
        {
            ["initial_context"] = "Focus on agent frameworks",
            ["corpus"] = new string('c', 3_000),
        };
        var llm = new TestLlmProvider(PlanBoth);

        await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(Context(ProcessType.Sequential, TwoTasks().Context.Tasks, variables), Ct);

        var prompt = Assert.Single(llm.Prompts);
        Assert.Contains("- initial_context: Focus on agent frameworks", prompt, StringComparison.Ordinal);
        Assert.Contains(PlanningDefaults.TruncationMarker, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('c', PlanningDefaults.MaxVariablesChars), prompt, StringComparison.Ordinal);
    }

    // ── what the planner writes, and where it goes ───────────────────────────────────────

    [Theory]
    [InlineData("""{"plans":[{"task":2,"plan":"B"},{"task":1,"plan":"A"}]}""")]
    [InlineData("Here is the plan:\n```json\n{\"plans\":[{\"task\":2,\"plan\":\"B\"},{\"task\":1,\"plan\":\"A\"}]}\n```\nGood luck!")]
    public async System.Threading.Tasks.Task Each_plan_goes_to_the_task_its_number_names(string reply)
    {
        var (context, outline, draft) = TwoTasks();

        var planning = await CrewPlanner.Create(new TestLlmProvider(reply), s_parser).CreatePlanAsync(context, Ct);

        Assert.Equal("A", planning.Plan.InstructionsFor(outline.Id));
        Assert.Equal("B", planning.Plan.InstructionsFor(draft.Id));
        Assert.Empty(planning.Warnings);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_number_given_twice_keeps_its_first_plan_and_an_unknown_one_is_ignored_each_with_a_warning()
    {
        var (context, outline, draft) = TwoTasks();
        var reply = """{"plans":[{"task":1,"plan":"FIRST"},{"task":1,"plan":"SECOND"},{"task":9,"plan":"NINE"},{"task":2,"plan":"B"}]}""";

        var planning = await CrewPlanner.Create(new TestLlmProvider(reply), s_parser).CreatePlanAsync(context, Ct);

        Assert.Equal("FIRST", planning.Plan.InstructionsFor(outline.Id));
        Assert.Equal("B", planning.Plan.InstructionsFor(draft.Id));
        Assert.Equal(2, planning.Plan.Tasks.Count);
        Assert.Contains(planning.Warnings, w => w.Contains("task 1", StringComparison.Ordinal) && w.Contains("first", StringComparison.Ordinal));
        Assert.Contains(planning.Warnings, w => w.Contains("task 9", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task A_task_the_plan_leaves_out_is_named_in_a_warning()
    {
        var (context, outline, draft) = TwoTasks();

        var planning = await CrewPlanner.Create(new TestLlmProvider("""{"plans":[{"task":1,"plan":"A"}]}"""), s_parser)
            .CreatePlanAsync(context, Ct);

        Assert.Equal("A", planning.Plan.InstructionsFor(outline.Id));
        Assert.Null(planning.Plan.InstructionsFor(draft.Id));
        var warning = Assert.Single(planning.Warnings);
        Assert.Contains("task 2", warning, StringComparison.Ordinal);
        Assert.Contains("Draft the report", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task An_unreadable_reply_is_asked_for_once_more_with_what_could_not_be_read()
    {
        var (context, outline, _) = TwoTasks();
        var llm = new TestLlmProvider("I would start with the outline.", PlanBoth);

        var planning = await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(context, Ct);

        Assert.Equal(2, llm.Prompts.Count);
        Assert.StartsWith(llm.Prompts[0], llm.Prompts[1], StringComparison.Ordinal);
        Assert.Contains("could not be read", llm.Prompts[1], StringComparison.Ordinal);
        Assert.Equal("1. List the sections.", planning.Plan.InstructionsFor(outline.Id));
        Assert.Empty(planning.Warnings);
    }

    [Fact]
    public async System.Threading.Tasks.Task Two_unreadable_replies_leave_the_crew_without_a_plan_and_say_so()
    {
        var llm = new TestLlmProvider("not a plan", "still not a plan");

        var planning = await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(TwoTasks().Context, Ct);

        Assert.Equal(2, llm.Prompts.Count);
        Assert.Empty(planning.Plan.Tasks);
        var warning = Assert.Single(planning.Warnings);
        Assert.Contains("without a plan", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxTokens", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_reply_cut_at_the_providers_MaxTokens_says_so()
    {
        var llm = new TestLlmProvider("""{"plans":[{"task":1,"plan":"1. List""")
        {
            BaseConfig = LlmConfig.OnProfile() with { MaxTokens = 50 },
            CompletionTokens = 50,
        };

        var planning = await CrewPlanner.Create(llm, s_parser).CreatePlanAsync(TwoTasks().Context, Ct);

        Assert.Contains("MaxTokens", Assert.Single(planning.Warnings), StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
