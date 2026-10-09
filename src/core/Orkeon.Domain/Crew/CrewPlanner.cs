using System.Globalization;
using System.Text;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Crew;
using Orkeon.Domain.Crew.Interfaces;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Crew;

/// <summary>An agent as the crew's planner sees it (GAP-31).</summary>
/// <param name="Role">The agent's role.</param>
/// <param name="Goal">The agent's goal.</param>
/// <param name="Tools">The names of the tools the agent holds for the task — its own and the task's.</param>
public sealed record PlanningAgent(string Role, string Goal, IReadOnlyList<string> Tools);

/// <summary>A task as the crew's planner sees it (GAP-31).</summary>
/// <param name="Id">The task. The planner is never shown it: it is told the task's number, and answers by number.</param>
/// <param name="Description">The task's description, with the run's variables in it, as the agent reads it.</param>
/// <param name="ExpectedOutput">The task's expected output, with the run's variables in it.</param>
/// <param name="Dependencies">The tasks it depends on; the planner is shown their numbers.</param>
/// <param name="Agent">
/// The agent the crew names for the task, or null when it names none. The planner is shown it where
/// the mode runs a task on the agent it names; elsewhere it is told what the mode does instead.
/// </param>
public sealed record PlanningTask(
    TaskId Id,
    string Description,
    string ExpectedOutput,
    IReadOnlyList<TaskId> Dependencies,
    PlanningAgent? Agent);

/// <summary>What the crew's planner is shown of a run (GAP-31).</summary>
/// <param name="Goal">The crew's goal.</param>
/// <param name="Process">The crew's process: what it does with the order of the tasks and with who runs them.</param>
/// <param name="Tasks">The tasks the run will take, in the order it takes them: task 1 first.</param>
/// <param name="Agents">The crew's agents that run tasks, listed once for the tasks whose agent the mode chooses.</param>
/// <param name="Variables">The run's variables, <c>initial_context</c> included.</param>
public sealed record PlanningContext(
    string Goal,
    ProcessType Process,
    IReadOnlyList<PlanningTask> Tasks,
    IReadOnlyList<PlanningAgent> Agents,
    IReadOnlyDictionary<string, string> Variables);

/// <summary>What planning produced (GAP-31).</summary>
/// <param name="Plan">One step-by-step plan per task; empty when no reply could be read.</param>
/// <param name="Warnings">What the run should be told, one sentence each: a task left without a plan, an entry ignored, a reply that could not be read.</param>
public sealed record CrewPlanningOutcome(ExecutionPlan Plan, IReadOnlyList<string> Warnings);

/// <summary>
/// The crew's planner (<c>planning: true</c>, GAP-31): before the run, it shows a model the crew — its
/// goal, its run's variables, each task by number with its description, expected output,
/// dependencies and agent, bounded by <see cref="PlanningDefaults"/> — and has it write one
/// step-by-step plan per task. The plan changes neither the order of the tasks nor who runs them:
/// each task reads its own plan in its prompt.
/// </summary>
/// <remarks>
/// The plan is advice. A reply that cannot be read is asked for once more, with what could not be read,
/// then the crew runs without a plan; a task the reply leaves out runs without one; each is a warning in
/// <see cref="CrewPlanningOutcome.Warnings"/>. Only a provider that fails — a refused key, an
/// unreachable endpoint — fails the planning, with the provider's reason. Parsing belongs to
/// <see cref="IExecutionPlanParser"/>, which keeps the Domain free from serialization concerns.
/// </remarks>
public sealed class CrewPlanner
{
    /// <summary>The first words of every planning prompt.</summary>
    public const string PromptOpening = "You are the planner of an agent crew.";

    private const int MaxAttempts = 2;
    private const int MaxTaskNameChars = 60;
    private const string PlanSchemaName = "crew_plan";

