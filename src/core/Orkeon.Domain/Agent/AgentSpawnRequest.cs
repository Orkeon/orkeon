using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Value object representing a request to spawn a new agent dynamically.
/// Contains all information needed to create an agent at runtime.
/// </summary>
public sealed record AgentSpawnRequest
{
    /// <summary>
    /// Gets the role for the spawned agent.
    /// </summary>
    public AgentRole Role { get; }

    /// <summary>
    /// Gets the goal for the spawned agent.
    /// </summary>
    public AgentGoal Goal { get; }

    /// <summary>
    /// Gets the optional backstory for the spawned agent.
    /// </summary>
    public AgentBackstory? Backstory { get; }

    /// <summary>
    /// Gets whether the spawned agent allows delegation.
    /// </summary>
    public bool AllowDelegation { get; }

    /// <summary>
    /// Gets the maximum iterations for the spawned agent.
    /// </summary>
    public int MaxIterations { get; }

    /// <summary>
    /// Gets the maximum requests per minute for the spawned agent.
    /// </summary>
    public int MaxRpm { get; }

    /// <summary>
    /// Gets whether verbose logging is enabled for the spawned agent.
    /// </summary>
    public bool Verbose { get; }

    /// <summary>
    /// Gets the maximum execution time for the spawned agent.
    /// </summary>
    public TimeSpan? MaxExecutionTime { get; }

    /// <summary>
    /// Gets whether caching is enabled for the spawned agent.
    /// </summary>
    public bool CacheEnabled { get; }

    /// <summary>
    /// Gets the tools to assign to the spawned agent.
    /// </summary>
    public IReadOnlyList<ITool> Tools { get; }

    /// <summary>
    /// Gets the parent crew ID that requested this agent spawn.
    /// Used to track agent lineage and scope.
    /// </summary>
    public CrewId ParentCrewId { get; }

    /// <summary>
    /// Gets the ID of the agent that requested this spawn (if applicable).
    /// Used for audit trails and understanding agent hierarchy.
    /// </summary>
    public AgentId? RequestingAgentId { get; }

    /// <summary>
    /// Gets optional metadata for the spawned agent (custom data for extensions).
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Creates a new agent spawn request.
    /// </summary>
    /// <remarks>Prefer <see cref="CreateBuilder"/> for construction; this constructor is used by the builder internally.</remarks>
#pragma warning disable S107 // Methods should not have too many parameters — required for complete initialization; use CreateBuilder() for callers
    public AgentSpawnRequest(
        AgentRole role,
        AgentGoal goal,
        CrewId parentCrewId,
        AgentBackstory? backstory = null,
        bool allowDelegation = false,
        int maxIterations = AgentDefaults.MaxIterations,
        int maxRpm = AgentDefaults.MaxRequestsPerMinute,
        bool verbose = false,
        TimeSpan? maxExecutionTime = null,
        bool cacheEnabled = true,
        IEnumerable<ITool>? tools = null,
        AgentId? requestingAgentId = null,
        IReadOnlyDictionary<string, object>? metadata = null)
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(parentCrewId);

        if (maxIterations <= 0)
            throw new ArgumentException("maxIterations must be positive.", nameof(maxIterations));

        if (maxRpm <= 0)
            throw new ArgumentException("maxRpm must be positive.", nameof(maxRpm));

        Role = role;
        Goal = goal;
        Backstory = backstory;
        AllowDelegation = allowDelegation;
        MaxIterations = maxIterations;
        MaxRpm = maxRpm;
        Verbose = verbose;
        MaxExecutionTime = maxExecutionTime;
        CacheEnabled = cacheEnabled;
        Tools = (tools as IReadOnlyList<ITool>) ?? (tools?.ToList().AsReadOnly() ?? new List<ITool>().AsReadOnly());
        ParentCrewId = parentCrewId;
        RequestingAgentId = requestingAgentId;
        Metadata = metadata ?? new Dictionary<string, object>().AsReadOnly();
    }

    /// <summary>
    /// Creates a builder for constructing spawn requests fluently.
    /// </summary>
    public static AgentSpawnRequestBuilder CreateBuilder(AgentRole role, AgentGoal goal, CrewId parentCrewId)
    {
        return new AgentSpawnRequestBuilder(role, goal, parentCrewId);
    }
}

