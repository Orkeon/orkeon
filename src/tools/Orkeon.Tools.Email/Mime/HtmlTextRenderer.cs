using System.Text;
using HtmlAgilityPack;

namespace Orkeon.Tools.Email.Mime;

/// <summary>An HTML body rendered as text.</summary>
/// <param name="Text">The visible text, one paragraph per block element.</param>
/// <param name="HadHiddenContent">
/// Whether the HTML hid text from a human reader (<c>display:none</c>, zero-size fonts,
/// <c>hidden</c>…). Hidden text is left out of <paramref name="Text"/> and reported instead:
/// it is where an injection aimed at an agent hides, and where newsletters keep their preheader.
/// </param>
internal sealed record RenderedHtml(string Text, bool HadHiddenContent);

/// <summary>
/// Renders an HTML mail body as plain text for an agent: scripts and styles dropped, blocks
/// on their own lines, link targets kept next to their text. The walk is iterative, so a
/// hostile message nesting elements thousands deep cannot exhaust the stack.
/// </summary>
internal static class HtmlTextRenderer
{
    /// <summary>What a body nested beyond <see cref="MaxNesting"/> reads as: its content is left out.</summary>
    internal const string TooDeepNotice = "[The HTML body was not rendered: it nests elements more than 5000 levels deep.]";

    private const int MaxLinkLength = 100;

