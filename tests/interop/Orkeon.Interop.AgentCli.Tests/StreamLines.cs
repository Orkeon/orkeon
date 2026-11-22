using System.Text.Json.Nodes;

namespace Orkeon.Interop.AgentCli.Tests;

/// <summary>The stream-json lines of the plan's appendix C, as <c>claude -p</c> 2.1 writes them.</summary>
internal static class StreamLines
{
    public static JsonObject Init(string apiKeySource = "none", string model = "claude-haiku-4-5") => new()
    {
        ["type"] = "system",
        ["subtype"] = "init",
        ["apiKeySource"] = apiKeySource,
        ["model"] = model,
        ["claude_code_version"] = "2.1.0",
    };

    public static JsonObject Text(string messageId, string text) => Assistant(messageId, new JsonObject { ["type"] = "text", ["text"] = text });

    public static JsonObject ToolUse(string messageId, string id, string tool, JsonObject? input = null) =>
        Assistant(messageId, new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = "mcp__orkeon__" + tool, ["input"] = input ?? [] });

    private static JsonObject Assistant(string messageId, JsonObject block) => new()
    {
        ["type"] = "assistant",
        ["parent_tool_use_id"] = null,
        ["message"] = new JsonObject
        {
            ["id"] = messageId,
            ["model"] = "claude-haiku-4-5",
            ["role"] = "assistant",
            ["content"] = new JsonArray(block),
            ["usage"] = new JsonObject { ["input_tokens"] = 3, ["output_tokens"] = 1 },
        },
    };

    public static JsonObject Delta(string stopReason, int input = 10, int output = 20, int cacheRead = 100, int cacheWrite = 5) => new()
    {
        ["type"] = "stream_event",
        ["parent_tool_use_id"] = null,
        ["event"] = new JsonObject
        {
            ["type"] = "message_delta",
            ["delta"] = new JsonObject { ["stop_reason"] = stopReason },
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = input,
                ["output_tokens"] = output,
                ["cache_read_input_tokens"] = cacheRead,
                ["cache_creation_input_tokens"] = cacheWrite,
            },
        },
    };

    public static JsonObject Stop() => new()
    {
        ["type"] = "stream_event",
        ["parent_tool_use_id"] = null,
        ["event"] = new JsonObject { ["type"] = "message_stop" },
    };

    public static JsonObject Result(string text, bool isError = false, int? status = null) => new()
    {
        ["type"] = "result",
        ["subtype"] = isError ? "error_during_execution" : "success",
        ["is_error"] = isError,
        ["api_error_status"] = status,
        ["result"] = text,
        ["total_cost_usd"] = 0.0123,
        ["terminal_reason"] = "completed",
    };

    public static JsonObject RateLimit(string status, long resetsAt) => new()
    {
        ["type"] = "rate_limit_event",
        ["rate_limit_info"] = new JsonObject { ["status"] = status, ["resetsAt"] = resetsAt, ["rateLimitType"] = "five_hour" },
    };

    public static JsonObject CompactBoundary() => new() { ["type"] = "system", ["subtype"] = "compact_boundary" };
}
