using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Services.AgentSelection;

/// <summary>
/// Selects agents by computing cosine similarity between task and agent profile embeddings.
/// </summary>
public class EmbeddingBasedSelectionStrategy : IAgentSelectionStrategy
{
    private const double Threshold = 0.5;

    private readonly IEmbeddingService _embeddingService;

    /// <summary>
    /// Initializes a new instance of <see cref="EmbeddingBasedSelectionStrategy"/>.
    /// </summary>
    public EmbeddingBasedSelectionStrategy(IEmbeddingService embeddingService)
    {
        ArgumentNullException.ThrowIfNull(embeddingService);
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Select Best Agent Async.
    /// </summary>
    public System.Threading.Tasks.Task<DomainAgent?> SelectBestAgentAsync(
        ICrewTask task,
        IEnumerable<DomainAgent> availableAgents,
        DomainAgent? currentAgent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(availableAgents);

        return SelectBestAgentAsyncCore(task, availableAgents, currentAgent, cancellationToken);
    }

    private async System.Threading.Tasks.Task<DomainAgent?> SelectBestAgentAsyncCore(
        ICrewTask task,
        IEnumerable<DomainAgent> availableAgents,
        DomainAgent? currentAgent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var taskText = task.Description?.ToString() ?? "";
        var taskEmbedding = await _embeddingService.GetEmbeddingAsync(taskText).ConfigureAwait(false);

        DomainAgent? best = null;
        double bestScore = Threshold;

        foreach (var agent in availableAgents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (currentAgent != null && agent.Id == currentAgent.Id)
                continue;

            var agentText = string.Join(" ",
                agent.Role?.ToString() ?? "",
                agent.Goal?.ToString() ?? "",
                agent.Backstory?.Value ?? "");

            var agentEmbedding = await _embeddingService.GetEmbeddingAsync(agentText).ConfigureAwait(false);
            var score = CosineSimilarity(taskEmbedding, agentEmbedding);

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return best;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0)
            return 0.0;

        var length = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;

        for (int i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator == 0.0 ? 0.0 : dot / denominator;
    }
}