    /// <summary>The reply's shape, for a provider that validates a JSON Schema.</summary>
    private const string PlanSchema =
        """{"type":"object","properties":{"plans":{"type":"array","items":{"type":"object","properties":{"task":{"type":"integer"},"plan":{"type":"string"}},"required":["task","plan"],"additionalProperties":false}}},"required":["plans"],"additionalProperties":false}""";

    private readonly ILlmProvider _planningLlm;
    private readonly IExecutionPlanParser _parser;

    private CrewPlanner(ILlmProvider planningLlm, IExecutionPlanParser parser)
    {
        ArgumentNullException.ThrowIfNull(planningLlm);
        _planningLlm = planningLlm;
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    /// <summary>Creates a planner.</summary>
    /// <param name="planningLlm">The provider the crew plans on.</param>
    /// <param name="parser">Reads the planner's replies.</param>
    public static CrewPlanner Create(ILlmProvider planningLlm, IExecutionPlanParser parser)
        => new(planningLlm, parser);

    /// <summary>Plans the run <paramref name="context"/> describes: one step-by-step plan per task.</summary>
    /// <param name="context">What the planner is shown of the run.</param>
    /// <param name="cancellationToken">The run's token: cancelling the run cancels the planning call.</param>
    /// <returns>The plan, and what the run should be warned of.</returns>
    /// <exception cref="InvalidOperationException">The planning provider failed; the message carries its reason.</exception>
    public Task<CrewPlanningOutcome> CreatePlanAsync(PlanningContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreatePlanCoreAsync(context, cancellationToken);
    }

    private async Task<CrewPlanningOutcome> CreatePlanCoreAsync(PlanningContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Tasks.Count == 0)
            return new CrewPlanningOutcome(ExecutionPlan.Empty, []);

        var numbered = context.Tasks.Select(task => task.Id).ToList();
        var prompt = BuildPrompt(context);
        var config = CallConfig();
        string? unreadable = null;
        LlmResponse? lastReply = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = unreadable is null ? prompt : prompt + RetryNote(unreadable);
            var reply = await _planningLlm.GenerateAsync(request, config, cancellationToken).ConfigureAwait(false);

            // A failed call is not an unreadable plan: its content is empty and the reason is in the
            // response. A refused key or an unreachable endpoint would fail every task too, so it
            // fails the run here, before the first task, saying why (GAP-29).
            if (reply.Error is { } failure)
                throw new InvalidOperationException($"Failed to create execution plan: the planning LLM call failed — {failure}");

            var reading = _parser.Read(reply.Content, numbered);
            if (reading.Plan is { } plan)
                return new CrewPlanningOutcome(plan, [.. reading.Warnings, .. TasksWithoutAPlan(context, plan)]);

            unreadable = reading.Error;
            lastReply = reply;
        }

