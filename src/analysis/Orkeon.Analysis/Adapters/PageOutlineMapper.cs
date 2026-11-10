using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Adapters;

/// <summary>
/// Projects the outline of a web page into a <see cref="RaggableTree"/>: a <see cref="UniversalNodeKind.Module"/>
/// node for the page, a <see cref="UniversalNodeKind.Section"/> per heading and per region (tag <c>role</c>),
/// a <see cref="UniversalNodeKind.Link"/> per link (tag <c>target:&lt;raw target&gt;</c>, like a Markdown link)
/// and a <see cref="UniversalNodeKind.Control"/> per interactive element.
/// </summary>
/// <remarks>
/// <para>
/// A heading holds what follows it up to a heading of its level or above, or to the end of the region that
/// holds it; a region holds what the page nests in it. Every element hangs from the innermost of the two,
/// from the page otherwise, through its <c>ParentId</c> and an <see cref="EdgeKind.Contains"/> edge.
/// </para>
/// <para>
/// The FQN of an element is the path of its ancestors' positions (<c>&lt;path&gt;::00003/00007</c>): FQN
/// order is document order, and the FQN of a section is a prefix of those under it, so a store answers a
/// subtree query (<c>NodeQuery.RootFqn</c>) on it. An in-page link (<c>#id</c>) to an element of the
/// outline yields an <see cref="EdgeKind.Documents"/> edge and a <c>resolved:&lt;id&gt;</c> tag.
/// </para>
/// </remarks>
public static class PageOutlineMapper
{
    /// <summary>Language of every node of a page.</summary>
    public const string Language = "html";

    /// <summary>Tag holding the role of an element: <c>navigation</c>, <c>button</c>, <c>textbox</c>.</summary>
    public const string RoleTag = "role";
    /// <summary>Tag holding the level of a heading, from 1 to 6.</summary>
    public const string LevelTag = "level";
    private const string AnchorTag = "anchor";
    /// <summary>Tag of an element the page does not show.</summary>
    public const string HiddenTag = "hidden";
    private const string TargetPrefix = "target:";
    private const string ResolvedPrefix = "resolved:";
    private const int MinHeadingLevel = 1;
    private const int MaxHeadingLevel = 6;

    /// <summary>Id of the node that stands for the page of <paramref name="path"/>.</summary>
    public static string RootId(string path) => $"{path}#page";

    /// <summary>Id of the node of the element at <paramref name="index"/> in the outline of <paramref name="path"/>.</summary>
    public static string NodeId(string path, int index) => string.Create(CultureInfo.InvariantCulture, $"{path}#{index}");

    /// <summary>Builds the tree of <paramref name="outline"/>.</summary>
    public static RaggableTree Map(PageOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentException.ThrowIfNullOrEmpty(outline.Path);

        var elements = outline.Elements;
        var root = Create(
            RootId(outline.Path), UniversalNodeKind.Module, NodeLevel.L2_Module, outline.Path,
            string.IsNullOrWhiteSpace(outline.Title) ? outline.Location : outline.Title, outline.Text, line: 0);
        root.Fqn = outline.Path;
        root.Signature = outline.Location;
        root.EmbeddingText = string.IsNullOrEmpty(outline.Text) ? outline.Title : $"{outline.Title}\n{outline.Text}";

        var nodes = new List<RaggableNode>(elements.Count + 1) { root };
        var edges = new List<RaggableEdge>(elements.Count);
        var byIndex = new RaggableNode[elements.Count];
        var openHeadings = new Stack<int>();

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var container = ContainerOf(elements, i);
            CloseHeadings(elements, openHeadings, element, container);

            var parent = ParentOf(elements, openHeadings, byIndex, root, container);
            var node = CreateElement(outline.Path, i, element, parent);
            byIndex[i] = node;
            nodes.Add(node);
            edges.Add(Edge(parent.Id, node.Id, EdgeKind.Contains));

            if (element.Kind == PageElementKind.Heading)
                openHeadings.Push(i);
        }

        LinkAnchors(elements, byIndex, edges);

