using System.Collections.Immutable;
using Orkeon.Generators;

namespace Orkeon.Application.Services.Generic;

/// <summary>
/// Strongly typed research findings.
/// </summary>
[TypedDictionary(typeof(ResearchFindingEntry), CacheEmpty = true, EmitGenericAdd = false)]
public sealed partial class ResearchFindings
{
    /// <summary>
    /// Get.
    /// </summary>
    public T? Get<T>(string source) where T : ResearchFindingEntry
    {
        if (!_items.TryGetValue(source, out var value))
            return default;

        return value as T;
    }

    /// <summary>
    /// Get Finding.
    /// </summary>
    public ResearchFindingEntry? GetFinding(string source)
    {
        return _items.TryGetValue(source, out var value) ? value : null;
    }

    /// <summary>
    /// Contains Source.
    /// </summary>
    public bool ContainsSource(string source) => _items.ContainsKey(source);

    /// <summary>
    /// Gets or sets the sources.
    /// </summary>
    public IEnumerable<string> Sources => _items.Keys;

    /// <summary>
    /// Gets the number of entries.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// To Dictionary.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.ToObject();
        }
        return result;
    }

    /// <summary>
    /// From Dictionary.
    /// </summary>
    public static ResearchFindings FromDictionary(IDictionary<string, object>? dictionary)
    {
        if (dictionary == null || dictionary.Count == 0)
            return Empty;

        var builder = CreateBuilder();
        foreach (var kvp in dictionary)
        {
            if (kvp.Value is ResearchFindingEntry entry)
            {
                builder.AddFinding(kvp.Key, entry);
            }
            else
            {
                // Try to convert from anonymous object format
                var obj = kvp.Value;
                if (obj != null)
                {
                    // Default to generic finding
                    builder.AddFinding(kvp.Key, new GenericFinding(obj));
                }
            }
        }
        return builder.Build();
    }

    /// <summary>Builder for constructing <see cref="ResearchFindings"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Finding.
        /// </summary>
        public Builder AddFinding(string source, ResearchFindingEntry finding)
        {
            ArgumentNullException.ThrowIfNull(finding);
            _items[source] = finding;
            return this;
        }

        /// <summary>
        /// Add Source Analysis.
        /// </summary>
        public Builder AddSourceAnalysis(string source, float relevance, string[] keyPoints, string summary)
        {
            _items[source] = new SourceAnalysis(relevance, keyPoints, summary);
            return this;
        }
    }
}

/// <summary>
/// Base class for research finding entries.
/// </summary>
public abstract class ResearchFindingEntry
{
    /// <summary>
    /// To Object.
    /// </summary>
    public abstract object ToObject();
}

/// <summary>
/// Source analysis finding.
/// </summary>
public sealed class SourceAnalysis : ResearchFindingEntry
{
    /// <summary>Gets or sets the relevance.</summary>
    public float Relevance { get; }
    /// <summary>Gets or sets the key points.</summary>
    public ImmutableArray<string> KeyPoints { get; }
    /// <summary>Gets or sets the summary.</summary>
    public string Summary { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SourceAnalysis"/>.
    /// </summary>
    public SourceAnalysis(float relevance, string[] keyPoints, string summary)
    {
        Relevance = relevance;
        KeyPoints = keyPoints?.ToImmutableArray() ?? [];
        Summary = summary ?? string.Empty;
    }

    /// <summary>
    /// To Object.
    /// </summary>
    public override object ToObject()
    {
        return new
        {
            Relevance,
            KeyPoints = KeyPoints.ToArray(),
            Summary
        };
    }
}

/// <summary>
/// Generic finding for flexibility.
/// </summary>
public sealed class GenericFinding : ResearchFindingEntry
{
    private readonly object _value;

    /// <summary>
    /// Initializes a new instance of <see cref="GenericFinding"/>.
    /// </summary>
    public GenericFinding(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>
    /// To Object.
    /// </summary>
    public override object ToObject() => _value;
}
