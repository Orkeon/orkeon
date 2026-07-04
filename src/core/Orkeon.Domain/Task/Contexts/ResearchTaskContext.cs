namespace Orkeon.Domain.Task.Contexts;

/// <summary>Context for research tasks.</summary>
public class ResearchTaskContext
{
    /// <summary>Gets or sets the research topic.</summary>
    public string Topic { get; init; } = string.Empty;

    private readonly List<string> _sources = [];
    private readonly List<string> _keyFindings = [];
    private readonly List<string> _searchQueries = [];
    private readonly List<ResearchReference> _references = [];

    /// <summary>Gets or sets the sources used.</summary>
    public IReadOnlyList<string> Sources
    {
        get => _sources;
        init => _sources = AsBackingList(value);
    }

    /// <summary>Gets or sets the relevance scores per source.</summary>
    public Dictionary<string, float> RelevanceScores { get; init; } = [];

    /// <summary>Gets or sets the key findings.</summary>
    public IReadOnlyList<string> KeyFindings
    {
        get => _keyFindings;
        init => _keyFindings = AsBackingList(value);
    }

    /// <summary>Gets or sets when the research started.</summary>
    public DateTime ResearchStarted { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the search queries used.</summary>
    public IReadOnlyList<string> SearchQueries
    {
        get => _searchQueries;
        init => _searchQueries = AsBackingList(value);
    }

    /// <summary>Gets or sets the references found.</summary>
    public IReadOnlyList<ResearchReference> References
    {
        get => _references;
        init => _references = AsBackingList(value);
    }

    private static List<T> AsBackingList<T>(IReadOnlyList<T> value) =>
        value switch
        {
            null => null!,
            List<T> list => list,
            _ => [.. value],
        };

    /// <summary>Adds a search query to the research context.</summary>
    /// <param name="query">The search query to add.</param>
    public void AddSearchQuery(string query)
    {
        _searchQueries.Add(query);
    }

    /// <summary>Adds a reference to the research context.</summary>
    /// <param name="reference">The reference to add.</param>
    public void AddReference(ResearchReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _references.Add(reference);
    }

    /// <summary>Adds a source with its relevance score.</summary>
    /// <param name="source">The source URL or identifier.</param>
    /// <param name="relevance">The relevance score (0.0 to 1.0).</param>
    public void AddSource(string source, float relevance = 1.0f)
    {
        if (!string.IsNullOrWhiteSpace(source))
        {
            _sources.Add(source);
            RelevanceScores[source] = Math.Clamp(relevance, 0.0f, 1.0f);
        }
    }

    /// <summary>Adds a key finding.</summary>
    /// <param name="finding">The finding text.</param>
    public void AddKeyFinding(string finding)
    {
        if (!string.IsNullOrWhiteSpace(finding))
        {
            _keyFindings.Add(finding);
        }
    }

    /// <summary>Gets the most relevant sources sorted by relevance score.</summary>
    /// <param name="count">The maximum number of sources to return.</param>
    /// <returns>The top sources by relevance.</returns>
    public IEnumerable<string> GetTopSources(int count = 5)
    {
        var sortedSources = new List<KeyValuePair<string, float>>(RelevanceScores);
        sortedSources.Sort((a, b) => b.Value.CompareTo(a.Value));

        for (int i = 0; i < Math.Min(count, sortedSources.Count); i++)
        {
            yield return sortedSources[i].Key;
        }
    }
}

/// <summary>Represents a research reference.</summary>
public sealed record ResearchReference
{
    /// <summary>Gets the title of the reference.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>Gets the URL of the reference.</summary>
    public Uri? Url { get; init; }
    /// <summary>Gets the author of the reference.</summary>
    public string Author { get; init; } = string.Empty;
    /// <summary>Gets the publication date, or null if unknown.</summary>
    public DateTime? PublishedDate { get; init; }
    /// <summary>Gets a summary of the reference content.</summary>
    public string Summary { get; init; } = string.Empty;
    /// <summary>Gets the relevance score (0.0 to 1.0).</summary>
    public float Relevance { get; init; } = 0.5f;
}
