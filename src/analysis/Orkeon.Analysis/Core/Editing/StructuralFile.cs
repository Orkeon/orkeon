#pragma warning disable CA1032, CA1064 // internal exceptions carry one domain constructor
using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Editing;

internal sealed record NodeRange(int StartByte, int EndByte, int StartLine, int EndLine);

internal sealed record StructuralNode(string Selector, string Kind, NodeRange Range, string Text, string Sha256);

/// <summary>
/// A parsed C#/TypeScript file whose declarations are addressable by selector. The tree is
/// snapshotted once at load and discarded; byte offsets are canonical (BOM and line endings kept).
/// </summary>
internal sealed class StructuralFile
{
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "namespace_declaration", "file_scoped_namespace_declaration", "class_declaration", "struct_declaration",
        "interface_declaration", "record_declaration", "record_struct_declaration", "enum_declaration",
        "abstract_class_declaration", "internal_module", "module",
    };

    private static readonly HashSet<string> Members = new(StringComparer.Ordinal)
    {
        "method_declaration", "constructor_declaration", "property_declaration", "method_definition",
        "method_signature", "function_declaration", "public_field_definition", "property_signature",
        "type_alias_declaration",
    };

    internal const string MarkdownLanguage = "markdown";

    private static readonly HashSet<string> Walkable = new(StringComparer.Ordinal) { "csharp", "typescript" };

    private static readonly HashSet<string> Bodies = new(StringComparer.Ordinal)
    {
        "declaration_list", "class_body", "interface_body", "object_type", "enum_body", "statement_block",
        "enum_member_declaration_list",
    };

    private readonly byte[] _bytes;
    private readonly string _source;
    private readonly int _bomLength;
    private readonly PathNode? _root;
    private readonly IReadOnlyList<RaggableNode> _sections;
    private readonly List<Declaration> _declarations = [];
    private string _markdownPath = string.Empty;
    private IReadOnlyList<SectionEntry>? _sectionEntries;

    private StructuralFile(byte[] bytes, string source, int bomLength, PathNode? root, IReadOnlyList<RaggableNode> sections)
    {
        _bytes = bytes;
        _source = source;
        _bomLength = bomLength;
        _root = root;
        _sections = sections;
    }

    public string Language { get; private init; } = string.Empty;

    public static async Task<StructuralFile> LoadAsync(IFileSystemService fs, TreeSitterParserPool pool, string virtualPath)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(pool);
        var grammar = GrammarOf(virtualPath);
        var bytes = await fs.TryReadAllBytesAsync(virtualPath, CancellationToken.None).ConfigureAwait(false)
            ?? throw new FileNotFoundException("File not found.", virtualPath);
        return Parse(pool, bytes, virtualPath, grammar.Language);
    }

    internal static StructuralFile FromBytes(TreeSitterParserPool pool, byte[] bytes, string virtualPath)
        => Parse(pool, bytes, virtualPath, GrammarOf(virtualPath).Language);

    private static GrammarInfo GrammarOf(string virtualPath)
    {
        var extension = Path.GetExtension(virtualPath);
        return LanguageRegistry.Shipped.FirstOrDefault(g =>
            (g.Language == MarkdownLanguage || Walkable.Contains(g.Language))
            && g.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            ?? throw new UnsupportedExtensionException(extension);
    }

    private static StructuralFile Parse(TreeSitterParserPool pool, byte[] bytes, string virtualPath, string language)
    {
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var source = Encoding.UTF8.GetString(bytes.AsSpan(bom));
        if (language == MarkdownLanguage)
        {
            var sections = new MarkdownDocumentMapper().Map(virtualPath, source)
                .Where(n => n.Kind == UniversalNodeKind.Section).ToList();
            return new StructuralFile(bytes, source, bom, null, sections) { _markdownPath = virtualPath, Language = language };
        }

        using var parsed = pool.Parse(source, language);
        var root = PathNode.Of(parsed.Root);
        var file = new StructuralFile(bytes, source, bom, root, []) { Language = language };
        file.Walk(root, string.Empty);
        return file;
    }

    public IReadOnlyList<StructuralNode> Outline() => _root is null
        ? Sections.Select(e => new StructuralNode(e.Selector, "section", SectionRange(e), string.Empty, string.Empty)).ToList()
        : _declarations.Select(d => new StructuralNode(d.Selector, d.Kind, DeclarationRange(d.Span, d.Siblings, d.Index), string.Empty, string.Empty)).ToList();

    public StructuralNode Resolve(NodeSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (_root is null) return ResolveSection(selector);
        return selector.IsTreeSitterPath ? ResolvePath(selector, _root) : ResolveReadable(selector);
    }

    private StructuralNode ResolveSection(NodeSelector selector)
    {
        var matches = Sections.Where(e => Matches(selector.Text, e.Path, false)).ToList();
        if (matches.Count == 0) matches = Sections.Where(e => Matches(selector.Text, e.Path, true)).ToList();
        if (matches.Count == 1) return BuildSection(matches[0]);
        if (matches.Count > 1) throw new AmbiguousSelectorException(matches.Select(m => m.Selector).ToList());

        var parent = selector.Text[..(selector.Text.LastIndexOf('/') + 1)];
        throw new SelectorNotFoundException(
            Sections.Select(e => e.Selector).Where(s => s.StartsWith(parent, StringComparison.Ordinal)).ToList());
    }

    /// <summary>
    /// Consumes the selector segment by segment against the section's heading names, which may hold a '/':
    /// a segment is the literal name first, then (when <paramref name="ordinals"/>) the name with its 1-based ordinal.
    /// </summary>
    private static bool Matches(string text, IReadOnlyList<SectionSegment> path, bool ordinals, int at = 0)
    {
        var segment = path[at];
        var forms = ordinals ? new[] { segment.Name, $"{segment.Name}[{segment.Ordinal}]" } : [segment.Name];
        foreach (var form in forms)
        {
            if (!text.StartsWith(form, StringComparison.Ordinal)) continue;
            var rest = text[form.Length..];
            if (at == path.Count - 1 ? rest.Length == 0 : rest.StartsWith('/') && Matches(rest[1..], path, ordinals, at + 1))
                return true;
        }
        return false;
    }

    private IReadOnlyList<SectionEntry> Sections => _sectionEntries ??= IndexSections();

    /// <summary>
    /// Numbers each heading among its same-named siblings (1-based). A selector segment carries its
    /// ordinal only where the name is shared, so an outline entry is the shortest selector that resolves to it.
    /// </summary>
    private List<SectionEntry> IndexSections()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var ancestors = new List<(string Fqn, SectionSegment[] Path)>();
        var indexed = new List<(RaggableNode Node, SectionSegment[] Path)>();
        var rootPrefix = _markdownPath + "::";
        foreach (var section in _sections)
        {
            var prefix = section.Fqn[..^section.Name.Length];
            var parentFqn = prefix == rootPrefix ? null : prefix[..^1];
            while (ancestors.Count > 0 && ancestors[^1].Fqn != parentFqn) ancestors.RemoveAt(ancestors.Count - 1);
            var parent = ancestors.Count > 0 ? ancestors[^1].Path : [];
            var key = string.Join('/', parent.Select(s => $"{s.Name}[{s.Ordinal}]")) + "\0" + section.Name;
            counts[key] = counts.GetValueOrDefault(key) + 1;
            SectionSegment[] path = [.. parent, new SectionSegment(section.Name, counts[key], key)];
            ancestors.Add((section.Fqn, path));
            indexed.Add((section, path));
        }
        return indexed.Select(i => new SectionEntry(
            i.Node,
            i.Path,
            string.Join('/', i.Path.Select(s => counts[s.Key] > 1 ? $"{s.Name}[{s.Ordinal}]" : s.Name)))).ToList();
    }

    private sealed record SectionSegment(string Name, int Ordinal, string Key);

    private sealed record SectionEntry(RaggableNode Node, IReadOnlyList<SectionSegment> Path, string Selector);

    private NodeRange SectionRange(SectionEntry entry)
    {
        var section = entry.Node;
        var text = section.SourceSnippet.TrimEnd('\r', '\n');
        var startByte = _bomLength + section.Range.StartByte;
        return new NodeRange(
            startByte, startByte + Encoding.UTF8.GetByteCount(text),
            section.Range.StartLine, section.Range.StartLine + text.Count(c => c == '\n'));
    }

    private StructuralNode BuildSection(SectionEntry entry)
    {
        var range = SectionRange(entry);
        var slice = _bytes.AsSpan(range.StartByte, range.EndByte - range.StartByte);
        return new StructuralNode(
            entry.Selector, "section", range, Encoding.UTF8.GetString(slice), Convert.ToHexStringLower(SHA256.HashData(slice)));
    }

    private StructuralNode ResolveReadable(NodeSelector selector)
    {
        var matches = _declarations
            .Where(d => d.Name == selector.Name
                && (selector.ParameterTypes is null
                    || (d.Parameters is not null && d.Parameters.SequenceEqual(selector.ParameterTypes))))
            .ToList();
        if (matches.Count == 1)
        {
            var d = matches[0];
            return Build(d.Selector, d.Kind, d.Span, d.Siblings, d.Index);
        }
        if (matches.Count > 1) throw new AmbiguousSelectorException(matches.Select(m => m.Selector).ToList());

        var last = selector.Name[(selector.Name.LastIndexOf('.') + 1)..];
        throw new SelectorNotFoundException(
            _declarations.Where(d => d.Name.EndsWith(last, StringComparison.Ordinal)).Select(d => d.Selector).ToList());
    }

    private StructuralNode ResolvePath(NodeSelector selector, PathNode root)
    {
        var steps = selector.Steps;
        if (steps[0] != root.Type) throw new SelectorNotFoundException([root.Type]);
        var node = root;
        IReadOnlyList<PathNode> siblings = [root];
        var index = 0;
        for (var i = 1; i < steps.Count; i += 2)
        {
            var named = node.NamedChildren;
            if (!int.TryParse(steps[i], out var at) || at < 0 || at >= named.Count || named[at].Type != steps[i + 1])
                throw new SelectorNotFoundException(named.Select((c, n) => $"{steps[i - 1]}/{n}/{c.Type}").ToList());
            siblings = named;
            index = at;
            node = named[at];
        }
        return Build(selector.Text, node.Type, node, siblings, index);
    }

    private void Walk(PathNode parent, string prefix)
    {
        var named = parent.NamedChildren;
        for (var i = 0; i < named.Count; i++)
        {
            var child = named[i];
            if (Bodies.Contains(child.Type))
            {
                Walk(child, prefix);
                continue;
            }
            var declared = child.Type == "export_statement" ? child.Ts.ChildByField("declaration") : child.Ts;
            if (declared is null || !(Containers.Contains(declared.Type) || Members.Contains(declared.Type))) continue;
            var name = declared.ChildByField("name")?.Text;
            if (name is null) continue;

            var fullName = prefix.Length == 0 ? name : $"{prefix}.{name}";
            var parameters = SignatureExtractor.ParameterTypes(declared);
            var selector = parameters is null ? fullName : $"{fullName}({string.Join(',', parameters)})";
            _declarations.Add(new Declaration(fullName, selector, declared.Type, parameters, child, named, i));
            if (Containers.Contains(declared.Type)) Walk(child.Type == "export_statement" ? PathNode.Of(declared) : child, fullName);
        }
    }

    private NodeRange DeclarationRange(PathNode span, IReadOnlyList<PathNode> siblings, int index)
    {
        var first = index;
        while (first > 0
            && siblings[first - 1].Type == "comment"
            && siblings[first].StartRow - siblings[first - 1].EndRow <= 1)
        {
            first--;
        }

        var start = siblings[first];
        var startByte = _bomLength + Encoding.UTF8.GetByteCount(_source.AsSpan(0, start.StartIndex));
        var endByte = _bomLength + Encoding.UTF8.GetByteCount(_source.AsSpan(0, span.EndIndex));
        return new NodeRange(startByte, endByte, start.StartRow + 1, span.EndRow + 1);
    }

    private StructuralNode Build(string selector, string kind, PathNode span, IReadOnlyList<PathNode> siblings, int index)
    {
        var range = DeclarationRange(span, siblings, index);
        var slice = _bytes.AsSpan(range.StartByte, range.EndByte - range.StartByte);
        return new StructuralNode(
            selector, kind, range, Encoding.UTF8.GetString(slice), Convert.ToHexStringLower(SHA256.HashData(slice)));
    }

    private sealed record Declaration(
        string Name, string Selector, string Kind, IReadOnlyList<string>? Parameters,
        PathNode Span, IReadOnlyList<PathNode> Siblings, int Index);

    /// <summary>Snapshot of a tree-sitter node; <see cref="Ts"/> is only valid while the tree is alive (load).</summary>
    private sealed class PathNode
    {
        private PathNode(TsNode node)
        {
            Ts = node;
            Type = node.Type;
            StartIndex = node.StartIndex;
            EndIndex = node.EndIndex;
            StartRow = node.StartPosition.Row;
            EndRow = node.EndPosition.Row;
            NamedChildren = node.Children.Where(c => c.IsNamed).Select(c => new PathNode(c)).ToList();
        }

        public TsNode Ts { get; }

        public string Type { get; }

        public int StartIndex { get; }

        public int EndIndex { get; }

        public int StartRow { get; }

        public int EndRow { get; }

        public IReadOnlyList<PathNode> NamedChildren { get; }

        public static PathNode Of(TsNode node) => new(node);
    }
}

internal sealed class AmbiguousSelectorException(IReadOnlyList<string> candidates)
    : Exception($"Ambiguous selector; candidates: {string.Join(", ", candidates)}")
{
    public IReadOnlyList<string> Candidates { get; } = candidates;
}

internal sealed class SelectorNotFoundException(IReadOnlyList<string> suggestions)
    : Exception($"Selector not found; suggestions: {string.Join(", ", suggestions)}")
{
    public IReadOnlyList<string> Suggestions { get; } = suggestions;
}

internal sealed class UnsupportedExtensionException(string extension)
    : Exception($"No tree-sitter grammar for extension '{extension}'")
{
    public string Extension { get; } = extension;
}