    /// <summary>
    /// The deepest nesting parsed. HtmlAgilityPack's parser itself recurses once per level (and
    /// slows quadratically): real mail nests a few dozen levels, only a hostile message thousands.
    /// </summary>
    private const int MaxNesting = 5000;

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "noscript", "template", "svg", "object", "iframe", "embed", "meta", "link",
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "table", "blockquote", "section", "article",
        "header", "footer", "ul", "ol", "hr", "pre", "dl", "dt", "dd", "center", "address", "form", "fieldset",
        "main", "nav", "aside", "figure", "figcaption", "tbody", "thead", "tfoot", "caption",
    };

    private static readonly string[] HiddenStyleMarkers =
    [
        "display:none", "visibility:hidden", "opacity:0;", "font-size:0;", "font-size:0px", "max-height:0",
        "mso-hide:all", "width:0;", "height:0;",
    ];

    /// <summary>Renders <paramref name="html"/>.</summary>
    public static RenderedHtml Render(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlDocument { OptionMaxNestedChildNodes = MaxNesting };
        try
        {
            document.LoadHtml(html);
        }
        catch (Exception ex) when (ex.GetType() == typeof(Exception))
        {
            // HtmlAgilityPack signals its nesting limit with a bare Exception. The body is left
            // out, so it is reported as hidden content.
            return new RenderedHtml(TooDeepNotice, HadHiddenContent: true);
        }

        var text = new StringBuilder(Math.Min(html.Length, 64 * 1024));
        var hidden = false;
        var stack = new Stack<(HtmlNode Node, bool Closing)>();
        stack.Push((document.DocumentNode, false));

        while (stack.Count > 0)
        {
            var (node, closing) = stack.Pop();
            if (closing)
            {
                Close(node, text);
                continue;
            }

            switch (node.NodeType)
            {
                case HtmlNodeType.Text:
                    text.Append(HtmlEntity.DeEntitize(((HtmlTextNode)node).Text));
                    break;

                case HtmlNodeType.Element when SkippedElements.Contains(node.Name):
                    break;

                case HtmlNodeType.Element when IsHidden(node):
                    hidden |= !string.IsNullOrWhiteSpace(TextOf(node));
                    break;

                case HtmlNodeType.Element:
                    if (Open(node, text))
                        PushChildren(node, stack, closeAfter: true);
                    break;

                case HtmlNodeType.Document:
                    PushChildren(node, stack, closeAfter: false);
                    break;
            }
        }

        return new RenderedHtml(Normalize(text.ToString()), hidden);
    }

    /// <summary>Writes what an element contributes before its children; false when it has no children to walk.</summary>
    private static bool Open(HtmlNode node, StringBuilder text)
    {
        if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            text.Append('\n');
            return false;
        }

        if (node.Name.Equals("img", StringComparison.OrdinalIgnoreCase))
        {
            var alt = node.GetAttributeValue("alt", string.Empty).Trim();
            if (alt.Length > 0)
                text.Append('[').Append(HtmlEntity.DeEntitize(alt)).Append(']');
            return false;
        }

        if (BlockElements.Contains(node.Name))
            text.Append('\n');
        if (node.Name.Equals("li", StringComparison.OrdinalIgnoreCase))
            text.Append("- ");
        return true;
    }

    /// <summary>Writes what an element contributes after its children.</summary>
    private static void Close(HtmlNode node, StringBuilder text)
    {
        if (node.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
            AppendLinkTarget(node, text);
        else if (node.Name.Equals("td", StringComparison.OrdinalIgnoreCase) || node.Name.Equals("th", StringComparison.OrdinalIgnoreCase))
            text.Append(' ');

        if (BlockElements.Contains(node.Name))
            text.Append('\n');
    }

    private static void PushChildren(HtmlNode node, Stack<(HtmlNode Node, bool Closing)> stack, bool closeAfter)
    {
        if (closeAfter)
            stack.Push((node, true));
        for (var i = node.ChildNodes.Count - 1; i >= 0; i--)
            stack.Push((node.ChildNodes[i], false));
    }

    private static bool IsHidden(HtmlNode node)
    {
        if (node.Attributes.Contains("hidden"))
            return true;

        var style = node.GetAttributeValue("style", string.Empty);
        if (style.Length == 0)
            return false;

        // Each marker is matched at the start of a declaration, so "width:0" does not fire on
        // "border-width:0", a style that hides nothing and wraps whole newsletters.
        var compact = ";" + string.Concat(style.Where(c => !char.IsWhiteSpace(c))) + ";";
        return HiddenStyleMarkers.Any(marker => compact.Contains(";" + marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The raw text under <paramref name="node"/>, gathered with an explicit stack: HtmlAgilityPack's
    /// <c>InnerText</c> recurses once per level and a deeply nested message overflows the stack.
    /// </summary>
    private static string TextOf(HtmlNode node)
    {
        var text = new StringBuilder();
        var pending = new Stack<HtmlNode>();
        pending.Push(node);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is HtmlTextNode textNode)
                text.Append(textNode.Text);
            for (var i = current.ChildNodes.Count - 1; i >= 0; i--)
                pending.Push(current.ChildNodes[i]);
        }

        return text.ToString();
    }

    private static void AppendLinkTarget(HtmlNode anchor, StringBuilder text)
    {
        var href = anchor.GetAttributeValue("href", string.Empty).Trim();
        if (!(href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        href = HtmlEntity.DeEntitize(href);
        var label = HtmlEntity.DeEntitize(TextOf(anchor)).Trim();
        if (string.Equals(label, href, StringComparison.OrdinalIgnoreCase))
            return;

        if (href.Length > MaxLinkLength)
            href = string.Concat(href.AsSpan(0, MaxLinkLength), "…");
        text.Append(" (").Append(href).Append(')');
    }

    /// <summary>Collapses runs of blanks inside lines and of empty lines between paragraphs.</summary>
    internal static string Normalize(string raw)
    {
        var lines = raw.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace(' ', ' ')
            .Split('\n');

        var output = new StringBuilder(raw.Length);
        var blankRun = 0;
        foreach (var line in lines)
        {
            var collapsed = CollapseSpaces(line);
            if (collapsed.Length == 0)
            {
                blankRun++;
                continue;
            }

            if (output.Length > 0)
                output.Append(blankRun > 0 ? "\n\n" : "\n");
            output.Append(collapsed);
            blankRun = 0;
        }

        return output.ToString();
    }

    private static string CollapseSpaces(string line)
    {
        var builder = new StringBuilder(line.Length);
        var pendingSpace = false;
        foreach (var c in line)
        {
            if (c is ' ' or '\t')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            builder.Append(c);
            pendingSpace = false;
        }

        return builder.ToString();
    }
}
