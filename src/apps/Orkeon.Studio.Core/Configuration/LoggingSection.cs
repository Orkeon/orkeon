using System.Text.Json.Nodes;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over <c>Logging:LogLevel</c> — a free-form map of category to level, so
/// the accessors are per-category rather than a fixed set of properties.
/// </summary>
public sealed class LoggingSection
{
    /// <summary>Configuration path of the log-level map.</summary>
    public const string SectionPath = "Logging:LogLevel";

    /// <summary>The category the .NET logging stack uses as the catch-all.</summary>
    public const string DefaultCategory = "Default";

    /// <summary>The level names accepted by the .NET logging configuration.</summary>
    public static IReadOnlyList<string> KnownLevels { get; } =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    private readonly AppSettingsDocument _document;

    internal LoggingSection(AppSettingsDocument document) => _document = document;

    /// <summary>True when the map carries at least one category.</summary>
    public bool Exists => _document.SectionExists(SectionPath);

    /// <summary>Level of the <c>Default</c> category.</summary>
    public string? DefaultLevel
    {
        get => GetLevel(DefaultCategory);
        set => SetLevel(DefaultCategory, value);
    }

    /// <summary>Every configured category, in file order.</summary>
    public IReadOnlyDictionary<string, string> Levels
    {
        get
        {
            var levels = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_document.GetNode(SectionPath) is not JsonObject map)
                return levels;

            foreach (var (category, node) in map)
            {
                if (node is JsonValue value && value.TryGetValue<string>(out var level))
                    levels[category] = level;
            }

            return levels;
        }
    }

    /// <summary>Reads one category's level.</summary>
    public string? GetLevel(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return _document.GetString($"{SectionPath}:{category}");
    }

    /// <summary>Writes one category's level; a null or blank level removes the category.</summary>
    public void SetLevel(string category, string? level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        _document.SetString($"{SectionPath}:{category}", level);
    }

    /// <summary>Removes the whole map.</summary>
    public void Remove() => _document.Remove(SectionPath);
}
