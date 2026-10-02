using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// <c>planning: true</c> earns its call. GAP-29 made it plan, on the host's default profile — the
/// planner stays there (GAP-19) — and a plan that fails says why. GAP-31: the planner reads the
/// crew (each task by number, its description, expected output, dependencies and agent), writes a
/// step-by-step plan per task, and each task finds its own plan in its prompt; the plan changes
/// neither the order of the tasks nor who runs them; an unreadable plan is asked for once more, then
/// the crew runs without one; the echo provider is not asked; cancelling the run cancels the call.
/// </summary>
public sealed partial class PlanningOnTheDefaultProfileTests
{
    private const string PlannedYaml = """
        name: planned-report
        goal: Write a short report
        process: sequential
        planning: true
        agents:
          writer:
            role: Writer
            goal: Write the report
        tasks:
          outline:
            description: Outline the report on {topic}
            expected_output: An outline of the {topic} report
            agent: writer
          draft:
            description: Draft the report
            expected_output: A draft
            agent: writer
            dependencies: [outline]
        """;

    private const string Outline = "Outline the report on AI agents";
    private const string Draft = "Draft the report";

    /// <summary>The section header a task's prompt shows its plan under.</summary>
    private const string PlanSection = "Plan for this task, from the crew's planner";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^Task (\d+)\r?\nDescription: (.+?)\r?$", RegexOptions.Multiline)]
    private static partial Regex PlannedTaskHeading();

    /// <summary>An entity id as the domain writes it: a ULID, 26 characters of Crockford base 32.</summary>
    [GeneratedRegex(@"\b[0-9A-HJKMNP-TV-Z]{26}\b")]
    private static partial Regex AnyEntityId();

    /// <summary>
    /// A vendor that plans the tasks its planning prompt numbers — "PLAN for &lt;description&gt;" each —
    /// and records every call: <c>plan@name</c>, then <c>task:&lt;name&gt;</c> for each task it runs,
    /// with the user prompt of each task.
    /// </summary>
    private sealed class Vendor
    {
        public Vendor(string name)
        {
            Provider = new MockLlmProvider { Name = name };
            Provider.SetGenerateFunc((prompt, _) =>
            {
                Calls.Add($"plan@{name}");
                PlanPrompts.Add(prompt);
                return new LlmResponse { Content = PlanReply(prompt), PromptTokens = 10, CompletionTokens = 5, TokensUsed = 15 };
            });
            Provider.SetChatFunc((messages, _) =>
            {
                var user = messages.First(m => m.Role == "user").Content;
                var task = user.Contains(Draft, StringComparison.Ordinal) ? "draft"
                    : user.Contains(Outline, StringComparison.Ordinal) ? "outline"
                    : "?";
                Calls.Add($"task:{task}");
                TaskPrompts.Add((task, user));
                return new LlmResponse { Content = "done", PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 };
            });
        }

        public MockLlmProvider Provider { get; }

        public List<string> Calls { get; } = [];

        public List<string> PlanPrompts { get; } = [];

        public List<(string Task, string Prompt)> TaskPrompts { get; } = [];

        /// <summary>The plan reply; by default, one plan per numbered task, in the order they were numbered.</summary>
        public Func<string, string> PlanReply { get; set; } = prompt => JsonSerializer.Serialize(new
        {
            plans = NumberedTasks(prompt).Select(t => new { task = t.Number, plan = $"PLAN for {t.Description}" }).ToArray(),
        });

        public string PromptOf(string task) => TaskPrompts.Single(p => p.Task == task).Prompt;
    }

    /// <summary>The tasks a planning prompt numbers, with the first line of their description.</summary>
    private static List<(int Number, string Description)> NumberedTasks(string planningPrompt) =>
        [.. PlannedTaskHeading().Matches(planningPrompt).Select(m => (int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[2].Value))];

