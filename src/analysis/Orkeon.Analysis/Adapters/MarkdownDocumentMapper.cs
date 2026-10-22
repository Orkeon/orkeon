using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Orkeon.Analysis.Adapters;

public sealed class MarkdownDocumentMapper
{
    private const string Language = "markdown";
    private const string TargetPrefix = "target:";

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePreciseSourceLocation()
        .Build();

    private readonly IDeserializer _yaml = new DeserializerBuilder().Build();

    public IReadOnlyList<RaggableNode> Map(string virtualPath, string content)
    {
        ArgumentNullException.ThrowIfNull(virtualPath);
        ArgumentNullException.ThrowIfNull(content);

        var document = Markdown.Parse(content, _pipeline);
        var bytes = ByteOffsets(content);
        var nodes = new List<RaggableNode>();

        var headings = document.Descendants<HeadingBlock>().ToList();
        var stack = new List<(int Level, string Fqn)>();
        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            var name = HeadingText(heading);
            while (stack.Count > 0 && stack[^1].Level >= heading.Level) stack.RemoveAt(stack.Count - 1);
            var fqn = stack.Count > 0 ? $"{stack[^1].Fqn}/{name}" : $"{virtualPath}::{name}";
            stack.Add((heading.Level, fqn));

            var next = headings.Skip(i + 1).FirstOrDefault(h => h.Level <= heading.Level);
            var end = next is null ? content.Length : next.Span.Start;
            nodes.Add(Create(virtualPath, UniversalNodeKind.Section, name, fqn, NodeLevel.L3_Symbol,
                content, bytes, heading.Span.Start, end, heading.Line));
        }

        foreach (var front in document.Descendants<YamlFrontMatterBlock>())
        {
            var node = Create(virtualPath, UniversalNodeKind.FrontMatter, "front-matter", string.Empty, NodeLevel.L4_Statement,
                content, bytes, front.Span.Start, front.Span.End + 1, front.Line);
            foreach (var key in YamlKeys(front.Lines.ToString()))
                node.TagsMutable[key] = string.Empty;
            nodes.Add(node);
        }

        foreach (var fence in document.Descendants<FencedCodeBlock>())
        {
            var language = fence.Info ?? string.Empty;
            var node = Create(virtualPath, UniversalNodeKind.CodeFence, language.Length > 0 ? language : "code", string.Empty,
                NodeLevel.L4_Statement, content, bytes, fence.Span.Start, fence.Span.End + 1, fence.Line);
            if (language.Length > 0) node.TagsMutable[language] = string.Empty;
            nodes.Add(node);
        }

        foreach (var link in document.Descendants<LinkInline>().Where(l => !l.IsImage))
        {
            var label = string.Concat(link.Descendants<LiteralInline>().Select(l => l.Content.ToString()));
            var node = Create(virtualPath, UniversalNodeKind.Link, label, string.Empty, NodeLevel.L4_Statement,
                content, bytes, link.Span.Start, link.Span.End + 1, link.Line);
            node.TagsMutable[TargetPrefix + link.Url] = string.Empty;
            nodes.Add(node);
        }

        AttachToSections(nodes);
        return nodes;
    }

    [SuppressMessage("Performance", "CA1822", Justification = "Instance member by public contract: callers resolve through the mapper they mapped with.")]
    public async Task<IReadOnlyList<RaggableNode>> ResolveLinksAsync(
        IReadOnlyList<RaggableNode> nodes, string virtualPath, IRaggableStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(virtualPath);
        ArgumentNullException.ThrowIfNull(store);

        foreach (var link in nodes.Where(n => n.Kind == UniversalNodeKind.Link))
        {
            var target = link.Tags.Keys.Single(k => k.StartsWith(TargetPrefix, StringComparison.Ordinal))[TargetPrefix.Length..]
                .Split('#')[0];
            if (target.Length == 0 || target.Contains(':', StringComparison.Ordinal)) continue;

            var found = await store.GetModuleByPathAsync(ResolvePath(virtualPath, target), cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                var candidates = await store.FindByLocalNameAsync(target, cancellationToken).ConfigureAwait(false);
                found = candidates.Count > 0 ? candidates[0] : null;
            }
            if (found is not null)
                link.TagsMutable[$"resolved:{found.Id}"] = found.Fqn;
        }

        return nodes;
    }

    private Dictionary<string, object?>.KeyCollection YamlKeys(string yaml)
    {
        try
        {
            return (_yaml.Deserialize<Dictionary<string, object?>>(yaml) ?? []).Keys;
        }
        catch (YamlException)
        {
            return new Dictionary<string, object?>().Keys;
        }
    }

    private static string HeadingText(HeadingBlock heading) =>
        string.Concat(heading.Inline?.Descendants().Select(i => i switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            _ => string.Empty,
        }) ?? []);

    private static string ResolvePath(string virtualPath, string target)
    {
        var origin = target.StartsWith('/') ? target : virtualPath[..(virtualPath.LastIndexOf('/') + 1)] + target;
        var segments = new List<string>();
        foreach (var segment in origin.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment != "..") segments.Add(segment);
            else if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
        }
        return "/" + string.Join('/', segments);
    }

    private static int[] ByteOffsets(string content)
    {
        var offsets = new int[content.Length + 1];
        for (var i = 0; i < content.Length; i++)
        {
            var width = char.IsHighSurrogate(content[i]) ? 4
                : char.IsLowSurrogate(content[i]) ? 0
                : Encoding.UTF8.GetByteCount(content.AsSpan(i, 1));
            offsets[i + 1] = offsets[i] + width;
        }
        return offsets;
    }

    private static RaggableNode Create(
        string path, UniversalNodeKind kind, string name, string fqn, NodeLevel level,
        string content, int[] bytes, int start, int endExclusive, int line)
    {
        var end = Math.Min(endExclusive, content.Length);
        var snippet = content[start..end];
        return new RaggableNode
        {
            Id = $"{path}#{kind}@{bytes[start]}",
            Kind = kind,
            Name = name,
            Fqn = fqn,
            VirtualFilePath = path,
            Range = new NodeRange(bytes[start], bytes[end], line + 1, line + 1 + snippet.Count(c => c == '\n'), 1),
            Level = level,
            Language = Language,
            SourceSnippet = snippet,
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snippet))),
        };
    }

    private static void AttachToSections(List<RaggableNode> nodes)
    {
        var sections = nodes.Where(n => n.Kind == UniversalNodeKind.Section).ToList();
        foreach (var node in nodes.Where(n => n.Kind != UniversalNodeKind.Section))
        {
            var parent = sections.LastOrDefault(s => s.Range.StartByte <= node.Range.StartByte && node.Range.EndByte <= s.Range.EndByte);
            if (parent is null) continue;
            node.ParentId = parent.Id;
            parent.ChildrenIdsMutable.Add(node.Id);
        }
    }
}
