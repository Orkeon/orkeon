using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Interfaces;

namespace Orkeon.Infrastructure.Parsing;

/// <summary>
/// Parses LLM responses into ExecutionPlan objects using a hybrid strategy:
/// tries JSON parsing first, then falls back to text-based "KEY: value" parsing.
/// This implementation was extracted from <c>CrewPlanner</c> (Domain layer)
/// to respect Clean Architecture — the Domain layer should not depend on
/// <see cref="System.Text.Json"/>.
/// </summary>
public sealed partial class ExecutionPlanParser : IExecutionPlanParser
{
    private static readonly string[] s_taskBlockSeparator = ["---"];

    private readonly ILogger<ExecutionPlanParser>? _logger;

    /// <inheritdoc />
    public string ParserName => "HybridExecutionPlanParser";

    /// <summary>
    /// Creates a new instance of <see cref="ExecutionPlanParser"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public ExecutionPlanParser(ILogger<ExecutionPlanParser>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ExecutionPlan?> ParseAsync(
        string llmResponse,
        IReadOnlyList<TaskId> tasks,
        IReadOnlyList<AgentId> agents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(llmResponse);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(agents);

        cancellationToken.ThrowIfCancellationRequested();

        // Try JSON parsing first, fall back to text-based parsing
        var plan = TryParseFromJson(llmResponse, tasks, agents);
        if (plan != null)
        {
            if (_logger is not null)
                LogParsedFromJson(_logger);
            return Task.FromResult<ExecutionPlan?>(plan);
        }

        if (_logger is not null)
            LogJsonParseFallback(_logger);
        plan = ParseFromText(llmResponse, tasks, agents);

