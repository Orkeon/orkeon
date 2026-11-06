using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// One reading of the page: its outline as a semantic graph, in a store of its own, and for each node
/// the reference the browser knows its element by. Node ids carry the number of the snapshot
/// (<c>p3#12</c>: element 12 of snapshot 3), so an id kept from an earlier reading is told apart.
/// </summary>
internal sealed class PageSnapshot
{
    private readonly ImmutableArray<string?> _sharedIds;
    private readonly Dictionary<string, RaggableNode> _byId;

    private PageSnapshot(int generation, PageOutline outline, RaggableTree tree, IRaggableStore store, ImmutableArray<string?> sharedIds)
    {
        Generation = generation;
        Outline = outline;
        Tree = tree;
        Store = store;
        _sharedIds = sharedIds;

        // The index of a tree is keyed by qualified name; node ids are what the agent holds.
        _byId = tree.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
    }

    /// <summary>Number of this snapshot in its session, from 1.</summary>
    public int Generation { get; }

    public PageOutline Outline { get; }

    public RaggableTree Tree { get; }

    /// <summary>The graph of this page alone: never the code index of the host.</summary>
    public IRaggableStore Store { get; }

    public string Url => Outline.Location;

    public string Title => Outline.Title;

    /// <summary>The rendered text of the page, cut at the limit the script was given.</summary>
    public string Text { get; private init; } = string.Empty;

    /// <summary><c>true</c> when <see cref="Text"/> was cut.</summary>
    public bool TextTruncated { get; private init; }

    /// <summary><c>true</c> when the page holds more elements than the outline lists.</summary>
    public bool Truncated { get; private init; }

    /// <summary>Id of the node of the page itself.</summary>
    public string RootId => PageOutlineMapper.RootId(Outline.Path);

    /// <summary>The virtual path of the pages of snapshot <paramref name="generation"/>.</summary>
    public static string PathOf(int generation) => string.Create(CultureInfo.InvariantCulture, $"p{generation}");

    /// <summary>
    /// Builds the snapshot from what <c>page-outline.js</c> returned: the JSON of the outline, and the
    /// browser's reference of each element in the same order.
    /// </summary>
    /// <exception cref="FirefoxException">The script's answer is not an outline.</exception>
    public static PageSnapshot Parse(string json, ImmutableArray<string?> sharedIds, int generation, IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        JsonObject page;
        try
        {
            page = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException ex)
        {
            throw new FirefoxException($"The page could not be read ({ex.Message}).", ex);
        }

        var elements = (page["elements"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(Element)
            .ToImmutableList();
        var outline = new PageOutline
        {
            Path = PathOf(generation),
            Location = Str(page, "url"),
            Title = Str(page, "title"),
            Text = Str(page, "lead"),
            Elements = elements,
        };
        var tree = PageOutlineMapper.Map(outline);
        return new PageSnapshot(generation, outline, tree, new InMemoryRaggableStore(tree, fileSystem), sharedIds)
        {
            Text = Str(page, "text"),
            TextTruncated = Flag(page, "textTruncated"),
            Truncated = Flag(page, "truncated"),
        };
    }

    /// <summary>
    /// Reads a node id: the snapshot it belongs to and the position of its element, -1 for the page itself.
    /// </summary>
    public static bool TryParseId(string? nodeId, out int generation, out int index)
    {
        generation = 0;
        index = -1;
        if (nodeId is null || nodeId.Length < 4 || nodeId[0] != 'p')
            return false;

        var separator = nodeId.IndexOf('#', StringComparison.Ordinal);
        if (separator < 2
            || !int.TryParse(nodeId.AsSpan(1, separator - 1), NumberStyles.None, CultureInfo.InvariantCulture, out generation))
        {
            return false;
        }

        var position = nodeId.AsSpan(separator + 1);
        return position.SequenceEqual("page")
            || int.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    /// <summary>The node <paramref name="nodeId"/> names in this snapshot.</summary>
    /// <exception cref="FirefoxException">Not a node id, or the id of an earlier snapshot.</exception>
    public RaggableNode Node(string nodeId)
    {
        if (!TryParseId(nodeId, out var generation, out var index) || generation > Generation)
            throw Unknown(nodeId);
        if (generation < Generation)
            throw new FirefoxException($"The node id '{nodeId}' belongs to an earlier reading of the page: call snapshot again.");
        if (index >= Outline.Elements.Count)
            throw Unknown(nodeId);

        return _byId[index < 0 ? RootId : PageOutlineMapper.NodeId(Outline.Path, index)];
    }

    /// <summary>The browser's reference of the element of <paramref name="nodeId"/>.</summary>
    /// <exception cref="FirefoxException">Not an element of this snapshot: the page itself, a stale or unknown id.</exception>
    public string SharedId(string nodeId)
    {
        Node(nodeId);
        if (!TryParseId(nodeId, out _, out var index) || index < 0)
            throw new FirefoxException($"'{nodeId}' is the page, not one of its elements: give the id of a link or of a control.");

        return index < _sharedIds.Length && _sharedIds[index] is { Length: > 0 } sharedId
            ? sharedId
            : throw new FirefoxException("The node is no longer in the page: call snapshot again and use the new node ids.");
    }

    private static FirefoxException Unknown(string? nodeId) =>
        new($"'{nodeId}' is not a node of the page: node ids come from snapshot, find and search.");

    private static PageElement Element(JsonObject element) => new()
    {
        Kind = Number(element, "k") switch
        {
            0 => PageElementKind.Heading,
            1 => PageElementKind.Region,
            2 => PageElementKind.Link,
            _ => PageElementKind.Control,
        },
        Role = Str(element, "r"),
        Name = Str(element, "n"),
        Level = Number(element, "l"),
        Container = Number(element, "c", -1),
        Text = Str(element, "t"),
        Target = element["h"] is null ? null : Str(element, "h"),
        Anchor = element["a"] is null ? null : Str(element, "a"),
        Visible = Number(element, "v", 1) != 0,
    };

    private static string Str(JsonObject source, string key) =>
        source[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static int Number(JsonObject source, string key, int fallback = 0) =>
        source[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

    private static bool Flag(JsonObject source, string key) =>
        source[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
