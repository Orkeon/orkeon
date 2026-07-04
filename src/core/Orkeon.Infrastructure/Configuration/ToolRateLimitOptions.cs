using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for tool invocation rate limiting.
/// </summary>
public class ToolRateLimitOptions
{
    /// <summary>
    /// Maximum tool requests per minute across all tools.
    /// </summary>
    public int GlobalToolRequestsPerMinute { get; set; } = RateLimitDefaults.GlobalToolRequestsPerMinute;

    /// <summary>
    /// Default maximum requests per minute for tools without a specific limit.
    /// </summary>
    public int DefaultToolRequestsPerMinute { get; set; } = RateLimitDefaults.DefaultToolRequestsPerMinute;

    /// <summary>
    /// Per-tool rate limits, keyed by tool name.
    /// </summary>
    public Dictionary<string, int> ToolSpecificLimits { get; } = new()
    {
        ["WebScrapeTool"] = RateLimitDefaults.WebScrapeToolLimit,
        ["HttpApiTool"] = RateLimitDefaults.HttpApiToolLimit,
        ["FileWriteTool"] = RateLimitDefaults.FileWriteToolLimit,
        ["SecureCodeInterpreterTool"] = RateLimitDefaults.CodeInterpreterToolLimit,
    };
}