    private static ServiceProvider Host(MockLlmProvider @default, RecordingLoggerFactory? logs = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        if (logs is not null)
            services.AddSingleton<ILoggerFactory>(logs);
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => @default, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static CrewInput Input() =>
        new("report", new Dictionary<string, object> { ["topic"] = "AI agents" });

    private static Task<CrewOutput> RunYamlAsync(MockLlmProvider @default, string yaml, RecordingLoggerFactory? logs = null) =>
        RunCancellableYamlAsync(@default, yaml, logs, Ct);

    private static async Task<CrewOutput> RunCancellableYamlAsync(
        MockLlmProvider @default, string yaml, RecordingLoggerFactory? logs, CancellationToken cancellationToken)
    {
        await using var container = Host(@default, logs);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(crew.Id, Input(), cancellationToken);
    }

    private static async Task<CrewOutput> RunBuiltAsync(MockLlmProvider @default, Func<CrewBuilder, CrewBuilder> planning)
    {
        await using var container = Host(@default);
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var writer = new AgentBuilder().Role("Writer").Goal("Write the report").Build();
        var outline = new CrewTaskBuilder().Description("Outline the report on {topic}").ExpectedOutput("An outline").AssignTo(writer).Build();
        var draft = new CrewTaskBuilder().Description("Draft the report").ExpectedOutput("A draft").AssignTo(writer).Build();
        var crew = planning(new CrewBuilder().Goal("Write a short report").Sequential().WithAgent(writer)).Build();
        crew.AddTask(outline.Id);
        crew.AddTask(draft.Id);

        await sp.GetRequiredService<IAgentRepository>().AddAsync(writer, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(outline, Ct);
        await sp.GetRequiredService<ITaskRepository>().AddAsync(draft, Ct);
        await sp.GetRequiredService<ICrewRepository>().AddAsync(crew, Ct);

        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(crew.Id, Input(), Ct);
    }

    private static IEnumerable<string> Warnings(RecordingLoggerFactory logs) =>
        logs.Logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    // ── the planner runs on the default profile (GAP-29) ─────────────────────────────────

    [Fact]
    public async Task A_yaml_crew_with_planning_true_plans_on_the_default_profile_then_runs_its_tasks()
    {
        var vendor = new Vendor("default");

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@default", "task:outline", "task:draft"], vendor.Calls);
        // The planner's own sampling, on the profile's model.
        Assert.Equal(0.3, vendor.Provider.LastGenerateConfig!.Temperature, precision: 3);
        Assert.Equal(string.Empty, vendor.Provider.LastGenerateConfig.Model);
    }

    [Fact]
    public async Task A_csharp_crew_calling_Planning_alone_plans_on_the_default_profile()
    {
        var vendor = new Vendor("default");

        var output = await RunBuiltAsync(vendor.Provider, crew => crew.Planning());

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@default", "task:outline", "task:draft"], vendor.Calls);
        Assert.Contains("PLAN for Outline the report on AI agents", vendor.PromptOf("outline"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_planning_provider_set_in_csharp_keeps_the_hand()
    {
        var planner = new Vendor("planner");
        var @default = new Vendor("default");

        var output = await RunBuiltAsync(@default.Provider, crew => crew.Planning().WithPlanningLlm(planner.Provider));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@planner"], planner.Calls);
        Assert.Equal(["task:outline", "task:draft"], @default.Calls);
    }

    [Fact]
    public async Task A_crew_without_planning_makes_no_planning_call()
    {
        var vendor = new Vendor("default");

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml.Replace("planning: true", "planning: false", StringComparison.Ordinal));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["task:outline", "task:draft"], vendor.Calls);
        Assert.All(vendor.TaskPrompts, p => Assert.DoesNotContain(PlanSection, p.Prompt, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_plan_that_fails_fails_the_crew_with_the_providers_reason()
    {
        // The planner ignored the provider's error and reported "Invalid plan: Task … is missing
        // from the plan" — the real cause (here a refused key) never appeared anywhere. A provider
        // that fails is still the one thing that stops the run (GAP-31, decision 2.5).
        var vendor = new Vendor("default");
        vendor.Provider.SetGenerateFunc((_, _) => new LlmResponse
        {
            Content = string.Empty,
            Metadata = new Dictionary<string, object>
            {
                [LlmResponseMetadataKeys.Error] = "DeepSeek API error: Unauthorized - Authentication Fails, Your api key is invalid",
            },
        });

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml);

        Assert.False(output.Succeeded);
        Assert.Contains("Your api key is invalid", output.Error, StringComparison.Ordinal);
        Assert.Empty(vendor.Calls);
    }

    // ── what the planner reads, and where its plan goes (GAP-31) ─────────────────────────

    [Fact]
    public async Task Each_task_reads_its_own_plan_and_no_other()
    {
        var vendor = new Vendor("default");

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml);

        Assert.True(output.Succeeded, output.Error);
        var outline = vendor.PromptOf("outline");
        var draft = vendor.PromptOf("draft");
        Assert.Contains(PlanSection, outline, StringComparison.Ordinal);
        Assert.Contains($"PLAN for {Outline}", outline, StringComparison.Ordinal);
        Assert.DoesNotContain($"PLAN for {Draft}", outline, StringComparison.Ordinal);
        Assert.Contains($"PLAN for {Draft}", draft, StringComparison.Ordinal);
        Assert.DoesNotContain($"PLAN for {Outline}", draft, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_planner_reads_the_tasks_descriptions_expected_outputs_dependencies_and_agents_by_number()
    {
        var vendor = new Vendor("default");

        await RunYamlAsync(vendor.Provider, PlannedYaml);

        var prompt = Assert.Single(vendor.PlanPrompts);
        // Interpolated as the agents read them, numbered in the order the run takes them.
        Assert.Equal([(1, Outline), (2, Draft)], NumberedTasks(prompt));
        Assert.Contains("An outline of the AI agents report", prompt, StringComparison.Ordinal);
        Assert.Contains("A draft", prompt, StringComparison.Ordinal);
        Assert.Contains("Depends on: task 1", prompt, StringComparison.Ordinal);
        Assert.Contains("Writer", prompt, StringComparison.Ordinal);
        Assert.Contains("Write the report", prompt, StringComparison.Ordinal);
        Assert.Contains("topic: AI agents", prompt, StringComparison.Ordinal);
        Assert.Contains("Write a short report", prompt, StringComparison.Ordinal);
        // The planner answers by number: it is never shown an id to copy back.
        Assert.DoesNotMatch(AnyEntityId(), prompt);
    }

    [Fact]
    public async Task A_plan_listing_the_tasks_in_another_order_changes_neither_the_order_nor_the_executions()
    {
        // GAP-29 followed the plan's order wherever the dependencies allowed it: the crew's output,
        // the context a task reads and Parallel's round-robin were at the plan's mercy. The plan
        // never orders anything now (SequentialTaskOrderTests used to pin the opposite).
        var vendor = new Vendor("default")
        {
            PlanReply = _ => """{"plans":[{"task":2,"plan":"PLAN B"},{"task":1,"plan":"PLAN A"}]}""",
        };

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml.Replace("    dependencies: [outline]", "", StringComparison.Ordinal));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@default", "task:outline", "task:draft"], vendor.Calls);
        Assert.Contains("PLAN A", vendor.PromptOf("outline"), StringComparison.Ordinal);
        Assert.Contains("PLAN B", vendor.PromptOf("draft"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_naming_a_task_twice_runs_it_once_on_the_first_plan_and_says_so()
    {
        // A plan citing a task twice used to run it twice (CrewTaskSequencer took the plan's list).
        using var logs = new RecordingLoggerFactory();
        var vendor = new Vendor("default")
        {
            PlanReply = _ => """{"plans":[{"task":1,"plan":"FIRST"},{"task":1,"plan":"SECOND"},{"task":2,"plan":"PLAN B"}]}""",
        };

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml, logs);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@default", "task:outline", "task:draft"], vendor.Calls);
        Assert.Contains("FIRST", vendor.PromptOf("outline"), StringComparison.Ordinal);
        Assert.DoesNotContain("SECOND", vendor.PromptOf("outline"), StringComparison.Ordinal);
        Assert.Contains(Warnings(logs), w => w.Contains("task 1", StringComparison.Ordinal) && w.Contains("first", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unreadable_plan_is_asked_for_once_more_then_the_crew_runs_without_one()
    {
        using var logs = new RecordingLoggerFactory();
        var vendor = new Vendor("default") { PlanReply = _ => "Sure! Here is my plan: do the outline, then the draft." };

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml, logs);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(["plan@default", "plan@default", "task:outline", "task:draft"], vendor.Calls);
        // The second request carries what could not be read in the first.
        Assert.Contains("could not be read", vendor.PlanPrompts[1], StringComparison.Ordinal);
        Assert.All(vendor.TaskPrompts, p => Assert.DoesNotContain(PlanSection, p.Prompt, StringComparison.Ordinal));
        Assert.Contains(Warnings(logs), w => w.Contains("without a plan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_reply_that_reads_is_the_plan()
    {
        var replies = new Queue<string>(["not json at all", """{"plans":[{"task":1,"plan":"PLAN A"},{"task":2,"plan":"PLAN B"}]}"""]);
        var vendor = new Vendor("default") { PlanReply = _ => replies.Dequeue() };

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(2, vendor.PlanPrompts.Count);
        Assert.Contains("PLAN A", vendor.PromptOf("outline"), StringComparison.Ordinal);
        Assert.Contains("PLAN B", vendor.PromptOf("draft"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reply_the_profiles_MaxTokens_cut_says_so()
    {
        // A plan cut by the profile's MaxTokens is unreadable JSON: the warning says why.
        using var logs = new RecordingLoggerFactory();
        var vendor = new Vendor("default") { PlanReply = _ => """{"plans":[{"task":1,"plan":"1. Gather the""" };
        vendor.Provider.BaseConfig = LlmConfig.OnProfile() with { MaxTokens = 5 };

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml, logs);

        Assert.True(output.Succeeded, output.Error);
        Assert.Contains(Warnings(logs), w => w.Contains("MaxTokens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_task_the_plan_leaves_out_runs_without_a_section_and_a_warning_names_it()
    {
        using var logs = new RecordingLoggerFactory();
        var vendor = new Vendor("default") { PlanReply = _ => """{"plans":[{"task":1,"plan":"PLAN A"},{"task":7,"plan":"PLAN Z"}]}""" };

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml, logs);

        Assert.True(output.Succeeded, output.Error);
        Assert.Contains("PLAN A", vendor.PromptOf("outline"), StringComparison.Ordinal);
        Assert.DoesNotContain(PlanSection, vendor.PromptOf("draft"), StringComparison.Ordinal);
        Assert.Contains(Warnings(logs), w => w.Contains("task 2", StringComparison.Ordinal) && w.Contains(Draft, StringComparison.Ordinal));
        // A number the crew does not have is ignored, and said.
        Assert.Contains(Warnings(logs), w => w.Contains("task 7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_provider_that_replays_its_prompt_is_not_asked_for_a_plan()
    {
        // The echo provider of a host without an Llm section: the plan it "returned" was the prompt,
        // every task was declared missing and the run failed, while the rest of a keyless run went
        // to the end.
        using var logs = new RecordingLoggerFactory();
        var vendor = new Vendor("undefined");
        vendor.Provider.Capabilities = new LlmProviderCapabilities { ReplaysPrompt = true };

        var output = await RunYamlAsync(vendor.Provider, PlannedYaml, logs);

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(0, vendor.Provider.GenerateCallCount);
        Assert.Equal(["task:outline", "task:draft"], vendor.Calls);
        Assert.Contains(Warnings(logs), w => w.Contains("planning", StringComparison.OrdinalIgnoreCase)
            && w.Contains("orkeon init", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelling_the_run_while_it_plans_stops_it_at_once()
    {
        // The planning call took no token: after a Ctrl+C it ran until the provider's own timeout
        // (600 s for a reasoning model).
        var vendor = new Vendor("default");
        var planning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vendor.Provider.SetGenerateAsyncFunc(async (_, _, token) =>
        {
            planning.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new LlmResponse { Content = "{}" };
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = RunCancellableYamlAsync(vendor.Provider, PlannedYaml, null, cts.Token);
        await planning.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await cts.CancelAsync();
        var output = await run.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.False(output.Succeeded);
        Assert.Empty(vendor.TaskPrompts);
    }
}
