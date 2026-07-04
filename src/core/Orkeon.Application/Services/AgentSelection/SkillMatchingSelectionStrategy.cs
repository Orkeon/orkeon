using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Services.AgentSelection;

/// <summary>
/// Selects agents by matching task description keywords against agent role, goal, and backstory.
/// Uses Jaccard similarity over token sets.
/// </summary>
public sealed class SkillMatchingSelectionStrategy : IAgentSelectionStrategy
{
    private const double Threshold = 0.1;

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

        var taskTokens = Tokenize(task.Description?.ToString());

        DomainAgent? best = null;
        double bestScore = Threshold;

        foreach (var agent in availableAgents)
        {
            if (currentAgent != null && agent.Id == currentAgent.Id)
                continue;

            var agentText = string.Join(" ",
                agent.Role?.ToString() ?? "",
                agent.Goal?.ToString() ?? "",
                agent.Backstory?.Value ?? "");

            var agentTokens = Tokenize(agentText);
            var score = JaccardSimilarity(taskTokens, agentTokens);

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
            }
        }

        return System.Threading.Tasks.Task.FromResult<DomainAgent?>(best);
    }

    private static HashSet<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    private static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 0.0;

        var intersection = a.Count(token => b.Contains(token));
        var union = a.Count + b.Count - intersection;

        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
