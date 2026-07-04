using HtmlAgilityPack;
using Orkeon.Application.Interfaces.Infrastructure;
using System.Net;

namespace Orkeon.Infrastructure.Serialization
{
    /// <summary>
    /// HtmlAgilityPack implementation of IHtmlParser
    /// </summary>
    public class HtmlAgilityPackParser : IHtmlParser
    {
        /// <inheritdoc />
        public string ExtractText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove script and style elements
            doc.DocumentNode.Descendants()
                .Where(n => n.Name == "script" || n.Name == "style")
                .ToList()
                .ForEach(n => n.Remove());

            var text = doc.DocumentNode.InnerText;
            return WebUtility.HtmlDecode(text?.Trim() ?? string.Empty);
        }

        /// <inheritdoc />
        public IEnumerable<string> ExtractElements(string html, string selector)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes(selector);
            if (nodes == null)
            {
                return [];
            }

            return nodes.Select(n => WebUtility.HtmlDecode(n.InnerText?.Trim() ?? string.Empty))
                       .Where(text => !string.IsNullOrWhiteSpace(text));
        }
    }
}
