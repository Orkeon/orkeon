using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Interop.AgentFramework;

/// <summary>
/// A Microsoft Agent Framework <see cref="AIAgent"/> used as the model of an Orkeon agent
/// (<see cref="AgentBuilderExtensions.WithAgentFrameworkAgent"/>, or <c>AgentBuilder.WithLlm(...)</c>):
/// the agent's own provider (<c>Agent.Llm</c>), which the run turns into a metered client (GAP-34).
/// <para>
/// Each call is one MAF run on a session this provider keeps for its lifetime, so a MAF agent with
/// memory or context providers sees one continuous conversation across the Orkeon agent's turns and
/// tasks. The session hears each message once: a call that extends the conversation the session holds
/// — the previous call's messages, then the answer this provider gave them — sends only what is new,
/// any other call — a new task — sends all its messages. Calls run one at a time, so two tasks of the
/// same agent never write to the session together.
/// </para>
/// <para>
/// The MAF agent calls the tools it carries, never Orkeon's: the provider declares
/// <see cref="LlmProviderCapabilities.RunsOwnTools"/>, and an Orkeon tool is refused on an agent it
/// answers for — give the tool to the MAF agent, or give the MAF agent to an Orkeon agent as a tool
/// (<see cref="AIAgentTool"/>). It sends the MAF agent messages only: every option of the
/// <see cref="LlmConfig"/> it is handed (model, temperature, response format, thinking…) is dropped,
/// and each one a call declares is a structured warning, once per run and option. It does not stream:
/// a streamed turn receives the MAF answer as one fragment.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The SemaphoreSlim is only ever waited on asynchronously and released: it allocates its wait handle only when AvailableWaitHandle is read, which this type never does, so Dispose would release nothing. Making the bridge IDisposable would ripple CA2000 into WithAgentFrameworkAgent and every host that registers it, to release nothing.")]
public sealed partial class AIAgentLlmProvider : ILlmProvider
{
    private static readonly LlmProviderCapabilities s_capabilities = LlmProviderCapabilities.Unknown with { RunsOwnTools = true };

    private const string OptionRemedy =
        "A Microsoft Agent Framework agent answers on its own model and settings: set the option on the MAF agent itself (its chat client, or its ChatClientAgentOptions)";

    private readonly AIAgent _agent;
    private readonly ILogger<AIAgentLlmProvider> _logger;
    private readonly Lazy<Task<AgentSession>> _session;
    private readonly SemaphoreSlim _oneCallAtATime = new(1, 1);
    private readonly Lock _warnedGate = new();
    private readonly HashSet<string> _warnedOptions = new(StringComparer.Ordinal);
    private string? _warnedRun;

    /// <summary>
    /// The conversation the session holds after the last call: that call's messages, then the answer
    /// this provider gave them (none for an empty answer). Null before the first call and after a call
    /// that failed — the session's state is then unknown, and the next call sends everything. Read and
    /// written under <see cref="_oneCallAtATime"/>.
    /// </summary>
    private List<LlmMessage>? _held;

    /// <summary>Wraps <paramref name="agent"/>.</summary>
    /// <param name="agent">The MAF agent that answers.</param>
    /// <param name="logger">
    /// Where the bridge says which options it did not send; null takes the logger factory the MAF agent
    /// exposes (<c>GetService&lt;ILoggerFactory&gt;()</c>, MAF's own convention), else logs nothing.
    /// </param>
    public AIAgentLlmProvider(AIAgent agent, ILogger<AIAgentLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        _logger = logger
            ?? agent.GetService<ILoggerFactory>()?.CreateLogger<AIAgentLlmProvider>()
            ?? NullLogger<AIAgentLlmProvider>.Instance;
        // One session for the provider's lifetime, created on first use. Session creation
        // is not tied to a caller's token: the first caller's cancellation must not poison
        // the session for the callers after it.
        _session = new(() => _agent.CreateSessionAsync(CancellationToken.None).AsTask(), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string Name => "agent-framework:" + (_agent.Name ?? _agent.Id);

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="LlmProviderCapabilities.RunsOwnTools"/>, and nothing else: no response format, no
    /// thinking control — the MAF agent's own settings decide them.
    /// </remarks>
    public LlmProviderCapabilities Capabilities => s_capabilities;

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        RunAsync([new LlmMessage { Role = "user", Content = prompt }], config, cancellationToken);

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return RunAsync(messages, config, cancellationToken);
    }

    private async Task<LlmResponse> RunAsync(IReadOnlyList<LlmMessage> conversation, LlmConfig? config, CancellationToken cancellationToken)
    {
        WarnAboutOptionsNotSent(config);

        await _oneCallAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _session.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            var toSend = WhatTheSessionLacks(conversation);

            // Unknown until the run ends: a call that fails leaves the next one to send everything.
            _held = null;
            var response = await _agent.RunAsync(toSend.Select(ToChatMessage).ToList(), session, options: null, cancellationToken).ConfigureAwait(false);
            var answer = ToLlmResponse(response, _agent);

            _held = [.. conversation];
            if (!string.IsNullOrEmpty(answer.Content))
                _held.Add(new LlmMessage { Role = "assistant", Content = answer.Content });
            return answer;
        }
        finally
        {
            _oneCallAtATime.Release();
        }
    }

