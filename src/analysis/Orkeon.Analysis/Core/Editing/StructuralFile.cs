#pragma warning disable CA1032, CA1064 // the exceptions carry one domain constructor
using System.Runtime.Serialization;
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

/// <summary>A direct child of a type body: its tree-sitter kind, its own source text and its range (leading comments included).</summary>
internal sealed record BodyMember(string Kind, string Text, NodeRange Range);

internal sealed record StructuralNode(string Selector, string Kind, NodeRange Range, string Text, string Sha256);

/// <summary>
/// A parsed source file whose declarations are addressable by selector, in any grammar of
/// <see cref="LanguageRegistry.Parseable"/>. The tree is snapshotted once at load and discarded;
/// byte offsets are canonical (BOM and line endings kept). A grammar whose declarations the
/// tables below do not name outlines its top-level nodes by tree-sitter path instead.
/// </summary>
internal sealed class StructuralFile
{
    // Node kinds are per grammar; one set serves them all, as no two grammars give a kind two meanings here.
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        // C#, TypeScript, JavaScript, Java
        "namespace_declaration", "file_scoped_namespace_declaration", "class_declaration", "struct_declaration",
        "interface_declaration", "record_declaration", "record_struct_declaration", "enum_declaration",
        "abstract_class_declaration", "internal_module", "module",
        // Python, Scala, Ruby (module above), Rust, C, C++, PHP, Julia, JSON
        "class_definition", "object_definition", "trait_definition", "class", "mod_item", "trait_item", "impl_item",
        "namespace_definition", "class_specifier", "struct_specifier", "trait_declaration", "module_definition", "pair",
    };

    private static readonly HashSet<string> Members = new(StringComparer.Ordinal)
    {
        "method_declaration", "constructor_declaration", "property_declaration", "method_definition",
        "method_signature", "function_declaration", "public_field_definition", "property_signature",
        "type_alias_declaration",
        // Python, C, C++, PHP, Bash, Scala, Julia, Ruby, Rust, Go
        "function_definition", "method", "singleton_method", "function_item", "function_signature_item", "struct_item",
        "enum_item", "type_item", "const_item", "static_item", "type_spec", "enum_specifier",
    };

    // A declaration wrapped in another node: the wrapper is the span, the inner node the declaration.
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
    {
        "export_statement", "decorated_definition", "template_declaration", "type_declaration",
    };

    internal const string MarkdownLanguage = "markdown";

    private static readonly HashSet<string> Bodies = new(StringComparer.Ordinal)
    {
        "declaration_list", "class_body", "interface_body", "object_type", "enum_body", "statement_block",
        "enum_member_declaration_list",
        "block", "body_statement", "field_declaration_list", "template_body", "compound_statement", "object",
    };

    private readonly byte[] _bytes;
    private readonly string _source;
    private readonly int _bomLength;
    private readonly PathNode? _root;
    private readonly IReadOnlyList<RaggableNode> _sections;
    private readonly List<Declaration> _declarations = [];
    private string _markdownPath = string.Empty;
    private IReadOnlyList<RaggableNode> _fences = [];
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
        return LanguageRegistry.Parseable.FirstOrDefault(g => g.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            ?? throw new UnsupportedExtensionException(extension);
    }

    private static StructuralFile Parse(TreeSitterParserPool pool, byte[] bytes, string virtualPath, string language)
    {
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var source = Encoding.UTF8.GetString(bytes.AsSpan(bom));
        if (language == MarkdownLanguage)
        {
            var mapped = new MarkdownDocumentMapper().Map(virtualPath, source);
            var sections = mapped.Where(n => n.Kind == UniversalNodeKind.Section).ToList();
            var fences = mapped.Where(n => n.Kind == UniversalNodeKind.CodeFence).OrderBy(n => n.Range.StartByte).ToList();
            return new StructuralFile(bytes, source, bom, null, sections) { _markdownPath = virtualPath, _fences = fences, Language = language };
        }

        using var parsed = pool.Parse(source, language);
        var root = PathNode.Of(parsed.Root);
        var file = new StructuralFile(bytes, source, bom, root, []) { Language = language };
        file.Walk(root, string.Empty);
        if (file._declarations.Count == 0) file.OutlineByPath(root);
        return file;
    }

    public IReadOnlyList<StructuralNode> Outline() => _root is null
        ? Sections.Select(e => new StructuralNode(e.Selector, "section", SectionRange(e), string.Empty, string.Empty)).ToList()
        : _declarations.Select(d => new StructuralNode(d.Selector, d.Kind, DeclarationRange(d.Span, d.Siblings, d.Index), string.Empty, string.Empty)).ToList();

    /// <summary>The fenced code blocks of a Markdown file, in document order (kind CodeFence).</summary>
    public IReadOnlyList<StructuralNode> CodeFences() => _fences.Select(BuildFence).ToList();

    private StructuralNode BuildFence(RaggableNode fence)
    {
        var text = fence.SourceSnippet.TrimEnd('\r', '\n');
        var startByte = _bomLength + fence.Range.StartByte;
        var range = new NodeRange(
            startByte, startByte + Encoding.UTF8.GetByteCount(text),
            fence.Range.StartLine, fence.Range.StartLine + text.Count(c => c == '\n'));
        return new StructuralNode(
            string.Empty, "CodeFence", range, text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    public StructuralNode Resolve(NodeSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (_root is null) return ResolveSection(selector);
        return selector.IsTreeSitterPath ? ResolvePath(selector, _root) : ResolveReadable(selector);
    }

    /// <summary>The members written directly in the braced body of the declaration <paramref name="declaration"/> (comments excluded), in source order.</summary>
    public IReadOnlyList<BodyMember> BodyMembers(StructuralNode declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var found = _declarations.FirstOrDefault(d => d.Selector == declaration.Selector);
        var body = found is null ? null : FindBody(found.Span);
        if (body is null) return [];
        var named = body.NamedChildren;
        var members = new List<BodyMember>(named.Count);
        for (var i = 0; i < named.Count; i++)
        {
            if (IsComment(named[i].Type)) continue;
            // tree-sitter-typescript leaves the terminating semicolon of a field outside its node.
            var semicolon = named[i].Type == "public_field_definition" && named[i].EndIndex < _source.Length && _source[named[i].EndIndex] == ';';
            var range = DeclarationRange(named[i], named, i);
            members.Add(new BodyMember(
                named[i].Type,
                _source[named[i].StartIndex..(named[i].EndIndex + (semicolon ? 1 : 0))],
                semicolon ? range with { EndByte = range.EndByte + 1 } : range));
        }

        return members;
    }

    private static PathNode? FindBody(PathNode node)
    {
        foreach (var child in node.NamedChildren)
        {
            if (Bodies.Contains(child.Type)) return child;
            if (Containers.Contains(child.Type) && FindBody(child) is { } inner) return inner;
        }

        return null;
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
            var declared = DeclaredNode(child);
            var name = declared is null ? null : NameOf(declared);
            if (declared is null || string.IsNullOrEmpty(name)) continue;

            var fullName = prefix.Length == 0 ? name : $"{prefix}.{name}";
            var inner = declared.Type == child.Type ? child : PathNode.Of(declared);
            // A Rust impl block adds members to its type: they are listed under the type's name, the block itself is not.
            if (declared.Type == "impl_item")
            {
                Walk(inner, fullName);
                continue;
            }

            _declarations.Add(DeclarationOf(fullName, declared, child, named, i));
            if (Containers.Contains(declared.Type)) Walk(inner, fullName);
        }
    }

    // The container or member a child declares, looking through a wrapper; null for anything else.
    private static TsNode? DeclaredNode(PathNode child)
    {
        var declared = child.Ts;
        if (Wrappers.Contains(child.Type))
        {
            declared = child.Ts.ChildByField("declaration") ?? child.Ts.ChildByField("definition")
                ?? child.Ts.NamedChildren.FirstOrDefault(c => Containers.Contains(c.Type) || Members.Contains(c.Type));
        }
        if (declared is null) return null;
        return Containers.Contains(declared.Type) || Members.Contains(declared.Type) ? declared : null;
    }

    // The declared name: the name field in most grammars, the declarator in C and C++, the receiver-qualified
    // name of a Go method, the implemented type of a Rust impl, the key of a JSON pair, the callee of a Julia signature.
    private static string? NameOf(TsNode declared)
    {
        if (declared.Type == "pair")
        {
            var key = declared.ChildByField("key")?.Text;
            return key is null ? null : key.Trim('"');
        }
        if (declared.Type == "impl_item") return declared.ChildByField("type")?.Text;

        var name = declared.ChildByField("name")?.Text;
        if (name is not null)
        {
            var receiver = declared.Type == "method_declaration" ? declared.ChildByField("receiver") : null;
            var parameter = receiver is { NamedChildren.Count: > 0 } ? receiver.NamedChildren[0] : null;
            var receiverType = parameter?.ChildByField("type")?.Text.TrimStart('*');
            return receiverType is null ? name : $"{receiverType}.{name}";
        }
        if (declared.ChildByField("declarator") is not null) return CDeclarators.Innermost(declared)?.Text;

        var signature = declared.NamedChildren.FirstOrDefault(c => c.Type == "signature");
        var call = signature is { NamedChildren.Count: > 0 } ? signature.NamedChildren[0] : null;
        return call is { Type: "call_expression", NamedChildren.Count: > 0 } ? call.NamedChildren[0].Text : null;
    }

    // A grammar with no declaration the tables know: its top-level nodes, through a lone wrapping node, by path.
    private void OutlineByPath(PathNode root)
    {
        var node = root;
        var path = $"@ts:{root.Type}";
        var named = Uncommented(root);
        while (named.Count == 1 && named[0].NamedChildren.Count > 0)
        {
            path += $"/{IndexIn(node, named[0])}/{named[0].Type}";
            node = named[0];
            named = Uncommented(node);
        }

        foreach (var child in named)
        {
            var index = IndexIn(node, child);
            var selector = $"{path}/{index}/{child.Type}";
            _declarations.Add(new Declaration(selector, selector, child.Type, null, child, node.NamedChildren, index));
        }
    }

    private static List<PathNode> Uncommented(PathNode node) => node.NamedChildren.Where(c => !IsComment(c.Type)).ToList();

    private static int IndexIn(PathNode parent, PathNode child)
    {
        var siblings = parent.NamedChildren;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (ReferenceEquals(siblings[i], child)) return i;
        }
        return -1;
    }

    /// <summary>A comment node in any grammar: <c>comment</c>, or <c>line_comment</c>/<c>block_comment</c> (Rust, Java).</summary>
    internal static bool IsComment(string kind) => kind == "comment" || kind.EndsWith("_comment", StringComparison.Ordinal);

    private static Declaration DeclarationOf(string fullName, TsNode declared, PathNode child, IReadOnlyList<PathNode> siblings, int index)
    {
        if (!SignatureExtractor.TryGetParameterTypes(declared, out var parameters))
            return new Declaration(fullName, fullName, declared.Type, null, child, siblings, index);
        var selector = $"{fullName}({string.Join(',', parameters)})";
        return new Declaration(fullName, selector, declared.Type, parameters, child, siblings, index);
    }

    private NodeRange DeclarationRange(PathNode span, IReadOnlyList<PathNode> siblings, int index)
    {
        var first = index;
        while (first > 0
            && IsComment(siblings[first - 1].Type)
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

        public List<PathNode> NamedChildren { get; }

        public static PathNode Of(TsNode node) => new(node);
    }
}

