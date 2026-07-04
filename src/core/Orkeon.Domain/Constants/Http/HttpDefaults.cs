namespace Orkeon.Domain.Constants.Http;

/// <summary>
/// Default values for HTTP, LLM, and tool call timeouts.
/// Centralises scattered HTTP-related magic values used across the Orkeon platform.
/// </summary>
public static class HttpDefaults
{
    /// <summary>Default timeout for HTTP requests, LLM calls, tool calls, and sandbox execution (30 s).</summary>
    public static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default timeout for parallel tool execution (60 s).</summary>
    public static readonly TimeSpan DefaultToolParallelTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Default timeout for web-scraping HTTP requests (30 s).</summary>
    public const int ScrapeTimeoutSeconds = 30;

    /// <summary>Default User-Agent string sent by Orkeon HTTP clients.</summary>
    public const string DefaultUserAgent = "Orkeon/1.0 (+https://github.com/cyril-canovas/orkeon)";
}
