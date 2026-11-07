using System.Text.Json;

namespace Orkeon.Plugins;

/// <summary>
/// The <c>plugin.json</c> manifest every plugin folder carries:
/// <c>{ "name", "version", "orkeonVersion", "entryAssembly", "tools": [] }</c>.
/// </summary>
public sealed record PluginManifest
{
    /// <summary>Plugin name; must equal <see cref="IOrkeonPlugin.Name"/>.</summary>
    public string? Name { get; init; }

    /// <summary>Plugin version; must equal <see cref="IOrkeonPlugin.Version"/>.</summary>
    public string? Version { get; init; }

    /// <summary>NuGet-style version range of the Orkeon releases the plugin targets.</summary>
    public string? OrkeonVersion { get; init; }

    /// <summary>File name of the entry assembly, inside the plugin folder.</summary>
    public string? EntryAssembly { get; init; }

    /// <summary>Names of the tools the plugin registers.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>
    /// Parses a manifest document. Required fields are not checked here
    /// (see <see cref="PluginManifestValidator"/>); a document that is not JSON, not an object, or
    /// whose fields have the wrong type is refused. Unknown properties are ignored.
    /// </summary>
    /// <param name="json">The content of a <c>plugin.json</c> file.</param>
    /// <exception cref="PluginManifestException">The document cannot be read as a manifest.</exception>
    public static PluginManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new PluginManifestException("The manifest must be a JSON object.");

            var manifest = new PluginManifest();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                manifest = property.Name.ToUpperInvariant() switch
                {
                    "NAME" => manifest with { Name = ReadText(property) },
                    "VERSION" => manifest with { Version = ReadText(property) },
                    "ORKEONVERSION" => manifest with { OrkeonVersion = ReadText(property) },
                    "ENTRYASSEMBLY" => manifest with { EntryAssembly = ReadText(property) },
                    "TOOLS" => manifest with { Tools = ReadTools(property) },
                    _ => manifest,
                };
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            throw new PluginManifestException($"The manifest is not valid JSON ({ex.Message}).", ex);
        }
    }

    private static string? ReadText(JsonProperty property) => property.Value.ValueKind switch
    {
        JsonValueKind.String => property.Value.GetString(),
        JsonValueKind.Null => null,
        _ => throw new PluginManifestException($"Manifest field '{property.Name}' must be a string."),
    };

    private static List<string> ReadTools(JsonProperty property)
    {
        if (property.Value.ValueKind == JsonValueKind.Null)
            return [];
        if (property.Value.ValueKind != JsonValueKind.Array)
            throw new PluginManifestException("Manifest field 'tools' must be an array of tool names.");

        var tools = new List<string>();
        foreach (var item in property.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new PluginManifestException("Manifest field 'tools' must hold strings only.");
            tools.Add(item.GetString()!);
        }

        return tools;
    }
}