/// <summary>
/// Fluent builder for AgentSpawnRequest.
/// </summary>
public sealed class AgentSpawnRequestBuilder
{
    private readonly AgentRole _role;
    private readonly AgentGoal _goal;
    private readonly CrewId _parentCrewId;
    private AgentBackstory? _backstory;
    private bool _allowDelegation;
    private int _maxIterations = AgentDefaults.MaxIterations;
    private int _maxRpm = AgentDefaults.MaxRequestsPerMinute;
    private bool _verbose;
    private TimeSpan? _maxExecutionTime;
    private bool _cacheEnabled = true;
    private readonly List<ITool> _tools = [];
    private AgentId? _requestingAgentId;
    private readonly Dictionary<string, object> _metadata = [];

    internal AgentSpawnRequestBuilder(AgentRole role, AgentGoal goal, CrewId parentCrewId)
    {
        _role = role;
        _goal = goal;
        _parentCrewId = parentCrewId;
    }

    /// <summary>Sets the agent backstory.</summary>
    public AgentSpawnRequestBuilder WithBackstory(AgentBackstory? backstory)
    {
        _backstory = backstory;
        return this;
    }

    /// <summary>Sets whether delegation is allowed.</summary>
    public AgentSpawnRequestBuilder AllowDelegation(bool allow = true)
    {
        _allowDelegation = allow;
        return this;
    }

    /// <summary>Sets the maximum iterations.</summary>
    public AgentSpawnRequestBuilder MaxIterations(int max)
    {
        _maxIterations = max;
        return this;
    }

    /// <summary>Sets the maximum requests per minute.</summary>
    public AgentSpawnRequestBuilder MaxRpm(int max)
    {
        _maxRpm = max;
        return this;
    }

    /// <summary>Enables verbose logging.</summary>
    public AgentSpawnRequestBuilder Verbose(bool enable = true)
    {
        _verbose = enable;
        return this;
    }

    /// <summary>Sets the maximum execution time.</summary>
    public AgentSpawnRequestBuilder MaxExecutionTime(TimeSpan? timeout)
    {
        _maxExecutionTime = timeout;
        return this;
    }

    /// <summary>Sets whether caching is enabled.</summary>
    public AgentSpawnRequestBuilder CacheEnabled(bool enabled = true)
    {
        _cacheEnabled = enabled;
        return this;
    }

    /// <summary>Adds a tool to the agent.</summary>
    public AgentSpawnRequestBuilder WithTool(ITool tool)
    {
        _tools.Add(tool);
        return this;
    }

    /// <summary>Adds multiple tools to the agent.</summary>
    public AgentSpawnRequestBuilder WithTools(params ITool[] tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    /// <summary>Adds multiple tools to the agent from an enumerable.</summary>
    public AgentSpawnRequestBuilder WithTools(IEnumerable<ITool> tools)
    {
        _tools.AddRange(tools);
        return this;
    }

    /// <summary>Sets the requesting agent ID for audit trail.</summary>
    public AgentSpawnRequestBuilder RequestedBy(AgentId agentId)
    {
        _requestingAgentId = agentId;
        return this;
    }

    /// <summary>Adds custom metadata.</summary>
    public AgentSpawnRequestBuilder WithMetadata(string key, object value)
    {
        _metadata[key] = value;
        return this;
    }

    /// <summary>Builds the spawn request.</summary>
    public AgentSpawnRequest Build()
    {
        return new AgentSpawnRequest(
            _role,
            _goal,
            _parentCrewId,
            _backstory,
            _allowDelegation,
            _maxIterations,
            _maxRpm,
            _verbose,
            _maxExecutionTime,
            _cacheEnabled,
            _tools,
            _requestingAgentId,
            _metadata.AsReadOnly());
    }
}