[Serializable]
public sealed class AmbiguousSelectorException : Exception
{
    private const string CandidatesKey = "Candidates";

    public AmbiguousSelectorException(IReadOnlyList<string> candidates)
        : base($"Ambiguous selector; candidates: {string.Join(", ", candidates)}")
    {
        Candidates = candidates;
    }

    public IReadOnlyList<string> Candidates { get; }

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private AmbiguousSelectorException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Candidates = (string[]?)info.GetValue(CandidatesKey, typeof(string[])) ?? [];
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(CandidatesKey, Candidates.ToArray());
    }
}

[Serializable]
public sealed class SelectorNotFoundException : Exception
{
    private const string SuggestionsKey = "Suggestions";

    public SelectorNotFoundException(IReadOnlyList<string> suggestions)
        : base($"Selector not found; suggestions: {string.Join(", ", suggestions)}")
    {
        Suggestions = suggestions;
    }

    public IReadOnlyList<string> Suggestions { get; }

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private SelectorNotFoundException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Suggestions = (string[]?)info.GetValue(SuggestionsKey, typeof(string[])) ?? [];
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(SuggestionsKey, Suggestions.ToArray());
    }
}

[Serializable]
public sealed class UnsupportedExtensionException : Exception
{
    private const string ExtensionKey = "Extension";

    public UnsupportedExtensionException(string extension)
        : base($"No tree-sitter grammar for extension '{extension}'")
    {
        Extension = extension;
    }

    public string Extension { get; }

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private UnsupportedExtensionException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Extension = info.GetString(ExtensionKey) ?? string.Empty;
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(ExtensionKey, Extension);
    }
}
