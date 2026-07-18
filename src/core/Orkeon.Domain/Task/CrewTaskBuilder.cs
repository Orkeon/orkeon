using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Domain.Task;

/// <summary>
/// Fluent builder for creating <see cref="CrewTask"/> instances.
/// Wraps <see cref="CrewTask.Create"/> with a discoverable, chainable API.
/// </summary>
public sealed class CrewTaskBuilder
{
    private TaskDescription? _description;
    private ExpectedOutput? _expectedOutput;
    private TaskPriority _priority = TaskPriority.Normal;
    private readonly List<TaskId> _dependencies = [];
    private readonly List<ToolId> _requiredTools = [];
    private readonly Dictionary<string, object> _context = [];
    private AgentId? _assignedAgent;
    private bool _asyncExecution;
    private JsonSchema? _outputJson;
    private Type? _outputPydantic;
    private string? _outputFile;
    private ITaskCallback? _callback;
    private bool _humanInput;
    private LlmConfigOverride? _llmOverride;
    private Orkeon.Domain.Agent.GuardrailsConfig? _guardrails;

    /// <summary>Sets the task description from a string value.</summary>
    public CrewTaskBuilder Description(string description)
    {
        _description = TaskDescription.From(description);
        return this;
    }

    /// <summary>Sets the task description from a <see cref="TaskDescription"/> value object.</summary>
    public CrewTaskBuilder Description(TaskDescription description)
    {
        _description = description;
        return this;
    }

    /// <summary>Sets the expected output for the task from a string value.</summary>
    public CrewTaskBuilder ExpectedOutput(string expectedOutput)
    {
        _expectedOutput = Task.ValueObjects.ExpectedOutput.From(expectedOutput);
        return this;
    }

    /// <summary>Sets the expected output for the task from an <see cref="Task.ValueObjects.ExpectedOutput"/> value object.</summary>
    public CrewTaskBuilder ExpectedOutput(ExpectedOutput expectedOutput)
    {
        _expectedOutput = expectedOutput;
        return this;
    }

    /// <summary>Sets the task priority.</summary>
    public CrewTaskBuilder Priority(TaskPriority priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>Adds a dependency on another task.</summary>
    public CrewTaskBuilder DependsOn(CrewTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _dependencies.Add(task.Id);
        return this;
    }

    /// <summary>Adds a dependency on a task by its identifier.</summary>
    public CrewTaskBuilder DependsOn(TaskId taskId)
    {
        _dependencies.Add(taskId);
        return this;
    }

    /// <summary>Adds dependencies on multiple tasks.</summary>
    public CrewTaskBuilder DependsOn(params CrewTask[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        foreach (var task in tasks)
        {
            _dependencies.Add(task.Id);
        }
        return this;
    }

    /// <summary>Adds a required tool by its identifier.</summary>
    public CrewTaskBuilder RequiresTool(ToolId toolId)
    {
        _requiredTools.Add(toolId);
        return this;
    }

    /// <summary>Adds a context key-value pair.</summary>
    public CrewTaskBuilder WithContext(string key, object value)
    {
        _context[key] = value;
        return this;
    }

    /// <summary>Enables or disables asynchronous execution.</summary>
    public CrewTaskBuilder Async(bool asyncExecution = true)
    {
        _asyncExecution = asyncExecution;
        return this;
    }

    /// <summary>Sets the output file path.</summary>
    public CrewTaskBuilder OutputFile(string outputFile)
    {
        _outputFile = outputFile;
        return this;
    }

    /// <summary>Sets the JSON schema for output validation.</summary>
    public CrewTaskBuilder OutputJson(JsonSchema outputJson)
    {
        _outputJson = outputJson;
        return this;
    }

    /// <summary>Sets the output type for structured data.</summary>
    public CrewTaskBuilder OutputPydantic(Type outputPydantic)
    {
        _outputPydantic = outputPydantic;
        return this;
    }

    /// <summary>Enables or disables human input requirement.</summary>
    public CrewTaskBuilder HumanInput(bool humanInput = true)
    {
        _humanInput = humanInput;
        return this;
    }

    /// <summary>Sets the task callback.</summary>
    public CrewTaskBuilder WithCallback(ITaskCallback callback)
    {
        _callback = callback;
        return this;
    }

    /// <summary>Assigns the task to an agent.</summary>
    public CrewTaskBuilder AssignTo(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _assignedAgent = agent.Id;
        return this;
    }

    /// <summary>Assigns the task to an agent by its identifier.</summary>
    public CrewTaskBuilder AssignTo(AgentId agentId)
    {
        _assignedAgent = agentId;
        return this;
    }

    /// <summary>
    /// Assigns a per-task <see cref="LlmConfigOverride"/>. Replaces any previously set override
    /// in this builder (not merged). Pass <c>null</c> to clear.
    /// </summary>
    public CrewTaskBuilder WithLlmOverride(LlmConfigOverride? llmOverride)
    {
        _llmOverride = llmOverride;
        return this;
    }

    /// <summary>
    /// Sugar for the most common case: force a specific output format on this task only.
    /// Merges <c>ResponseFormat</c> into an existing override (if any) rather than replacing.
    /// </summary>
    public CrewTaskBuilder WithResponseFormat(LlmResponseFormat responseFormat)
    {
        _llmOverride = (_llmOverride ?? new LlmConfigOverride()) with { ResponseFormat = responseFormat };
        return this;
    }

    /// <summary>Sugar overload accepting a string (e.g. <c>"json_object"</c>).</summary>
    public CrewTaskBuilder WithResponseFormat(string type)
    {
        return WithResponseFormat(new LlmResponseFormat { Type = type });
    }

    /// <summary>
    /// Assigns per-task guardrails, injected into this task's prompt alongside the agent's. Replaces
    /// any previously set value in this builder (not merged). Pass <c>null</c> to clear.
    /// </summary>
    public CrewTaskBuilder WithGuardrails(Orkeon.Domain.Agent.GuardrailsConfig? guardrails)
    {
        _guardrails = guardrails;
        return this;
    }

    /// <summary>
    /// Builds and returns a new <see cref="CrewTask"/> instance.
    /// </summary>
    /// <exception cref="BuilderValidationException">
    /// Thrown when <see cref="Description(string)"/> or <see cref="ExpectedOutput(ExpectedOutput)"/> have not been set.
    /// </exception>
    public CrewTask Build()
    {
        if (_description is null)
            throw new BuilderValidationException("Task", "Description is required.");

        if (_expectedOutput is null)
            throw new BuilderValidationException("Task", "ExpectedOutput is required.");

        var outputOptions = new TaskOutputOptions
        {
            AsyncExecution = _asyncExecution,
            OutputJson = _outputJson,
            OutputPydantic = _outputPydantic,
            OutputFile = _outputFile,
            Callback = _callback,
            HumanInput = _humanInput
        };

        var task = CrewTask.Create(_description, _expectedOutput, _priority, outputOptions);

        foreach (var dep in _dependencies)
        {
            task.AddDependency(dep);
        }

        foreach (var tool in _requiredTools)
        {
            task.AddRequiredTool(tool);
        }

        foreach (var kvp in _context)
        {
            task.AddContext(kvp.Key, kvp.Value);
        }

        if (_assignedAgent is not null)
        {
            task.AssignTo(_assignedAgent);
        }

        if (_llmOverride is not null)
        {
            task.SetLlmOverride(_llmOverride);
        }

        if (_guardrails is not null)
        {
            task.SetGuardrails(_guardrails);
        }

        return task;
    }
}
