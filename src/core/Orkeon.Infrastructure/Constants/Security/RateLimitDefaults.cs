namespace Orkeon.Infrastructure.Constants.Security;

/// <summary>
/// Default values for rate limiting configuration.
/// Centralises magic numbers used across tool and LLM rate limiters.
/// </summary>
public static class RateLimitDefaults
{
    /// <summary>Maximum tool requests per minute across all tools.</summary>
    public const int GlobalToolRequestsPerMinute = 120;

    /// <summary>Default maximum requests per minute for tools without a specific limit.</summary>
    public const int DefaultToolRequestsPerMinute = 30;

    /// <summary>Per-tool rate limit for WebScrapeTool.</summary>
    public const int WebScrapeToolLimit = 10;

    /// <summary>Per-tool rate limit for HttpApiTool.</summary>
    public const int HttpApiToolLimit = 20;

    /// <summary>Per-tool rate limit for FileWriteTool.</summary>
    public const int FileWriteToolLimit = 15;

    /// <summary>Per-tool rate limit for SecureCodeInterpreterTool.</summary>
    public const int CodeInterpreterToolLimit = 5;

    /// <summary>Number of segments in the sliding window.</summary>
    public const int SlidingWindowSegments = 6;

    /// <summary>Duration of the sliding window for rate limiters (1 minute).</summary>
    public static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);

    /// <summary>Default rate limit reset strategy.</summary>
    public const string DefaultResetStrategy = "sliding_window";
}
