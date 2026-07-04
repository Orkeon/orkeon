using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Parsing;

/// <summary>
/// Markdig implementation of IMarkdownParser.
/// </summary>
public partial class MarkdownParser : IMarkdownParser
{
    private readonly MarkdownPipeline _pipeline;

    /// <summary>Initializes a new instance of <see cref="MarkdownParser"/>.</summary>
    public MarkdownParser()
    {
        // Pipeline for HTML conversion with all extensions
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseYamlFrontMatter()
            .Build();
    }

    /// <inheritdoc />
    public string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, _pipeline);
    }

    /// <inheritdoc />
    public string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        // First pass: use regex to clean up the markdown
        var plainText = markdown;

        // Remove code blocks first (triple backticks)
        plainText = CodeBlockRegex().Replace(plainText, m =>
        {
            var content = m.Value.Replace("```", "", StringComparison.Ordinal).Trim();
            var lines = content.Split('\n');
            if (lines.Length > 1)
                return string.Join("\n", lines.Skip(1)); // Skip language identifier
            return content;
        });

        // Process the rest of markdown syntax
        plainText = HeaderRegex().Replace(plainText, ""); // Remove headers
        plainText = BoldRegex().Replace(plainText, "$1"); // Remove bold
        plainText = ItalicRegex().Replace(plainText, "$1"); // Remove italic
        plainText = LinkRegex().Replace(plainText, "$1"); // Remove links
        plainText = InlineCodeRegex().Replace(plainText, "$1"); // Remove inline code backticks
        plainText = BlockquoteRegex().Replace(plainText, ""); // Remove blockquotes
        plainText = ListMarkerRegex().Replace(plainText, ""); // Remove list markers

        return plainText.Trim();
    }

    /// <inheritdoc />
    public IDictionary<string, object> ExtractMetadata(string markdown)
    {
        var metadata = new Dictionary<string, object>();

        if (string.IsNullOrWhiteSpace(markdown))
            return metadata;

        var document = Markdown.Parse(markdown, _pipeline);
        var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

        if (yamlBlock != null)
        {
            var yaml = markdown.Substring(yamlBlock.Span.Start, yamlBlock.Span.Length);
            // Remove the --- delimiters
            yaml = YamlFrontMatterStartRegex().Replace(yaml, "");
            yaml = YamlFrontMatterEndRegex().Replace(yaml, "");

            // Simple YAML parsing (for basic key-value pairs)
            var lines = yaml.Split('\n');
            foreach (var line in lines)
            {
                var match = YamlKeyValueRegex().Match(line);
                if (match.Success)
                {
                    var key = match.Groups[1].Value.Trim();
                    var value = match.Groups[2].Value.Trim();
                    metadata[key] = value;
                }
            }
        }

        return metadata;
    }

    /// <inheritdoc />
    public string Sanitize(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        // Remove potentially dangerous content
        markdown = ScriptTagRegex().Replace(markdown, "");
        markdown = IframeTagRegex().Replace(markdown, "");
        markdown = JavascriptProtocolRegex().Replace(markdown, "");
        markdown = EventHandlerRegex().Replace(markdown, "");

        return markdown;
    }

    /// <inheritdoc />
    public IEnumerable<string> ExtractLinks(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return [];

        var document = Markdown.Parse(markdown, _pipeline);

        return document.Descendants<LinkInline>()
            .Select(link => link.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct()!;
    }

    /// <inheritdoc />
    public IEnumerable<(int Level, string Text)> ExtractHeaders(string markdown)
    {
        var headers = new List<(int Level, string Text)>();

        if (string.IsNullOrWhiteSpace(markdown))
            return headers;

        var document = Markdown.Parse(markdown, _pipeline);

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var textBuilder = new StringBuilder();
            if (heading.Inline != null)
            {
                foreach (var inline in heading.Inline)
                {
                    if (inline is LiteralInline literal)
                    {
                        textBuilder.Append(literal.Content);
                    }
                }
            }

            var text = textBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                headers.Add((heading.Level, text.Trim()));
            }
        }

        return headers;
    }

    [GeneratedRegex(@"```[^`]*```", RegexOptions.Singleline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"^#+\s*", RegexOptions.Multiline)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\*([^*]+)\*")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"^>\s*", RegexOptions.Multiline)]
    private static partial Regex BlockquoteRegex();

    [GeneratedRegex(@"^[\*\-]\s*", RegexOptions.Multiline)]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"^---\s*\n")]
    private static partial Regex YamlFrontMatterStartRegex();

    [GeneratedRegex(@"\n---\s*$")]
    private static partial Regex YamlFrontMatterEndRegex();

    [GeneratedRegex(@"^([^:]+):\s*(.+)$")]
    private static partial Regex YamlKeyValueRegex();

    [GeneratedRegex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<iframe[^>]*>.*?</iframe>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IframeTagRegex();

    [GeneratedRegex(@"javascript:", RegexOptions.IgnoreCase)]
    private static partial Regex JavascriptProtocolRegex();

    [GeneratedRegex(@"on\w+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerRegex();
}
