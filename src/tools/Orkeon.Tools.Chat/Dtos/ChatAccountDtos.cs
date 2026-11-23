using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Chat.Dtos;

/// <summary>Request of <c>chat_accounts</c>.</summary>
internal sealed record ChatAccountsRequest
{
}

/// <summary>A configured account.</summary>
internal sealed record ChatAccountDto
{
    /// <summary>Name to pass as account.</summary>
    [JsonPropertyName("name")]
    [ReturnSchema(Description = "Name to pass as `account`")]
    public string Name { get; init; } = "";

    /// <summary>The chat service it opens.</summary>
    [JsonPropertyName("provider")]
    [ReturnSchema(Description = "The chat service it opens")]
    public string Provider { get; init; } = "";

    /// <summary>What an agent may do with it.</summary>
    [JsonPropertyName("rights")]
    [ReturnSchema(Description = "What an agent may do with it")]
    public string? Rights { get; init; }

    /// <summary>Whether it is used when account is omitted.</summary>
    [JsonPropertyName("default")]
    [ReturnSchema(Description = "Whether it is used when `account` is omitted")]
    public bool Default { get; init; }

    /// <summary>Whether it can connect; problem says what to fix otherwise.</summary>
    [JsonPropertyName("ready")]
    [ReturnSchema(Description = "Whether it can connect; `problem` says what to fix otherwise")]
    public bool Ready { get; init; }

    /// <summary>What an operator has to fix.</summary>
    [JsonPropertyName("problem")]
    [ReturnSchema(Description = "What an operator has to fix")]
    public string? Problem { get; init; }
}

/// <summary>Response of <c>chat_accounts</c>.</summary>
internal sealed record ChatAccountsResponse
{
    /// <summary>The configured accounts.</summary>
    [JsonPropertyName("accounts")]
    [ReturnSchema(Description = "The configured accounts")]
    public IReadOnlyList<ChatAccountDto> Accounts { get; init; } = [];
}
