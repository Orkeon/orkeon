using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Decision;

/// <summary>
/// The <see cref="IDecisionRelay"/> of the host (#430): one chat call on the relay agent's provider - its own
/// provider, else its <c>llm</c> profile, else the default profile - with no tool and no history. The schema
/// travels as <c>json_schema</c> to a provider that declares it, in the prompt to any other. The call is
/// metered by the profile's client under <see cref="LlmUsageOperations.Relay"/> and the agent's role, so a
/// <c>cost.updated</c> names it.
/// </summary>
internal sealed class LlmDecisionRelay : IDecisionRelay
{
    /// <summary>The name the answer's schema is sent under.</summary>
    internal const string SchemaName = "decision_relay_answer";

    private readonly ILlmProfileRegistry? _profiles;

    /// <summary>Initializes a relay over the host's LLM profiles.</summary>
    /// <param name="profiles">The registry that builds and meters the agent's client; null in a host that has none, where every call fails.</param>
    public LlmDecisionRelay(ILlmProfileRegistry? profiles) => _profiles = profiles;

    /// <inheritdoc />
    public async Task<DecisionRelayResponse> AskAsync(DecisionRelayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var agent = request.Agent;
        var profile = ProfileOf(agent);

        // json_schema only where the provider declares it: elsewhere the adapter would drop it, and the schema
        // the agent must follow goes in its prompt instead.
        var native = profile.Provider.Capabilities.ResponseFormat >= ResponseFormatSupport.JsonSchema;
        var options = Options(agent.LlmConfig);
        if (native)
        {
            options.AdditionalProperties ??= [];
            // Not strict: OpenAI's strict mode refuses the numeric bounds the schema states.
            options.AdditionalProperties[LlmChatOptionsKeys.StructuredOutput] =
                LlmResponseFormat.JsonSchema(SchemaName, request.Schema, strict: false);
        }

        List<ChatMessage> messages =
        [
            new(ChatRole.System, request.SystemPrompt),
            new(ChatRole.User, native ? request.Prompt : request.PromptWithSchema),
        ];

        ChatResponse response;
        using (LlmUsageScope.Begin(LlmUsageOperations.Relay, agentId: agent.Role.Value, taskId: request.TaskId))
            response = await profile.ChatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        return new DecisionRelayResponse
        {
            Text = response.Text,
            SchemaInPrompt = !native,
            InputTokens = (int)(response.Usage?.InputTokenCount ?? 0),
            OutputTokens = (int)(response.Usage?.OutputTokenCount ?? 0),
        };
    }

    private LlmProfile ProfileOf(Orkeon.Domain.Agent.Agent agent)
    {
        if (_profiles is null)
            throw new InvalidOperationException("this host registers no LLM profile registry to build the agent's client");
        if (agent.Llm is { } own)
            return _profiles.ForProvider(own);

        // A relay agent on llm.profile: auto answers a decision, not a task: the default profile (#434).
        var named = LlmProfiles.IsAuto(agent.LlmConfig?.Profile) ? null : agent.LlmConfig?.Profile;
        if (!LlmProfiles.IsDefault(named))
            LlmProfiles.EnsureKnown(_profiles, named, $"The relay agent '{agent.Role.Value}'");
        return _profiles.Resolve(LlmProfiles.IsDefault(named) ? null : named);
    }

    // What the agent's config sets for its calls, as an agent's own call forwards it; the rest stays the provider's.
    private static ChatOptions Options(LlmConfig? llm)
    {
        var options = new ChatOptions();
        if (llm is null)
            return options;
        if (!string.IsNullOrWhiteSpace(llm.Model))
            options.ModelId = llm.Model;
        if (llm.Temperature is { } temperature)
            options.Temperature = (float)temperature;
        if (llm.MaxTokens is > 0)
            options.MaxOutputTokens = llm.MaxTokens;
        if (llm.TopP is { } topP)
            options.TopP = (float)topP;
        if (llm.Seed is { } seed)
            options.Seed = seed;
        // The penalties have no unset value: 0 is every vendor's default, so one is sent when it is not 0.
        if (llm.FrequencyPenalty != 0.0)
            options.FrequencyPenalty = (float)llm.FrequencyPenalty;
        if (llm.PresencePenalty != 0.0)
            options.PresencePenalty = (float)llm.PresencePenalty;
        if (llm.StopSequences is { Count: > 0 } stopSequences)
            options.StopSequences = [.. stopSequences];
        return options;
    }
}
