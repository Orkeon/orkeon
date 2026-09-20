using System.Text.RegularExpressions;
using Orkeon.Constants.Configuration;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over the <c>MCP</c> section (STUDIO-21): the switch and the servers the runner
/// connects before a crew loads, keyed by identifier under <c>MCP:Servers</c>. Every write
/// lands in place, field by field, so a key Studio does not model survives an edit of the
/// server that carries it. The five fields are the ones <c>McpServerConfig</c> binds; the
/// outbound server (<c>MCP:Server</c>) is not modelled here.
/// </summary>
public sealed partial class McpSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = ConfigurationKeys.McpSection;

    /// <summary>Configuration path of the servers dictionary.</summary>
    public const string ServersPath = SectionPath + ":Servers";

    /// <summary>The stdio transport: a process the runner launches.</summary>
    public const string StdioTransport = "Stdio";

    /// <summary>The HTTP transport (streamable HTTP, JSON-response mode), spelt as the runtime does.</summary>
    public const string SseTransport = "Sse";

    /// <summary>The transports the runtime knows, in the spelling it binds.</summary>
    public static IReadOnlyList<string> Transports { get; } = [StdioTransport, SseTransport];

    private readonly AppSettingsDocument _document;

    internal McpSection(AppSettingsDocument document) => _document = document;

    /// <summary>True when the section is present with at least one key.</summary>
    public bool Exists => _document.SectionExists(SectionPath);

    /// <summary>The switch as written; absent means on, which <see cref="IsEnabled"/> resolves.</summary>
    public bool? Enabled
    {
        get => _document.GetBoolean($"{SectionPath}:Enabled");
        set => _document.SetBoolean($"{SectionPath}:Enabled", value);
    }

    /// <summary>Whether the runtime will read the servers at all: the switch defaults to on.</summary>
    public bool IsEnabled => Enabled ?? true;

    /// <summary>The server identifiers, in document order.</summary>
    public IReadOnlyList<string> ServerIds => _document.ObjectKeys(ServersPath);

    /// <summary>Whether at least one server is declared.</summary>
    public bool HasServers => ServerIds.Count > 0;

    /// <summary>
    /// An identifier the configuration binder keeps whole: letters, digits, dots, underscores
    /// and hyphens. A colon would split it into a path, a blank would vanish.
    /// </summary>
    public static bool IsValidServerId(string? id) => id is { Length: > 0 } && ServerIdPattern().IsMatch(id);

    /// <summary>The server under <paramref name="id"/>, or null when no such object exists.</summary>
    public McpServerDefinition? GetServer(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var path = $"{ServersPath}:{id}";
        if (_document.GetNode(path) is not System.Text.Json.Nodes.JsonObject)
            return null;

        return new McpServerDefinition
        {
            Id = id,
            Transport = _document.GetString($"{path}:Transport") ?? StdioTransport,
            Command = _document.GetString($"{path}:Command"),
            Args = _document.GetStringArray($"{path}:Args"),
            Url = _document.GetString($"{path}:Url"),
            Env = _document.GetStringMap($"{path}:Env"),
        };
    }

    /// <summary>
    /// Writes the server's five fields in place under its identifier. A blank command or URL
    /// and an empty argument list or environment remove their key rather than writing an
    /// empty value; the transport is always written, so a server with nothing else set still
    /// exists as an object the runtime can see.
    /// </summary>
    public void SetServer(McpServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!IsValidServerId(server.Id))
            throw new ArgumentException($"'{server.Id}' is not a usable MCP server identifier.", nameof(server));

        var path = $"{ServersPath}:{server.Id}";
        _document.SetString($"{path}:Transport", server.Transport);
        _document.SetString($"{path}:Command", server.Command);
        if (server.Args.Count == 0)
            _document.Remove($"{path}:Args");
        else
            _document.SetStringArray($"{path}:Args", server.Args);
        _document.SetString($"{path}:Url", server.Url);
        _document.SetStringMap($"{path}:Env", server.Env);
    }

    /// <summary>Removes the server and everything under it; the dictionary goes when it empties.</summary>
    public void RemoveServer(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _document.Remove($"{ServersPath}:{id}");
        if (ServerIds.Count == 0)
            _document.Remove(ServersPath);
    }

    /// <summary>
    /// Moves a server under a new identifier, every key it carries included. A rename is a
    /// removal and a write of the same node, so nothing the server holds is lost on the way.
    /// </summary>
    public void RenameServer(string oldId, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
        if (!IsValidServerId(newId))
            throw new ArgumentException($"'{newId}' is not a usable MCP server identifier.", nameof(newId));
        if (string.Equals(oldId, newId, StringComparison.Ordinal))
            return;

        var node = _document.GetNode($"{ServersPath}:{oldId}");
        if (node is null)
            return;

        var copy = node.DeepClone();
        _document.Remove($"{ServersPath}:{oldId}");
        _document.SetNode($"{ServersPath}:{newId}", copy);
    }

    /// <summary>Removes the whole section.</summary>
    public void Remove() => _document.Remove(SectionPath);

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex ServerIdPattern();
}

/// <summary>One MCP server as the settings spell it: the five fields the runtime binds.</summary>
public sealed record McpServerDefinition
{
    /// <summary>The dictionary key under <c>MCP:Servers</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The transport, in the runtime's spelling; see <see cref="McpSection.Transports"/>.</summary>
    public string Transport { get; init; } = McpSection.StdioTransport;

    /// <summary>The process to launch (stdio).</summary>
    public string? Command { get; init; }

    /// <summary>The process arguments (stdio).</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>The endpoint (HTTP), as typed.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
        Justification = "This is the JSON field as typed by the user, not a resolved endpoint: an " +
                        "in-progress or malformed URL must survive load/edit/save, which System.Uri " +
                        "cannot represent. Validation reports it instead of rejecting the text.")]
    public string? Url { get; init; }

    /// <summary>Environment variables laid on the process, verbatim (stdio).</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the transport is stdio (case-blind, as the binder reads enums).</summary>
    public bool IsStdio => string.Equals(Transport, McpSection.StdioTransport, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the transport is the HTTP one.</summary>
    public bool IsSse => string.Equals(Transport, McpSection.SseTransport, StringComparison.OrdinalIgnoreCase);
}
