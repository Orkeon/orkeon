using Orkeon.Domain.Common;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Application.Flow;

/// <summary>
/// Fluent builder for creating IFlowDefinition instances.
/// </summary>
public class FlowDefinitionBuilder
{
    private FlowId _id = FlowId.Create();
    private string _name = string.Empty;
    private string _description = string.Empty;
    private FlowType _type = FlowType.Sequential;
    private TimeSpan? _timeout;
    private int _maxRetries = AgentDefaults.MaxRetryLimit;
    private readonly List<FlowStep> _steps = [];
    private readonly Dictionary<string, FlowStepId> _stepNameToId = [];

    /// <summary>
    /// Sets the flow identifier from a ULID string.
    /// </summary>
    public FlowDefinitionBuilder WithId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _id = FlowId.Parse(id);
        return this;
    }

    /// <summary>
    /// Sets the flow name.
    /// </summary>
    public FlowDefinitionBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the flow description.
    /// </summary>
    public FlowDefinitionBuilder WithDescription(string desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        _description = desc;
        return this;
    }

    /// <summary>
    /// Sets the flow type to Sequential.
    /// </summary>
    public FlowDefinitionBuilder AsSequential()
    {
        _type = FlowType.Sequential;
        return this;
    }

    /// <summary>
    /// Sets the flow type to Parallel.
    /// </summary>
    public FlowDefinitionBuilder AsParallel()
    {
        _type = FlowType.Parallel;
        return this;
    }

    /// <summary>
    /// Sets the flow type to Conditional.
    /// </summary>
    public FlowDefinitionBuilder AsConditional()
    {
        _type = FlowType.Conditional;
        return this;
    }

    /// <summary>
    /// Sets the flow type to Loop.
    /// </summary>
    public FlowDefinitionBuilder AsLoop()
    {
        _type = FlowType.Loop;
        return this;
    }

    /// <summary>
    /// Sets the flow-level timeout.
    /// </summary>
    public FlowDefinitionBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of retries for the flow.
    /// </summary>
    public FlowDefinitionBuilder WithMaxRetries(int retries)
    {
        _maxRetries = retries;
        return this;
    }

    /// <summary>
    /// Resolves a step name to its <see cref="FlowStepId"/>, creating one if needed.
    /// </summary>
    internal FlowStepId ResolveStepId(string stepName)
    {
        if (!_stepNameToId.TryGetValue(stepName, out var id))
        {
            id = FlowStepId.Create();
            _stepNameToId[stepName] = id;
        }
        return id;
    }

    /// <summary>
    /// Adds a generic step to the flow.
    /// </summary>
    public FlowDefinitionBuilder AddStep(string name, string type, Action<FlowStepBuilder>? configure = null)
    {
        var stepId = ResolveStepId(name);
        var builder = new FlowStepBuilder(stepId, name, type, this);

        configure?.Invoke(builder);

        _steps.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Adds a crew execution step to the flow.
    /// </summary>
    public FlowDefinitionBuilder AddCrewStep(string name, string crewConfig, Action<FlowStepBuilder>? configure = null)
    {
        return AddStep(name, "crew", builder =>
        {
            builder.WithParameter("crew_config", crewConfig);
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Adds an LLM invocation step to the flow.
    /// </summary>
    public FlowDefinitionBuilder AddLlmStep(string name, string promptTemplate, Action<FlowStepBuilder>? configure = null)
    {
        return AddStep(name, "llm", builder =>
        {
            builder.WithParameter("prompt_template", promptTemplate);
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Adds a tool execution step to the flow.
    /// </summary>
    public FlowDefinitionBuilder AddToolStep(string name, string toolName, Action<FlowStepBuilder>? configure = null)
    {
        return AddStep(name, "tool", builder =>
        {
            builder.WithParameter("tool_name", toolName);
            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Builds the flow definition, validating required fields.
    /// </summary>
    public IFlowDefinition Build()
    {
        if (string.IsNullOrWhiteSpace(_name))
            throw new InvalidOperationException("Flow name is required.");

        if (_steps.Count == 0)
            throw new InvalidOperationException("Flow must have at least one step.");

        var config = new FlowConfiguration
        {
            Id = _id,
            Name = _name,
            Type = _type,
            Timeout = _timeout,
            MaxRetries = _maxRetries
        };

        return new InternalFlowDefinition(_id, _name, _description, _type, _steps.ToList(), config);
    }

    /// <summary>
    /// Internal IFlowDefinition implementation created by the builder.
    /// </summary>
    private sealed class InternalFlowDefinition : IFlowDefinition
    {
        public FlowId Id { get; }
        public string Name { get; }
        public string Description { get; }
        public FlowType Type { get; }
        public IReadOnlyList<FlowStep> Steps { get; }
        public FlowConfiguration Configuration { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="InternalFlowDefinition"/>.
        /// </summary>
        public InternalFlowDefinition(
            FlowId id,
            string name,
            string description,
            FlowType type,
            List<FlowStep> steps,
            FlowConfiguration configuration)
        {
            Id = id;
            Name = name;
            Description = description;
            Type = type;
            Steps = steps.AsReadOnly();
            Configuration = configuration;
        }

        /// <summary>
        /// Validate.
        /// </summary>
        public bool Validate(out IReadOnlyList<string> errors)
        {
            var errorList = new List<string>();
            errors = errorList;

            if (string.IsNullOrWhiteSpace(Name))
                errorList.Add("Flow name is required.");

            if (Steps.Count == 0)
                errorList.Add("Flow must have at least one step.");

            // Check for duplicate step IDs
            var duplicateIds = Steps
                .GroupBy(s => s.Id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var dupId in duplicateIds)
            {
                errorList.Add($"Duplicate step ID: '{dupId}'.");
            }

            // Check for circular dependencies
            if (HasCircularDependencies(out var cycle))
                errorList.Add($"Circular dependency detected: {cycle}");

            return errorList.Count == 0;
        }

        private bool HasCircularDependencies(out string cycle)
        {
            cycle = string.Empty;
            var stepMap = Steps.ToDictionary(s => s.Id);
            var visited = new HashSet<FlowStepId>();
            var inStack = new HashSet<FlowStepId>();

            foreach (var step in Steps)
            {
                if (DetectCycle(step.Id, stepMap, visited, inStack, [], out cycle))
                    return true;
            }

            return false;
        }

        private static bool DetectCycle(
            FlowStepId stepId,
            Dictionary<FlowStepId, FlowStep> stepMap,
            HashSet<FlowStepId> visited,
            HashSet<FlowStepId> inStack,
            List<FlowStepId> path,
            out string cycle)
        {
            cycle = string.Empty;

            if (inStack.Contains(stepId))
            {
                path.Add(stepId);
                cycle = string.Join(" -> ", path.Select(id => id.ToString()));
                return true;
            }

            if (visited.Contains(stepId))
                return false;

            visited.Add(stepId);
            inStack.Add(stepId);
            path.Add(stepId);

            if (stepMap.TryGetValue(stepId, out var step))
            {
                foreach (var dep in step.Dependencies)
                {
                    if (DetectCycle(dep, stepMap, visited, inStack, [.. path], out cycle))
                        return true;
                }
            }

            inStack.Remove(stepId);
            return false;
        }
    }
}

/// <summary>
/// Builder for configuring individual flow steps.
/// </summary>
public class FlowStepBuilder
{
    private readonly FlowStepId _id;
    private readonly string _name;
    private readonly string _type;
    private readonly FlowDefinitionBuilder _parent;
    private readonly List<string> _dependencyNames = [];
    private TimeSpan? _timeout;
    private int _maxRetries = AgentDefaults.MaxRetryLimit;
    private FlowStepParameters _parameters = FlowStepParameters.Empty;

    internal FlowStepBuilder(FlowStepId id, string name, string type, FlowDefinitionBuilder parent)
    {
        _id = id;
        _name = name;
        _type = type;
        _parent = parent;
    }

    /// <summary>
    /// Adds dependencies on other steps by name.
    /// </summary>
    public FlowStepBuilder DependsOn(params string[] stepNames)
    {
        _dependencyNames.AddRange(stepNames);
        return this;
    }

    /// <summary>
    /// Sets the step timeout.
    /// </summary>
    public FlowStepBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// Sets the maximum retries for this step.
    /// </summary>
    public FlowStepBuilder WithMaxRetries(int retries)
    {
        _maxRetries = retries;
        return this;
    }

    /// <summary>
    /// Adds a parameter to this step.
    /// </summary>
    public FlowStepBuilder WithParameter(string key, object value)
    {
        _parameters = _parameters.Set(key, value);
        return this;
    }

    /// <summary>
    /// Builds the configured <see cref="FlowStep"/> record.
    /// </summary>
    internal FlowStep Build()
    {
        // Resolve dependency names to FlowStepIds via the parent builder
        var resolvedDeps = _dependencyNames
            .Select(name => _parent.ResolveStepId(name))
            .ToList();

        return new FlowStep
        {
            Id = _id,
            Name = _name,
            Type = _type,
            Dependencies = resolvedDeps.Count > 0
                ? resolvedDeps.AsReadOnly()
                : Array.Empty<FlowStepId>(),
            Timeout = _timeout,
            MaxRetries = _maxRetries,
            Parameters = _parameters
        };
    }
}
