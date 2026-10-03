using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Crew;

/// <summary>
/// Resolves the LLM a crew gives its manager (GAP-19): the provider C# sets with
/// <c>CrewBuilder.WithManagerLlm</c> — CrewAI's <c>manager_llm</c> —, metered for the host's
/// <see cref="ILlmUsageSink"/> and limited by its <see cref="ILlmRateLimiter"/> like a provider the host
/// registers; else the provider the manager agent carries itself (<c>Agent.Llm</c> — a Microsoft Agent
/// Framework agent answering for it, GAP-34), entered the same way; else the manager agent's
/// <c>llm:</c> block, its profile (<see cref="ILlmProfileRegistry"/>) and model, a model left unset
/// being the profile's own; else the host's default profile. The hierarchical and autonomous
/// strategies resolve it once per run and hand it to every manager call.
/// </summary>
/// <remarks>
/// Whichever it is, every call of the manager waits its turn first (GAP-38): in the window of its
/// manager agent — its <c>maxRpm</c> and the host's per-agent cap — when it has one, and in the window
/// of the crew in progress — its <c>maxRpm</c>. An autonomous crew's manager has no agent: its calls
/// count for the crew alone.
/// </remarks>
public sealed partial class ManagerLlmResolver
{
    private readonly IChatClient _defaultChatClient;
    private readonly ILlmProfileRegistry? _profiles;
    private readonly ILlmUsageSink? _usageSink;
    private readonly ILlmRateLimiter? _rateLimiter;
    private readonly TimeProvider _time;
    private readonly int? _hostAgentLimit;
    private readonly ILogger _logger;

    /// <summary>Builds the resolver over the host's language models.</summary>
    /// <param name="defaultChatClient">The host's default profile, as a chat client.</param>
    /// <param name="profiles">
    /// The host's named profiles; null — a strategy built by hand — offers the default alone, and a
    /// manager agent naming another profile fails with the list of known ones.
    /// </param>
    /// <param name="usageSink">Where the calls of a provider the crew sets are metered; null meters nothing.</param>
    /// <param name="rateLimiter">The host's limiter of a provider the crew sets; null limits nothing.</param>
    /// <param name="time">The clock the manager's request windows count on; <see cref="TimeProvider.System"/> when null.</param>
    /// <param name="hostAgentLimit">The host's per-agent cap (<c>RateLimiting:AgentRequestsPerMinute</c>); null sets none.</param>
    /// <param name="logger">Where the line of a manager call that waited goes.</param>
#pragma warning disable S107 // DI constructor: the host's models, its meter and limiter, the windows' clock and cap, the log
    public ManagerLlmResolver(
        IChatClient defaultChatClient,
        ILlmProfileRegistry? profiles = null,
        ILlmUsageSink? usageSink = null,
        ILlmRateLimiter? rateLimiter = null,
        TimeProvider? time = null,
        int? hostAgentLimit = null,
        ILogger<ManagerLlmResolver>? logger = null)
#pragma warning restore S107
    {
        ArgumentNullException.ThrowIfNull(defaultChatClient);
        _defaultChatClient = defaultChatClient;
        _profiles = profiles;
        _usageSink = usageSink;
        _rateLimiter = rateLimiter;
        _time = time ?? TimeProvider.System;
        _hostAgentLimit = hostAgentLimit;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>The LLM <paramref name="crew"/>'s manager runs on.</summary>
    /// <param name="crew">The crew whose manager it is.</param>
    /// <param name="managerAgent">The crew's manager agent; null when it has none.</param>
    /// <returns>The chat client, the model and the name of the manager's LLM.</returns>
    /// <exception cref="InvalidOperationException">
    /// The manager agent names a profile the host does not offer. The crew load checks it first;
    /// this is the guard for a crew built by hand.
    /// </exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The adapter over a provider the crew sets holds no resource (its Dispose is empty, the provider owns its own lifecycle); it lives as long as the ManagerLlm handed to the run, as it did before the turn-taking client wrapped it.")]
    public ManagerLlm Resolve(DomainCrew crew, DomainAgent? managerAgent)
    {
        ArgumentNullException.ThrowIfNull(crew);
        var name = ManagerLlm.Describe(crew, managerAgent);

        // The provider the crew gave its manager wins, as CrewAI's manager_llm does over its
        // manager agent: metered and limited here unless it already is, its own configuration the base.
        if (crew.ManagerLlm is { } provider)
        {
            return new ManagerLlm
            {
                ChatClient = TakingTurns(new LlmProviderToChatClientAdapter(
                    LlmProviderEntrance.Enter(provider, _usageSink, _rateLimiter)), managerAgent),
                Name = name,
            };
        }

        var config = managerAgent?.LlmConfig;
        var model = string.IsNullOrWhiteSpace(config?.Model) ? null : config.Model;

        // Then the provider the manager agent carries itself — the one its turns run on: entered at
        // this same entrance, or a manager backed by a MAF agent would manage on the default in silence.
        if (managerAgent?.Llm is { } own)
        {
            return new ManagerLlm
            {
                ChatClient = TakingTurns(new LlmProviderToChatClientAdapter(
                    LlmProviderEntrance.Enter(own, _usageSink, _rateLimiter)), managerAgent),
                Model = model,
                Name = name,
            };
        }

        if (LlmProfiles.IsDefault(config?.Profile))
            return new ManagerLlm { ChatClient = TakingTurns(_defaultChatClient, managerAgent), Model = model, Name = name };

        LlmProfiles.EnsureKnown(_profiles, config!.Profile, $"Manager agent '{managerAgent!.Role.Value}'");
        return new ManagerLlm
        {
            ChatClient = TakingTurns(_profiles!.Resolve(config.Profile).ChatClient, managerAgent),
            Model = model,
            Name = name,
        };
    }

    /// <summary><paramref name="client"/>, each call of which first waits its turn for the manager.</summary>
    private ManagerTurnChatClient TakingTurns(IChatClient client, DomainAgent? managerAgent) =>
        new(client, managerAgent, _time, _hostAgentLimit, _logger);

    /// <summary>
    /// The manager's chat client: before each call — buffered or streamed — it waits its turn in the
    /// manager agent's window, if there is one, and in the crew's (<see cref="RequestRates"/>), and
    /// says so on one Information line when it waited. Nothing is held during the call.
    /// </summary>
    private sealed class ManagerTurnChatClient(
        IChatClient inner, DomainAgent? managerAgent, TimeProvider time, int? hostAgentLimit, ILogger logger)
        : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            await WaitTurnAsync(cancellationToken).ConfigureAwait(false);
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await WaitTurnAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
                yield return update;
        }

        private async Task WaitTurnAsync(CancellationToken cancellationToken)
        {
            var turn = await RequestRates.WaitTurnAsync(managerAgent, hostAgentLimit, time, cancellationToken).ConfigureAwait(false);
            if (turn.HasWaited)
                LogManagerWaited(logger, managerAgent?.Role.Value ?? "manager", turn.Waited.TotalSeconds, turn.Limit ?? string.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Manager {ManagerRole} waited {Seconds:0.#} s for its model request: {Limit}")]
    private static partial void LogManagerWaited(ILogger logger, string managerRole, double seconds, string limit);
}
