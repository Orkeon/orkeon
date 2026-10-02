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
    private readonly List<Orkeon.Domain.Tools.IBaseTool> _tools = [];
    private readonly Dictionary<string, object> _context = [];
    private AgentId? _assignedAgent;
    private bool _asyncExecution;
    private JsonSchema? _outputJson;
    private Type? _outputPydantic;
    private string? _outputFile;
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

    /// <summary>
    /// Adds a tool the agent holds while it runs this task, on top of its own tools (YAML
    /// <c>tools:</c> on a task).
    /// </summary>
    public CrewTaskBuilder WithTool(Orkeon.Domain.Tools.IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools.Add(tool);
        return this;
    }

    /// <summary>Adds several tools the agent holds while it runs this task, on top of its own.</summary>
    public CrewTaskBuilder WithTools(IEnumerable<Orkeon.Domain.Tools.IBaseTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        foreach (var tool in tools)
            WithTool(tool);
        return this;
    }

    /// <summary>Adds a context key-value pair.</summary>
    public CrewTaskBuilder WithContext(string key, object value)
    {
        _context[key] = value;
        return this;
    }

    /// <summary>
    /// Asks for asynchronous execution: in a sequential crew the task runs alongside the tasks after
    /// it, and a task that depends on it waits for it (GAP-22). A parallel crew accepts it without an
    /// effect of its own; the other modes refuse it when the crew is built.
    /// </summary>
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
            HumanInput = _humanInput
        };

        var task = CrewTask.Create(_description, _expectedOutput, _priority, outputOptions);

        foreach (var dep in _dependencies)
        {
            task.AddDependency(dep);
        }

        foreach (var tool in _tools)
        {
            task.AddTool(tool);
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
