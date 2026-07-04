namespace Orkeon.Application.Interfaces.Infrastructure.Serialization;

/// <summary>
/// Interface for Markdown parsing operations.
/// </summary>
public interface IMarkdownParser
{
    /// <summary>
    /// Converts Markdown text to HTML.
    /// </summary>
    /// <param name="markdown">The Markdown text to convert.</param>
    /// <returns>The HTML representation.</returns>
    string ToHtml(string markdown);

    /// <summary>
    /// Converts Markdown text to plain text.
    /// </summary>
    /// <param name="markdown">The Markdown text to convert.</param>
    /// <returns>The plain text representation.</returns>
    string ToPlainText(string markdown);

    /// <summary>
    /// Extracts metadata from Markdown front matter.
    /// </summary>
    /// <param name="markdown">The Markdown text with front matter.</param>
    /// <returns>Dictionary of metadata key-value pairs.</returns>
    IDictionary<string, object> ExtractMetadata(string markdown);

    /// <summary>
    /// Sanitizes Markdown content for safe rendering.
    /// </summary>
    /// <param name="markdown">The Markdown text to sanitize.</param>
    /// <returns>The sanitized Markdown text.</returns>
    string Sanitize(string markdown);

    /// <summary>
    /// Extracts all links from Markdown content.
    /// </summary>
    /// <param name="markdown">The Markdown text to analyze.</param>
    /// <returns>Collection of extracted links.</returns>
    IEnumerable<string> ExtractLinks(string markdown);

    /// <summary>
    /// Extracts all headers from Markdown content.
    /// </summary>
    /// <param name="markdown">The Markdown text to analyze.</param>
    /// <returns>Collection of headers with their levels.</returns>
    IEnumerable<(int Level, string Text)> ExtractHeaders(string markdown);
}
