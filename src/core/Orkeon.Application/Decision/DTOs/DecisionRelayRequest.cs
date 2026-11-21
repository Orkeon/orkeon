namespace Orkeon.Application.Decision.DTOs;

/// <summary>What a decision task asks its <c>without:</c> relay agent (#430), for one LLM call.</summary>
public sealed record DecisionRelayRequest
{
    /// <summary>Gets the crew agent that answers: its provider (its own, or its <c>llm</c> profile) and its role.</summary>
    public required Orkeon.Domain.Agent.Agent Agent { get; init; }

    /// <summary>Gets the id of the decision task, for the call's attribution.</summary>
    public required string TaskId { get; init; }

    /// <summary>Gets the system prompt: the agent's role, goal and backstory.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>Gets the user prompt for a provider that receives the schema as <c>json_schema</c>.</summary>
    public required string Prompt { get; init; }

    /// <summary>Gets the user prompt with the schema written into it, for a provider that cannot take <c>json_schema</c>.</summary>
    public required string PromptWithSchema { get; init; }

    /// <summary>Gets the JSON schema of the answer, generated from the questions.</summary>
    public required string Schema { get; init; }
}

/// <summary>What the relay agent answered (#430): its text, unchecked, and the tokens the call used.</summary>
public sealed record DecisionRelayResponse
{
    /// <summary>Gets the text the agent answered.</summary>
    public required string Text { get; init; }

    /// <summary>Gets whether the schema travelled in the prompt rather than as <c>json_schema</c>.</summary>
    public bool SchemaInPrompt { get; init; }

    /// <summary>Gets the prompt tokens the call used.</summary>
    public int InputTokens { get; init; }

    /// <summary>Gets the completion tokens the call used.</summary>
    public int OutputTokens { get; init; }
}