    /// <summary>
    /// What of <paramref name="conversation"/> the session does not hold yet: the messages after the
    /// conversation it holds when the call extends it — less an empty assistant turn the empty answer
    /// left there —, else all of them.
    /// </summary>
    private IReadOnlyList<LlmMessage> WhatTheSessionLacks(IReadOnlyList<LlmMessage> conversation)
    {
        if (_held is not { } held || conversation.Count <= held.Count)
            return conversation;

        for (var index = 0; index < held.Count; index++)
        {
            if (!SameMessage(held[index], conversation[index]))
                return conversation;
        }

        var rest = conversation.Skip(held.Count)
            .SkipWhile(message => IsAssistant(message.Role) && string.IsNullOrEmpty(message.Content))
            .ToList();
        return rest.Count > 0 ? rest : conversation;
    }

    private static bool SameMessage(LlmMessage held, LlmMessage sent) =>
        MapRole(held.Role) == MapRole(sent.Role) && string.Equals(held.Content, sent.Content, StringComparison.Ordinal);

    private static bool IsAssistant(string? role) => MapRole(role) == ChatRole.Assistant;

    private static ChatMessage ToChatMessage(LlmMessage message) => new(MapRole(message.Role), message.Content);

    internal static LlmResponse ToLlmResponse(AgentResponse response, AIAgent agent)
    {
        var usage = response.Usage;
        return new LlmResponse
        {
            Content = response.Text,
            Model = agent.Name ?? agent.Id,
            TokensUsed = (int)(usage?.TotalTokenCount ?? 0),
            PromptTokens = usage?.InputTokenCount is { } input ? (int)input : null,
            CompletionTokens = usage?.OutputTokenCount is { } output ? (int)output : null,
        };
    }

#pragma warning disable CA1308 // the role token is normalised for the switch, not compared
    private static ChatRole MapRole(string? role) => role?.ToLowerInvariant() switch
#pragma warning restore CA1308
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User,
    };

    /// <summary>
    /// Warns, once per run — the crew the call is attributed to (<see cref="LlmUsageScope"/>) — and per
    /// option, about each option <paramref name="config"/> declares: none reaches the MAF agent
    /// (GAP-34, decision 7). The rule of the HTTP providers: an option a provider cannot honour is a
    /// structured warning, never a silent drop.
    /// </summary>
    private void WarnAboutOptionsNotSent(LlmConfig? config)
    {
        if (config is null || !_logger.IsEnabled(LogLevel.Warning))
            return;

        var declared = DeclaredOptions(config);
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
            LogOptionNotSent(option, Name, OptionRemedy);
    }

    /// <summary>
    /// The options <paramref name="config"/> sets beyond a configuration that names nothing
    /// (<see cref="LlmConfig.OnProfile"/>) — a temperature or a <c>top_p</c> whatever its value: 0.7
    /// and 1.0 used to be taken for "not set", and said nothing (GAP-36).
    /// </summary>
    private static List<string> DeclaredOptions(LlmConfig config)
    {
        var options = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.Model))
            options.Add("model");
        if (config.Temperature is not null)
            options.Add("temperature");
        if (config.MaxTokens is not null)
            options.Add("max_tokens");
        if (config.TopP is not null)
            options.Add("top_p");
        if (config.FrequencyPenalty != 0.0)
            options.Add("frequency_penalty");
        if (config.PresencePenalty != 0.0)
            options.Add("presence_penalty");
        if (config.Seed is not null)
            options.Add("seed");
        if (config.StopSequences.Count > 0)
            options.Add("stop");
        if (!string.IsNullOrWhiteSpace(config.SystemMessage))
            options.Add("system_message");
        if (config.ResponseFormat is { } format && !string.Equals(format.Type, "text", StringComparison.OrdinalIgnoreCase))
            options.Add("response_format");
        if (config.Thinking is not null)
            options.Add("thinking");
        if (!string.IsNullOrWhiteSpace(config.GrammarGbnf))
            options.Add("grammar");
        if (config.Tools is { Count: > 0 })
            options.Add("tools");
        if (config.Cache is not null)
            options.Add("cache");
        if (config.CustomParameters.Count > 0)
            options.Add("custom_parameters");
        return options;
    }

    /// <summary>
    /// LLM-02's warning, in the HTTP providers' words: an option the caller declared that this provider
    /// cannot honour is reported, with what to do about it, instead of being dropped.
    /// </summary>
    [LoggerMessage(EventId = 110, Level = LogLevel.Warning,
        Message = "Option '{Option}' was declared but {ProviderName} does not support it — it was not sent. {Remedy}.")]
    private partial void LogOptionNotSent(string option, string providerName, string remedy);
}
