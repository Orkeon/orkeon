namespace Orkeon.Application.Interfaces.Infrastructure
{
    /// <summary>
    /// Abstraction for HTML parsing operations
    /// </summary>
    public interface IHtmlParser
    {
        /// <summary>
        /// Parses HTML content and extracts text
        /// </summary>
        string ExtractText(string html);

        /// <summary>
        /// Extracts specific elements from HTML using a selector
        /// </summary>
        IEnumerable<string> ExtractElements(string html, string selector);
    }
}
