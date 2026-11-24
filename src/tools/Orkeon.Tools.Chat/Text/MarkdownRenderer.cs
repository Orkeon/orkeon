using System.Net;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Orkeon.Tools.Chat.Text;

/// <summary>
/// Turns what a model wrote into the HTML a chat message holds. Raw HTML is encoded, so the text
/// can hold neither a tag nor a mention; a link keeps its address only when it is http or https,
/// and an image becomes a link, since fetching it would tell its host who read the message.
/// </summary>
internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseAutoLinks()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .Build();

    /// <summary>Renders Markdown.</summary>
    public static string FromMarkdown(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        MarkdownDocument document;
        try
        {
            document = Markdown.Parse(markdown, Pipeline);
        }
        catch (ArgumentException ex)
        {
            // Markdig's signal for a text nested beyond its depth limit.
            throw new ChatToolException(ChatErrorCode.InvalidRequest, "The Markdown nests too deeply; nothing was posted. Simplify it, or pass `format` text.", ex);
        }

        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            link.IsImage = false;
            link.GetDynamicUrl = null;
            link.Title = null;
            if (IsWebAddress(link.Url))
                continue;
            while (link.FirstChild is { } child)
            {
                child.Remove();
                link.InsertBefore(child);
            }

            link.Remove();
        }

        foreach (var link in document.Descendants<AutolinkInline>().Where(link => link.IsEmail || !IsWebAddress(link.Url)).ToList())
            link.ReplaceBy(new LiteralInline(link.Url));
        return document.ToHtml(Pipeline).Trim();
    }

    /// <summary>Renders plain text.</summary>
    public static string FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WebUtility.HtmlEncode(text.Replace("\r\n", "\n", StringComparison.Ordinal))
            .Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);
}