        return new CrewPlanningOutcome(ExecutionPlan.Empty, [UnreadableReply(unreadable!, lastReply!)]);
    }

    /// <summary>
    /// On the planning provider's own model (GAP-18) and connection (GAP-29), at a low temperature,
    /// and with the reply's shape constrained as far as the provider can: a JSON Schema, else a JSON
    /// object, else the prompt alone. No <c>MaxTokens</c>: a ceiling above the model's own would fail
    /// the call, and the profile's own applies.
    /// </summary>
    private LlmConfig CallConfig()
    {
        var config = LlmConfig.OnProfile() with { Temperature = PlanningDefaults.Temperature };
        return _planningLlm.Capabilities.ResponseFormat switch
        {
            ResponseFormatSupport.JsonSchema => config with { ResponseFormat = LlmResponseFormat.JsonSchema(PlanSchemaName, PlanSchema) },
            ResponseFormatSupport.JsonObject => config with { ResponseFormat = LlmResponseFormat.JsonObject() },
            _ => config,
        };
    }

    private static string RetryNote(string error) =>
        $"\n\nYour previous reply could not be read: {error}. Reply again with the JSON object only, in the shape above.";

    /// <summary>A task the reply leaves out runs without a plan: the warning names it, by number and by its first words.</summary>
    private static IEnumerable<string> TasksWithoutAPlan(PlanningContext context, ExecutionPlan plan)
    {
        for (var index = 0; index < context.Tasks.Count; index++)
        {
            var task = context.Tasks[index];
            if (plan.InstructionsFor(task.Id) is null)
            {
                yield return string.Create(CultureInfo.InvariantCulture,
                    $"the planner wrote no plan for task {index + 1} ({TaskName(task.Description)}); it runs without one.");
            }
        }
    }

    /// <summary>
    /// Two replies that could not be read: the crew runs without a plan. A reply that stopped at the
    /// provider's <c>MaxTokens</c> is a plan cut short, which is what the warning then says.
    /// </summary>
    private string UnreadableReply(string error, LlmResponse lastReply)
    {
        var warning = $"the planner's reply could not be read, twice ({error}); the crew runs without a plan.";
        if (_planningLlm.BaseConfig?.MaxTokens is int ceiling && lastReply.CompletionTokens is int completion && completion >= ceiling)
        {
            warning += string.Create(CultureInfo.InvariantCulture,
                $" The reply stopped at the provider's MaxTokens ({ceiling}), which cut the plan short: raise Llm:MaxTokens for a crew of this size.");
        }

        return warning;
    }

    /// <summary>The planning prompt. Written with <c>\n</c>, never the platform's line ending: one prompt on every OS.</summary>
    private static string BuildPrompt(PlanningContext context)
    {
        var prompt = new StringBuilder();
        Line(prompt, PromptOpening + " Before the crew runs, you write a step-by-step plan for each of its tasks; "
            + "the agent that runs a task reads its plan with the task.");
        Line(prompt);
        Line(prompt, $"Crew goal: {context.Goal}");
        Line(prompt, $"How the crew runs: {HowTheCrewRuns(context.Process)}");
        AppendVariables(prompt, context.Variables);

        Line(prompt);
        Line(prompt, "Tasks, numbered in the order they run:");
        var numbers = new Dictionary<TaskId, int>();
        for (var index = 0; index < context.Tasks.Count; index++)
            numbers.TryAdd(context.Tasks[index].Id, index + 1);

        var agentChosenByTheMode = false;
        for (var index = 0; index < context.Tasks.Count; index++)
        {
            var task = context.Tasks[index];
            Line(prompt);
            Line(prompt, string.Create(CultureInfo.InvariantCulture, $"Task {index + 1}"));
            Line(prompt, $"Description: {Bound(task.Description, PlanningDefaults.MaxTaskDescriptionChars)}");
            Line(prompt, $"Expected output: {Bound(task.ExpectedOutput, PlanningDefaults.MaxExpectedOutputChars)}");
            Line(prompt, $"Depends on: {DependsOn(task, numbers)}");
            if (HonoursTaskAgent(context.Process) && task.Agent is { } agent)
            {
                Line(prompt, $"Agent: {Sheet(agent)}");
            }
            else
            {
                Line(prompt, $"Agent: {AgentTheModeChooses(context.Process)}");
                agentChosenByTheMode = true;
            }
        }

        if (agentChosenByTheMode && context.Agents.Count > 0)
        {
            Line(prompt);
            Line(prompt, "The crew's agents:");
            foreach (var agent in context.Agents)
                Line(prompt, $"- {Sheet(agent)}");
        }

        Line(prompt);
        Line(prompt, string.Create(CultureInfo.InvariantCulture,
            $"Write one plan per task: numbered steps, {PlanningDefaults.MaxPlanSteps} at most, each one concrete, naming the tools to use where a tool helps. ")
            + "A plan changes neither the agent that runs its task nor the order of the tasks.");
        Line(prompt, "Reply with a JSON object only, of this shape:");
        Line(prompt, """{"plans": [{"task": 1, "plan": "1. ...\n2. ..."}]}""");
        prompt.Append(string.Create(CultureInfo.InvariantCulture,
            $"\"task\" is the task's number above; write exactly one plan for each of the {context.Tasks.Count} task(s)."));
        return prompt.ToString();
    }

    /// <summary>
    /// Whether the mode runs a task on the agent the task names: Sequential, Parallel and Graph do;
    /// Hierarchical and Autonomous hand every task to the agent their manager chooses; in Consensual
    /// every agent answers every task.
    /// </summary>
    private static bool HonoursTaskAgent(ProcessType process) =>
        process == ProcessType.Sequential || process == ProcessType.Parallel || process == ProcessType.Graph;

    private static string HowTheCrewRuns(ProcessType process) => process.Value switch
    {
        "Parallel" => "tasks that do not depend on each other run at the same time; a task starts once the tasks it depends on are done, and reads their results.",
        "Hierarchical" => "a manager hands each task, in the order below, to the agent of its choice, then reviews the result.",
        "Autonomous" => "a manager hands each task, in the order below, to the agent of its choice; a task that fails is handed to another agent.",
        "Consensual" => "every agent answers each task, in the order below, then the agents vote for the best answer.",
        "Graph" => "the tasks run one after another, in the order below, and a task that fails may be run again; each task reads the results of the tasks before it.",
        _ => "the tasks run one after another, in the order below; each task reads the results of the tasks before it.",
    };

    private static string AgentTheModeChooses(ProcessType process) => process.Value switch
    {
        "Hierarchical" or "Autonomous" => "assigned by the manager at run time, among the crew's agents below",
        "Consensual" => "every agent answers, among the crew's agents below",
        _ => "chosen at run time among the crew's agents below",
    };

    private static void AppendVariables(StringBuilder prompt, IReadOnlyDictionary<string, string> variables)
    {
        if (variables.Count == 0)
            return;

        Line(prompt);
        Line(prompt, "Run inputs:");
        var block = string.Join('\n', variables.Select(variable => $"- {variable.Key}: {variable.Value}"));
        Line(prompt, Bound(block, PlanningDefaults.MaxVariablesChars));
    }

    private static string DependsOn(PlanningTask task, Dictionary<TaskId, int> numbers)
    {
        var dependencies = task.Dependencies
            .Where(numbers.ContainsKey)
            .Select(dependency => numbers[dependency])
            .Distinct()
            .Order()
            .Select(number => number.ToString(CultureInfo.InvariantCulture))
            .ToList();

        return dependencies.Count switch
        {
            0 => "none",
            1 => $"task {dependencies[0]}",
            _ => $"tasks {string.Join(", ", dependencies)}",
        };
    }

    /// <summary><c>Writer (goal: Write the report; tools: file_write, web_search)</c>, bounded.</summary>
    private static string Sheet(PlanningAgent agent) =>
        $"{agent.Role} (goal: {Bound(agent.Goal, PlanningDefaults.MaxAgentGoalChars)}; tools: {ToolList(agent)})";

    /// <summary>The agent's tool names, the first few, then how many more it has; <c>none</c> without any.</summary>
    private static string ToolList(PlanningAgent agent)
    {
        if (agent.Tools.Count == 0)
            return "none";

        var listed = string.Join(", ", agent.Tools.Take(PlanningDefaults.MaxToolNamesPerAgent));
        if (agent.Tools.Count <= PlanningDefaults.MaxToolNamesPerAgent)
            return listed;

        return listed + string.Create(CultureInfo.InvariantCulture, $" (+{agent.Tools.Count - PlanningDefaults.MaxToolNamesPerAgent} more)");
    }

    private static string Bound(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max), PlanningDefaults.TruncationMarker);

    /// <summary>A task's description on one line, cut to its first words: how a warning names the task.</summary>
    private static string TaskName(string description)
    {
        var line = string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxTaskNameChars ? line : string.Concat(line.AsSpan(0, MaxTaskNameChars), "…");
    }

    private static void Line(StringBuilder prompt, string text = "") => prompt.Append(text).Append('\n');
}
