using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Execution;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Interop.AgentCli.Sessions;

namespace Orkeon.Interop.AgentCli;

/// <summary>
/// <c>Llm:Provider = agent-cli</c> (ADR-020): a model turn through an agent CLI's own login. Orkeon keeps the
/// history and runs every tool; the CLI is a model reached through its subscription. Each call returns one
/// assistant message, in the Anthropic <c>content[]</c> shape, so the tool loops read its tool calls as they
/// read Anthropic's. The cost is 0: the subscription pays, and the CLI's notional figure is kept apart
/// (<see cref="NotionalCostKey"/>).
/// </summary>
internal sealed partial class AgentCliLlmProvider : ILlmProvider
{
    /// <summary>The metadata key of the CLI's own cost figure, what the turn would cost at API prices.</summary>
    internal const string NotionalCostKey = "agent_cli_notional_cost_usd";

    private const string OptionRemedy = "The agent CLI takes the model and a thinking effort alone; set the others in the CLI itself, or use another provider";

    private const string WorkspaceRemedy = "Only Anthropic scopes a key to a workspace, and the agent CLI runs on its login; remove Llm:WorkspaceId (or the profile's) for this provider";

    private const string ThinkingRemedy = "The agent CLI takes a thinking effort alone (--effort low, medium, high, xhigh or max): it has no flag to switch thinking off, to switch it on without a level, or to bound it by a token budget; give an effort, or use another provider";

    private const string ToolChoiceRemedy = "The agent CLI has no flag to force a tool call; the tools are offered and the model chooses, or use another provider";

    private readonly AgentCliProcessHost _host;
    private readonly LlmConfig _config;
    private readonly ILogger _logger;
    private readonly Lock _warnedGate = new();
    private readonly HashSet<string> _warnedOptions = new(StringComparer.Ordinal);
    private string? _warnedRun;

    public AgentCliLlmProvider(AgentCliProcessHost host, LlmConfig config, ILogger logger)
    {
        _host = host;
        _config = config;
        _logger = logger;
    }

    public string Name => AgentCliDiagnostics.ProviderKey;

    public LlmConfig? BaseConfig => _config;

    /// <summary>
    /// The CLI takes a thinking effort, Orkeon's five levels (<c>--effort</c>, #487), and nothing else of the
    /// thinking: no switch, no budget.
    /// </summary>
    public LlmProviderCapabilities Capabilities { get; } = LlmProviderCapabilities.Unknown with
    {
        Thinking = ThinkingSupport.EffortOnly,
        EffortLevels = LlmThinkingConfig.Levels,
    };

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        ChatAsync([new LlmMessage { Role = "user", Content = prompt }], config, cancellationToken);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The throwaway store is disposed in the finally block; the run's store is the scope's.")]
    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var effective = config?.InheritFrom(_config) ?? _config;
        WarnAboutOptionsNotSent(effective);

        var (systemPrompt, history) = AgentCliHistory.Split(messages, effective.SystemMessage);
        if (history.Count == 0)
            return Failure("The agent-cli provider was called with no message beyond the system prompt.", "InvalidRequest", effective.Model);

        var model = string.IsNullOrWhiteSpace(effective.Model) ? null : effective.Model;
        var effort = EffortSent(effective.Thinking);
        var keys = history.Select(AgentCliHistory.KeyOf).ToList();
        var fingerprint = AgentCliHistory.Fingerprint(model, systemPrompt, keys, effort);
        // ToolMode None sends no tool: the CLI cannot call one it was not given. Required has no flag (warned).
        IReadOnlyList<ToolSchema> tools = effective.ToolMode == ToolCallMode.None ? [] : effective.Tools ?? [];
        var bounds = new AgentCliCallBounds(effective.ResolveTimeoutSeconds(), effective.StreamIdleSeconds);