        return new RaggableTree(nodes, edges, nodes.ToDictionary(n => n.Fqn, StringComparer.Ordinal));
    }

    // A heading ends with the region that holds it, and with a heading of its level or above.
    private static void CloseHeadings(ImmutableList<PageElement> elements, Stack<int> openHeadings, PageElement element, int container)
    {
        while (openHeadings.Count > 0 && !Encloses(elements, ContainerOf(elements, openHeadings.Peek()), container))
            openHeadings.Pop();
        if (element.Kind != PageElementKind.Heading) return;
        while (openHeadings.Count > 0 && HeadingLevel(elements[openHeadings.Peek()]) >= HeadingLevel(element))
            openHeadings.Pop();
    }

    // The open heading holds the element when both sit in the same region; a heading outside
    // the element's region holds that region, not the element.
    private static RaggableNode ParentOf(ImmutableList<PageElement> elements, Stack<int> openHeadings, RaggableNode[] byIndex, RaggableNode root, int container)
    {
        if (openHeadings.Count > 0 && ContainerOf(elements, openHeadings.Peek()) == container)
            return byIndex[openHeadings.Peek()];
        return container >= 0 ? byIndex[container] : root;
    }

    private static RaggableNode CreateElement(string path, int index, PageElement element, RaggableNode parent)
    {
        var (kind, level) = element.Kind switch
        {
            PageElementKind.Link => (UniversalNodeKind.Link, NodeLevel.L4_Statement),
            PageElementKind.Control => (UniversalNodeKind.Control, NodeLevel.L4_Statement),
            _ => (UniversalNodeKind.Section, NodeLevel.L3_Symbol),
        };

        var node = Create(NodeId(path, index), kind, level, path, element.Name, element.Text, line: index + 1);
        var position = index.ToString("D5", CultureInfo.InvariantCulture);
        node.Fqn = parent.Level == NodeLevel.L2_Module ? $"{path}::{position}" : $"{parent.Fqn}/{position}";
        node.ParentId = parent.Id;
        parent.ChildrenIdsMutable.Add(node.Id);
        node.EmbeddingText = string.IsNullOrEmpty(element.Text) ? element.Name : $"{element.Name}\n{element.Text}";

        if (!string.IsNullOrEmpty(element.Role)) node.TagsMutable[RoleTag] = element.Role;
        if (!string.IsNullOrEmpty(element.Anchor)) node.TagsMutable[AnchorTag] = element.Anchor;
        if (!element.Visible) node.TagsMutable[HiddenTag] = string.Empty;
        if (element.Kind == PageElementKind.Heading)
            node.TagsMutable[LevelTag] = HeadingLevel(element).ToString(CultureInfo.InvariantCulture);
        if (element.Kind == PageElementKind.Link && !string.IsNullOrEmpty(element.Target))
        {
            node.Signature = element.Target;
            node.TagsMutable[TargetPrefix + element.Target] = string.Empty;
        }

        return node;
    }

    private static void LinkAnchors(ImmutableList<PageElement> elements, RaggableNode[] byIndex, List<RaggableEdge> edges)
    {
        var anchors = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i].Anchor is { Length: > 0 } anchor)
                anchors.TryAdd(anchor, byIndex[i]);
        }

        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i] is not { Kind: PageElementKind.Link, Target: ['#', _, ..] target }
                || !anchors.TryGetValue(target[1..], out var found)
                || ReferenceEquals(found, byIndex[i]))
            {
                continue;
            }

            byIndex[i].TagsMutable[ResolvedPrefix + found.Id] = found.Fqn;
            edges.Add(Edge(byIndex[i].Id, found.Id, EdgeKind.Documents));
        }
    }

    /// <summary>The region that holds the element at <paramref name="index"/>: an earlier region, or -1.</summary>
    private static int ContainerOf(ImmutableList<PageElement> elements, int index)
    {
        var container = elements[index].Container;
        return container >= 0 && container < index && elements[container].Kind == PageElementKind.Region ? container : -1;
    }

    /// <summary><paramref name="outer"/> is <paramref name="inner"/> or a region around it (-1 is around everything).</summary>
    private static bool Encloses(ImmutableList<PageElement> elements, int outer, int inner)
    {
        if (outer < 0) return true;
        for (var current = inner; current >= 0; current = ContainerOf(elements, current))
        {
            if (current == outer) return true;
        }

        return false;
    }

    private static int HeadingLevel(PageElement element) => Math.Clamp(element.Level, MinHeadingLevel, MaxHeadingLevel);

    private static RaggableEdge Edge(string from, string to, EdgeKind kind) => new($"{from}→{to}:{kind}", from, to, kind);

    private static RaggableNode Create(
        string id, UniversalNodeKind kind, NodeLevel level, string path, string name, string text, int line) => new()
        {
            Id = id,
            Kind = kind,
            Name = name,
            VirtualFilePath = path,
            // A page has no byte offsets: the line is the position of the element in the outline.
            Range = new NodeRange(0, 0, line, line, 1),
            Level = level,
            Language = Language,
            SourceSnippet = text,
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
        };
}