        return Task.FromResult<ExecutionPlan?>(plan);
    }

    // =====================================================================
    // JSON parsing (extracted from CrewPlanner.TryParseExecutionPlanFromJson)
    // =====================================================================

    private static ExecutionPlan? TryParseFromJson(
        string llmResponse,
        IReadOnlyList<TaskId> tasks,
        IReadOnlyList<AgentId> agents)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(llmResponse);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("tasks", out var tasksElement) ||
                tasksElement.ValueKind != JsonValueKind.Array)
                return null;

            var plan = ExecutionPlan.Create();

            foreach (var taskElement in tasksElement.EnumerateArray())
            {
                plan = ParseJsonTaskElement(taskElement, tasks, agents, plan);
            }

            return plan.Tasks.Count > 0 ? plan : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExecutionPlan ParseJsonTaskElement(
        JsonElement taskElement,
        IReadOnlyList<TaskId> tasks,
        IReadOnlyList<AgentId> agents,
        ExecutionPlan plan)
    {
        var taskId = ExtractTaskId(taskElement, tasks);
        if (taskId == null)
            return plan;

        if (!taskElement.TryGetProperty("order", out var orderProp) ||
            !orderProp.TryGetInt32(out var order))
            return plan;

        int? parallelGroup = ExtractParallelGroup(taskElement);
        var dependencies = ExtractDependencies(taskElement, tasks);
        string? instructions = taskElement.TryGetProperty("instructions", out var instProp)
            && instProp.ValueKind == JsonValueKind.String
            ? instProp.GetString()
            : null;

        var plannedTask = PlannedTask.Create(taskId, order, parallelGroup, dependencies, instructions);
        plan = plan.WithTask(plannedTask);

        plan = TryAssignAgentFromJson(taskElement, agents, plan, taskId);
        return plan;
    }

    private static TaskId? ExtractTaskId(
        JsonElement taskElement,
        IReadOnlyList<TaskId> tasks)
    {
        // LLM output is untrusted: a non-string "task" must skip the entry,
        // not throw out of GetString().
        if (!taskElement.TryGetProperty("task", out var taskIdProp) ||
            taskIdProp.ValueKind != JsonValueKind.String)
            return null;

        var taskIdStr = taskIdProp.GetString();
        if (taskIdStr == null)
            return null;

        return FindById(tasks, taskIdStr);
    }

    private static int? ExtractParallelGroup(JsonElement taskElement)
    {
        if (taskElement.TryGetProperty("parallel_group", out var groupProp) &&
            groupProp.TryGetInt32(out var group))
            return group;

        return null;
    }

    private static List<TaskId> ExtractDependencies(
        JsonElement taskElement,
        IReadOnlyList<TaskId> tasks)
    {
        var dependencies = new List<TaskId>();

        if (!taskElement.TryGetProperty("dependencies", out var depsProp) ||
            depsProp.ValueKind != JsonValueKind.Array)
            return dependencies;

        foreach (var dep in depsProp.EnumerateArray())
        {
            // Skip non-string entries instead of throwing on GetString().
            if (dep.ValueKind != JsonValueKind.String)
                continue;

            var depStr = dep.GetString();
            if (depStr == null)
                continue;

            var depId = FindById(tasks, depStr);
            if (depId != null)
                dependencies.Add(depId);
        }

        return dependencies;
    }

    private static ExecutionPlan TryAssignAgentFromJson(
        JsonElement taskElement,
        IReadOnlyList<AgentId> agents,
        ExecutionPlan plan,
        TaskId taskId)
    {
        if (!taskElement.TryGetProperty("agent", out var agentProp) ||
            agentProp.ValueKind != JsonValueKind.String)
            return plan;

        var agentIdStr = agentProp.GetString();
        if (agentIdStr == null)
            return plan;

        var agentId = FindById(agents, agentIdStr);
        if (agentId != null)
            return plan.WithAgentAssignment(taskId, agentId);

        return plan;
    }

    // =====================================================================
    // Text-based parsing (extracted from CrewPlanner.ParseExecutionPlanFromText)
    // =====================================================================

    private static ExecutionPlan ParseFromText(
        string llmResponse,
        IReadOnlyList<TaskId> tasks,
        IReadOnlyList<AgentId> agents)
    {
        var plan = ExecutionPlan.Create();
        var taskBlocks = llmResponse.Split(s_taskBlockSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in taskBlocks)
        {
            plan = ParseTextTaskBlock(block, tasks, agents, plan);
        }

        return plan;
    }

    private static ExecutionPlan ParseTextTaskBlock(
        string block,
        IReadOnlyList<TaskId> tasks,
        IReadOnlyList<AgentId> agents,
        ExecutionPlan plan)
    {
        var taskData = ParseKeyValueBlock(block);

        if (!taskData.TryGetValue("TASK", out var taskIdStr) ||
            !taskData.TryGetValue("ORDER", out var orderStr) ||
            !int.TryParse(orderStr, out var order))
            return plan;

        var taskId = FindById(tasks, taskIdStr);
        if (taskId == null)
            return plan;

        var parallelGroup = ExtractParallelGroupFromText(taskData);
        var dependencies = ExtractDependenciesFromText(taskData, tasks);
        var instructions = taskData.TryGetValue("INSTRUCTIONS", out var inst) ? inst : null;

        var plannedTask = PlannedTask.Create(taskId, order, parallelGroup, dependencies, instructions);
        plan = plan.WithTask(plannedTask);

        plan = TryAssignAgentFromText(taskData, agents, plan, taskId);
        return plan;
    }

    private static int? ExtractParallelGroupFromText(Dictionary<string, string> taskData)
    {
        if (taskData.TryGetValue("PARALLEL_GROUP", out var groupStr) &&
            int.TryParse(groupStr, out var group))
            return group;

        return null;
    }

    private static List<TaskId> ExtractDependenciesFromText(
        Dictionary<string, string> taskData,
        IReadOnlyList<TaskId> tasks)
    {
        if (!taskData.TryGetValue("DEPENDENCIES", out var depsStr) || string.IsNullOrWhiteSpace(depsStr))
            return [];

        return depsStr.Split(',')
            .Select(d => FindById(tasks, d.Trim()))
            .Where(t => t != null)
            .Cast<TaskId>()
            .ToList();
    }

    private static ExecutionPlan TryAssignAgentFromText(
        Dictionary<string, string> taskData,
        IReadOnlyList<AgentId> agents,
        ExecutionPlan plan,
        TaskId taskId)
    {
        if (!taskData.TryGetValue("AGENT", out var agentIdStr))
            return plan;

        var agentId = FindById(agents, agentIdStr);
        if (agentId != null)
            return plan.WithAgentAssignment(taskId, agentId);

        return plan;
    }

    private static Dictionary<string, string> ParseKeyValueBlock(string block)
    {
        var result = new Dictionary<string, string>();
        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2)
                result[parts[0].Trim()] = parts[1].Trim();
        }

        return result;
    }

    // =====================================================================
    // Shared helpers
    // =====================================================================

    private static T? FindById<T>(IReadOnlyList<T> items, string idStr) where T : class, IEntityId
        => items.FirstOrDefault(item => item.Value.ToString().Equals(idStr, StringComparison.Ordinal));

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Successfully parsed execution plan from JSON")]
    static partial void LogParsedFromJson(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "JSON parsing failed, falling back to text-based parsing")]
    static partial void LogJsonParseFallback(ILogger logger);
}
