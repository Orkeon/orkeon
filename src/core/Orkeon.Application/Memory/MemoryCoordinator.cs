using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Memory;

/// <summary>
/// Coordinates memory operations for agents and tasks.
/// </summary>
public partial class MemoryCoordinator : IMemoryCoordinator
{
    private readonly ILogger<MemoryCoordinator> _logger;
    private readonly IMemoryService _memoryService;
    private readonly CrewMemoryProviderRegistry? _providerRegistry;

    /// <summary>
    /// Initializes a new instance of <see cref="MemoryCoordinator"/>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memoryService">Where the memories are saved and searched.</param>
    /// <param name="providerRegistry">
    /// What each crew declared at kickoff; gives the scope a memory is tagged with
    /// (<c>crew:&lt;scope&gt;</c>). Without it, the scope is the crew id.
    /// </param>
    public MemoryCoordinator(
        ILogger<MemoryCoordinator> logger,
        IMemoryService memoryService,
        CrewMemoryProviderRegistry? providerRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(memoryService);
        _memoryService = memoryService;
        _providerRegistry = providerRegistry;
    }

    /// <summary>
    /// Retrieve Relevant Memories Async.
    /// </summary>
    public System.Threading.Tasks.Task<IEnumerable<MemoryItem>> RetrieveRelevantMemoriesAsync(
        DomainAgent agent,
        CrewTask task,
        SimpleExecutionContext context,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);
        return RetrieveRelevantMemoriesCoreAsync();

        async System.Threading.Tasks.Task<IEnumerable<MemoryItem>> RetrieveRelevantMemoriesCoreAsync()
        {
            var query = $"{task.Description} {agent.Role}";
            var memories = await _memoryService.SearchMemoryAsync(
                context.CrewId,
                query,
                maxResults,
                null,
                cancellationToken).ConfigureAwait(false);

            LogMemoriesRetrieved(memories.Count, agent.Id, task.Id);

            return memories;
        }
    }

    /// <summary>
    /// Store Task Result Async.
    /// </summary>
    public System.Threading.Tasks.Task StoreTaskResultAsync(
        DomainAgent agent,
        CrewTask task,
        string output,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(context);
        return StoreTaskResultCoreAsync();

        async System.Threading.Tasks.Task StoreTaskResultCoreAsync()
        {
            var tags = new Dictionary<string, string>
            {
                ["agent_id"] = agent.Id.ToString(),
                ["agent_role"] = agent.Role.Value,
                ["task_id"] = task.Id.ToString()
            };

            var customProps = new Dictionary<string, string>
            {
                ["task_description"] = task.Description.Value,
                ["timestamp"] = Inv.ToString(DateTime.UtcNow, "O")
            };

            customProps = customProps.Concat(tags).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var memory = MemoryItem.Create(
                content: string.IsNullOrWhiteSpace(output) ? "[No output generated]" : output,
                embedding: null,
                importance: 0.8f,
                source: "task_execution",
                tags: [$"agent:{agent.Id}", $"task:{task.Id}", CrewTag(context)],
                createdBy: agent.Id,
                customProperties: customProps);

            await _memoryService.SaveMemoryAsync(
                context.CrewId,
                memory,
                cancellationToken).ConfigureAwait(false);

            LogTaskResultStored(agent.Id, task.Id);
        }
    }

    /// <summary>
    /// Store Agent Experience Async.
    /// </summary>
    public System.Threading.Tasks.Task StoreAgentExperienceAsync(
        DomainAgent agent,
        string experience,
        double importance,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(context);
        return StoreAgentExperienceCoreAsync();

        async System.Threading.Tasks.Task StoreAgentExperienceCoreAsync()
        {
            var tags = new Dictionary<string, string>
            {
                ["agent_id"] = agent.Id.ToString(),
                ["agent_role"] = agent.Role.Value,
                ["type"] = "experience"
            };

            var memory = MemoryItem.Create(
                content: string.IsNullOrWhiteSpace(experience) ? "[No experience recorded]" : experience,
                embedding: null,
                importance: (float)importance,
                source: "agent_experience",
                tags: [$"agent:{agent.Id}", "experience", CrewTag(context)],
                createdBy: agent.Id,
                customProperties: tags);

            await _memoryService.SaveMemoryAsync(
                context.CrewId,
                memory,
                cancellationToken).ConfigureAwait(false);

            LogExperienceStored(agent.Id, importance);
        }
    }

    /// <summary>
    /// Update Working Memory Async.
    /// </summary>
    public System.Threading.Tasks.Task UpdateWorkingMemoryAsync(
        DomainAgent agent,
        string key,
        string value,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(context);
        return UpdateWorkingMemoryCoreAsync();

        async System.Threading.Tasks.Task UpdateWorkingMemoryCoreAsync()
        {
            var metadata = new Dictionary<string, object>
            {
                ["timestamp"] = DateTime.UtcNow
            };

            var content = key switch
            {
                null when value == null => "[No working memory data]",
                null => $"(null): {value}",
                _ when value == null => $"{key}: (null)",
                _ => $"{key}: {value}"
            };

            var memory = MemoryItem.Create(
                content: content,
                embedding: null,
                importance: MemoryDefaults.DefaultImportance, // Working memory has medium importance
                source: "working_memory",
                tags: [$"agent:{agent.Id}", "working_memory", $"key:{key ?? "null"}", CrewTag(context)],
                createdBy: agent.Id,
                customProperties: metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? string.Empty));

            await _memoryService.SaveMemoryAsync(
                context.CrewId,
                memory,
                cancellationToken).ConfigureAwait(false);

            LogWorkingMemoryUpdated(agent.Id, key, value);
        }
    }

    /// <summary>
    /// The tag naming the crew a memory belongs to: <c>crew:&lt;scope&gt;</c>, the scope being the
    /// crew's name recorded at kickoff, else its id (GAP-20).
    /// </summary>
    private string CrewTag(SimpleExecutionContext context) =>
        $"crew:{_providerRegistry?.GetScope(context.CrewId) ?? context.CrewId.ToString()}";

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrieved {Count} memories for agent {AgentId} and task {TaskId}")]
    private partial void LogMemoriesRetrieved(int count, object agentId, object taskId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored task result in memory for agent {AgentId} and task {TaskId}")]
    private partial void LogTaskResultStored(object agentId, object taskId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored experience for agent {AgentId} with importance {Importance}")]
    private partial void LogExperienceStored(object agentId, double importance);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated working memory for agent {AgentId}: {Key}={Value}")]
    private partial void LogWorkingMemoryUpdated(object agentId, string? key, string? value);
}
