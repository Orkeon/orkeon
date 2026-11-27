using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Interop.AgentCli.Engine;

/// <summary>
/// Claude Code's <c>claude -p</c> in stream-json (ADR-020): the command line and environment the probes
/// of 2026-11-21 and 2026-11-22 settled (plan, appendix A and the L2 probe results), and the lines of
/// appendices B and C.
/// </summary>
internal sealed class ClaudeCodeEngine : IAgentCliEngine
{
    /// <summary>The bridge's server name in the <c>--mcp-config</c> document.</summary>
    internal const string BridgeServerName = "orkeon";

    /// <summary>
    /// The variables removed from the CLI's environment. The first two would put the CLI on an API key
    /// instead of its login (probe 24, <c>apiKeySource: "ANTHROPIC_API_KEY"</c>); the others switch it to a
    /// cloud vendor's account, which bills that account and not the subscription.
    /// </summary>
    internal static readonly IReadOnlyList<string> RemovedVariables =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN",
        "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY",
    ];

    /// <summary>
    /// The prefixes of the variables removed from the CLI's environment, whatever follows them. Every
    /// <c>ANTHROPIC_*</c> variable of the host is meant for Orkeon's own <c>anthropic</c> provider or for an
    /// API client: <c>ANTHROPIC_BASE_URL</c> would send the CLI, and its subscription login, to a proxy the host
    /// set for that provider; <c>ANTHROPIC_CUSTOM_HEADERS</c>, <c>ANTHROPIC_MODEL</c> and the others would
    /// change what it sends. The CLI's own folder (<c>CLAUDE_CONFIG_DIR</c>, where its login lives), the
    /// <c>PATH</c>, the home folder, the proxies and the certificate bundles stay.
    /// </summary>
    internal static readonly IReadOnlyList<string> RemovedPrefixes = ["ANTHROPIC_"];

    private readonly AgentCliOptions _options;

    public ClaudeCodeEngine(AgentCliOptions options) => _options = options;

    public string Name => AgentCliEngines.Claude;

    public string ToolPrefix => "mcp__" + BridgeServerName + "__";

    public AgentCliCommandLine BuildCommandLine(AgentCliLaunch launch)
    {
        // Never --bare: it reads ANTHROPIC_API_KEY or an apiKeyHelper only, never the login (L2 probe 4).
        List<string> arguments =
        [
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--system-prompt-file", launch.SystemPromptPath,
            "--tools", "",
            "--mcp-config", launch.McpConfigJson,
            "--strict-mcp-config",
            "--allowedTools", ToolPrefix + "*",
            "--setting-sources", "",
            "--disable-slash-commands",
            "--no-session-persistence",
            "--permission-prompts", "none",
            "--autocompact", Math.Max(100_000, _options.AutocompactTokens).ToString(CultureInfo.InvariantCulture),
        ];
        if (!string.IsNullOrWhiteSpace(launch.Model))
        {
            arguments.Add("--model");
            arguments.Add(launch.Model);
        }

        // The CLI's levels are Orkeon's five (claude --help: low, medium, high, xhigh, max), for the whole process (#487).
        if (!string.IsNullOrWhiteSpace(launch.Effort))
        {
            arguments.Add("--effort");
            arguments.Add(launch.Effort);
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The CLI's memory, CLAUDE.md files and claude.ai connectors stay out of the conversation.
            ["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1",
            ["CLAUDE_CODE_DISABLE_CLAUDE_MDS"] = "1",
            ["ENABLE_CLAUDEAI_MCP_SERVERS"] = "false",
            // A tool call longer than two minutes would otherwise go to the background, its result lost in -p (probe 14).
            ["CLAUDE_CODE_MCP_AUTO_BACKGROUND_MS"] = "0",
            // Above every result the bridge hands over: the bridge caps results itself (L2 probe 2).
            ["MAX_MCP_OUTPUT_TOKENS"] = "1000000",
            ["ENABLE_TOOL_SEARCH"] = "false",
        };

        return new AgentCliCommandLine(_options.Command, arguments, environment, RemovedVariables, RemovedPrefixes);
    }

    public string UserLine(string text) =>
        new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = text },
        }.ToJsonString();

    public string InterruptLine(string requestId) =>
        new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = requestId,
            ["request"] = new JsonObject { ["subtype"] = "interrupt" },
        }.ToJsonString();

    public AgentCliEvent? ReadEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is null)
            return null;

        return Text(root, "type") switch
        {
            "system" => ReadSystem(root),
            "assistant" => ReadAssistant(root),
            "stream_event" => ReadStreamEvent(root),
            "result" => ReadResult(root),
            "rate_limit_event" => ReadRateLimit(root),
            _ => null,
        };
    }

    private static AgentCliEvent? ReadSystem(JsonObject root) => Text(root, "subtype") switch
    {
        "init" => new AgentCliEvent
        {
            Kind = AgentCliEventKind.Init,
            Model = Text(root, "model"),
            ApiKeySource = Text(root, "apiKeySource"),
            Version = Text(root, "claude_code_version"),
        },
        "compact_boundary" => new AgentCliEvent { Kind = AgentCliEventKind.CompactBoundary },
        _ => null,
    };

    private static AgentCliEvent? ReadAssistant(JsonObject root)
    {
        // A sub-agent's line (parent_tool_use_id set) never happens without the CLI's own tools: ignored.
        if (root["parent_tool_use_id"] is JsonValue)
            return null;
        if (root["message"] is not JsonObject message)
            return null;

        return new AgentCliEvent
        {
            Kind = AgentCliEventKind.AssistantBlock,
            MessageId = Text(message, "id"),
            Model = Text(message, "model"),
            Blocks = message["content"] is JsonArray content ? (JsonArray)content.DeepClone() : [],
            Usage = message["usage"] is JsonObject usage ? (JsonObject)usage.DeepClone() : null,
        };
    }

    private static AgentCliEvent? ReadStreamEvent(JsonObject root)
    {
        if (root["parent_tool_use_id"] is JsonValue || root["event"] is not JsonObject ev)
            return null;

        return Text(ev, "type") switch
        {
            "message_delta" => new AgentCliEvent
            {
                Kind = AgentCliEventKind.MessageDelta,
                StopReason = ev["delta"] is JsonObject delta ? Text(delta, "stop_reason") : null,
                Usage = ev["usage"] is JsonObject usage ? (JsonObject)usage.DeepClone() : null,
            },
            "message_stop" => new AgentCliEvent { Kind = AgentCliEventKind.MessageStop },
            _ => null,
        };
    }

    private static AgentCliEvent ReadResult(JsonObject root)
    {
        var text = Text(root, "result");
        if (string.IsNullOrEmpty(text) && root["errors"] is JsonArray errors)
            text = string.Join("; ", errors.Select(error => error?.ToString()).Where(error => !string.IsNullOrEmpty(error)));

        return new AgentCliEvent
        {
            Kind = AgentCliEventKind.Result,
            IsError = root["is_error"] is JsonValue isError && isError.TryGetValue<bool>(out var failed) && failed,
            ApiErrorStatus = Int(root, "api_error_status"),
            Subtype = string.Join('/', new[] { Text(root, "subtype"), Text(root, "terminal_reason") }.Where(part => !string.IsNullOrEmpty(part))),
            Text = text,
            NotionalCostUsd = root["total_cost_usd"] is JsonValue cost && cost.TryGetValue<decimal>(out var usd) ? usd : null,
            Usage = root["usage"] is JsonObject usage ? (JsonObject)usage.DeepClone() : null,
        };
    }

    private static AgentCliEvent? ReadRateLimit(JsonObject root)
    {
        if (root["rate_limit_info"] is not JsonObject info)
            return null;

        return new AgentCliEvent
        {
            Kind = AgentCliEventKind.RateLimit,
            RateLimitStatus = Text(info, "status"),
            RateLimitType = Text(info, "rateLimitType"),
            ResetsAt = info["resetsAt"] is JsonValue resets && resets.TryGetValue<long>(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null,
        };
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
}
