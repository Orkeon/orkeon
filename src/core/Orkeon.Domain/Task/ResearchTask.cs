using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task;

/// <summary>
/// Task specialized for research activities.
/// </summary>
public class ResearchTask : CrewTaskBase<ResearchTaskContext>
{
    /// <summary>
    /// Private constructor for research task.
    /// </summary>
    private ResearchTask(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        string researchTopic,
        TaskPriority priority,
        TaskOutputOptions? outputOptions)
        : base(
            id,
            description,
            expectedOutput,
            priority,
            outputOptions,
            new ResearchTaskContext { Topic = researchTopic })
    {
    }

    /// <summary>
    /// Creates a new research task with the specified parameters.
    /// </summary>
    /// <param name="id">The unique task identifier.</param>
    /// <param name="description">The task description.</param>
    /// <param name="expectedOutput">The expected output description.</param>
    /// <param name="researchTopic">The research topic.</param>
    /// <param name="priority">The task priority.</param>
    /// <param name="outputOptions">Optional output configuration.</param>
    /// <returns>A new <see cref="ResearchTask"/> instance.</returns>
    public static ResearchTask Create(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        string researchTopic,
        TaskPriority? priority = null,
        TaskOutputOptions? outputOptions = null)
    {
        return new ResearchTask(id, description, expectedOutput, researchTopic, priority ?? TaskPriority.Normal, outputOptions);
    }

    /// <summary>
    /// Adds a source to the research.
    /// </summary>
    public void AddSource(string source, float relevance = 1.0f)
    {
        UpdateContext(ctx => ctx.AddSource(source, relevance));
    }

    /// <summary>
    /// Adds a key finding.
    /// </summary>
    public void AddKeyFinding(string finding)
    {
        UpdateContext(ctx => ctx.AddKeyFinding(finding));
    }

    /// <summary>
    /// Adds a search query used.
    /// </summary>
    public void AddSearchQuery(string query)
    {
        UpdateContext(ctx => ctx.AddSearchQuery(query));
    }

    /// <summary>
    /// Adds a reference.
    /// </summary>
    public void AddReference(ResearchReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        UpdateContext(ctx => ctx.AddReference(reference));
    }

    /// <summary>
    /// Gets the top sources by relevance.
    /// </summary>
    public IEnumerable<string> GetTopSources(int count = 5)
    {
        return TypedContext.GetTopSources(count);
    }

    /// <summary>
    /// Gets a summary of the research context.
    /// </summary>
    public override string GetContextSummary()
    {
        var context = TypedContext;
        return $"Research on '{context.Topic}' with {context.Sources.Count} sources and {context.KeyFindings.Count} findings";
    }
}