        // A run keeps its sessions until it ends; a call outside any run gets a session of its own.
        var scope = AgentRunScope.Current;
        var store = scope?.GetOrAdd((this, AgentCliDiagnostics.ProviderKey), () => new AgentCliSessionStore(_host));
        var throwaway = store is null ? new AgentCliSessionStore(_host) : null;
        try
        {
            var session = await (store ?? throwaway!).AcquireAsync(fingerprint, keys, systemPrompt, model, effort).ConfigureAwait(false);
            AgentCliTurn turn;
            try
            {
                turn = await session.RunAsync(history, keys, tools, bounds, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                session.Release();
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            session.Release();
            if (turn.Error is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                return Failure(turn.Error, turn.ErrorType ?? (turn.ApiErrorStatus is 429 ? "RateLimitError" : "APIError"), model);
            }

            return Response(turn, model);
        }
        finally
        {
            if (throwaway is not null)
                await throwaway.DisposeAsync().ConfigureAwait(false);
        }
    }

    private LlmResponse Response(AgentCliTurn turn, string? requestedModel)
    {
        var usage = turn.Usage;
        var input = Count(usage, "input_tokens");
        var output = Count(usage, "output_tokens");
        var cacheWrite = Count(usage, "cache_creation_input_tokens");
        var cacheRead = Count(usage, "cache_read_input_tokens");
        var cacheWrite1h = usage?["cache_creation"] is JsonObject creation ? Count(creation, "ephemeral_1h_input_tokens") : null;
        var prompt = (input ?? 0) + (cacheWrite ?? 0) + (cacheRead ?? 0);
        var completion = output ?? 0;

        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["provider"] = Name,
            [LlmUsageMetadataKeys.Cost] = 0m,
            [LlmResponseMetadataKeys.FinishReason] = turn.StopReason ?? "end_turn",
            ["input_tokens"] = input ?? 0,
            ["output_tokens"] = completion,
        };
        if (turn.NotionalCostUsd is { } notional)
            metadata[NotionalCostKey] = notional;

        var content = new JsonArray();
        if (turn.Text.Length > 0)
            content.Add(new JsonObject { ["type"] = "text", ["text"] = turn.Text });
        foreach (var call in turn.ToolCalls)
            content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = call.Input.DeepClone() });

        var model = turn.Model ?? requestedModel ?? "";
        var body = new JsonObject
        {
            ["id"] = turn.MessageId ?? "",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = model,
            ["content"] = content,
            ["stop_reason"] = turn.StopReason ?? "end_turn",
            ["usage"] = usage?.DeepClone(),
        };

        return new LlmResponse
        {
            Content = turn.Text,
            TokensUsed = prompt + completion,
            PromptTokens = prompt,
            CompletionTokens = completion,
            CacheHitTokens = cacheRead,
            CacheMissTokens = cacheWrite is null && cacheRead is null ? null : (input ?? 0) + (cacheWrite ?? 0),
            CacheWriteTokens = cacheWrite,
            CacheWrite1hTokens = cacheWrite1h,
            Model = model,
            Metadata = metadata,
            RawResponseBody = body.ToJsonString(),
        };
    }

    private LlmResponse Failure(string error, string errorType, string? model) => new()
    {
        Content = "",
        Model = model ?? "",
        Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["provider"] = Name,
            [LlmUsageMetadataKeys.Cost] = 0m,
            [LlmResponseMetadataKeys.Error] = error,
            [LlmResponseMetadataKeys.ErrorType] = errorType,
        },
    };

    private static int? Count(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<long>(out var count)
            ? (int)Math.Min(count, int.MaxValue)
            : null;

    /// <summary>
    /// Warns, once per run and per option, about each option the call declares that the CLI cannot take: the
    /// rule of every provider (an option a provider cannot honour is a structured warning, never a silent drop).
    /// </summary>
    private void WarnAboutOptionsNotSent(LlmConfig config)
    {
        if (!_logger.IsEnabled(LogLevel.Warning))
            return;

        var declared = OptionDeclarations.Where(declaration => declaration.IsDeclared(config)).Select(declaration => declaration.Option).ToList();
        if (declared.Count == 0)
            return;

        var run = LlmUsageScope.Current.CrewId;
        List<string> unwarned;
        lock (_warnedGate)
        {
            if (!string.Equals(_warnedRun, run, StringComparison.Ordinal))
            {
                _warnedRun = run;
                _warnedOptions.Clear();
            }

            unwarned = declared.Where(_warnedOptions.Add).ToList();
        }

        foreach (var option in unwarned)
        {
            LogOptionNotSent(option, Name, option switch
            {
                WorkspaceOption => WorkspaceRemedy,
                ThinkingOption => ThinkingRemedy,
                ToolChoiceOption => ToolChoiceRemedy,
                _ => OptionRemedy,
            });
        }
    }

    /// <summary>
    /// The effort the CLI is started at: the call's level when thinking is not written off and the level is one of
    /// <see cref="LlmThinkingConfig.Levels"/>, which the CLI takes all of. A vendor's own value, or a level under
    /// <c>enabled: false</c>, is not sent and is warned about (<see cref="ThinkingDropped"/>).
    /// </summary>
    internal static string? EffortSent(LlmThinkingConfig? thinking) =>
        thinking is { Enabled: not false, Effort: { } effort } && LlmThinkingConfig.Levels.Contains(effort, StringComparer.Ordinal)
            ? effort
            : null;

    /// <summary>
    /// Whether the call declares something of the thinking the CLI cannot take: a budget, a switch (off, or on
    /// without a level), or an effort that is not sent.
    /// </summary>
    private static bool ThinkingDropped(LlmConfig config) =>
        config.Thinking is { } thinking
        && (thinking.BudgetTokens is not null
            || thinking.Enabled == false
            || (thinking.Effort is null ? thinking.Enabled == true : EffortSent(thinking) is null));

    /// <summary>The thinking options, under their wire name.</summary>
    private const string ThinkingOption = "thinking";

    /// <summary>The name the HTTP providers warn about <c>Llm:WorkspaceId</c> under (#468).</summary>
    private const string WorkspaceOption = "WorkspaceId";

    /// <summary><c>ToolMode = Required</c>, under its wire name.</summary>
    private const string ToolChoiceOption = "tool_choice";

    /// <summary>
    /// The options a configuration can declare that the CLI's command line has no flag for. The call's bounds
    /// (<c>Llm:TimeoutSeconds</c>, <c>Llm:StreamIdleSeconds</c>), a thinking effort and <c>ToolMode = None</c> are honoured.
    /// </summary>
    private static readonly (string Option, Func<LlmConfig, bool> IsDeclared)[] OptionDeclarations =
    [
        ("temperature", config => config.Temperature is not null),
        ("max_tokens", config => config.MaxTokens is not null),
        ("top_p", config => config.TopP is not null),
        ("frequency_penalty", config => config.FrequencyPenalty is not 0.0),
        ("presence_penalty", config => config.PresencePenalty is not 0.0),
        ("seed", config => config.Seed is not null),
        ("stop", config => config.StopSequences.Count > 0),
        ("response_format", config => config.ResponseFormat is { } format
            && !string.Equals(format.Type, "text", StringComparison.OrdinalIgnoreCase)),
        (ThinkingOption, ThinkingDropped),
        ("grammar", config => !string.IsNullOrWhiteSpace(config.GrammarGbnf)),
        ("cache", config => config.Cache is { RequestsAnyBreakpoint: true }),
        (WorkspaceOption, config => !string.IsNullOrWhiteSpace(config.WorkspaceId)),
        (ToolChoiceOption, config => config.ToolMode == ToolCallMode.Required),
    ];

    [LoggerMessage(EventId = 110, Level = LogLevel.Warning,
        Message = "Option '{Option}' was declared but {ProviderName} does not support it — it was not sent. {Remedy}.")]
    private partial void LogOptionNotSent(string option, string providerName, string remedy);
}
