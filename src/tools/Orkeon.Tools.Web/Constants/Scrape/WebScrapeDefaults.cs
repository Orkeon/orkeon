namespace Orkeon.Tools.Web.Constants.Scrape;

/// <summary>
/// Default values for web scraping operations.
/// </summary>
internal static class WebScrapeDefaults
{
    /// <summary>Domain suffix used to identify Wikipedia URLs.</summary>
    public const string WikipediaDomain = ".wikipedia.org";

    /// <summary>Wikipedia REST API v1 endpoint path for page summaries.</summary>
    public const string WikipediaApiEndpoint = "/api/rest_v1/page/summary/";

    /// <summary>Regex pattern for matching Wikipedia article paths.</summary>
    public const string WikipediaPathRegex = @"^/wiki/(.+)$";
}
