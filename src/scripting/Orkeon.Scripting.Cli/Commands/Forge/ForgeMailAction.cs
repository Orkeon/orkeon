using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>A mail action a try simulated.</summary>
internal sealed record ForgeMailAction
{
    /// <summary>What was done, e.g. <c>send</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    /// <summary>The account it was done for.</summary>
    [JsonPropertyName("account")]
    public string Account { get; init; } = "";

    /// <summary>The recipients.</summary>
    [JsonPropertyName("recipients")]
    public ImmutableList<string> Recipients { get; init; } = [];

    /// <summary>The subject.</summary>
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = "";

    /// <summary>The body.</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = "";

    /// <summary>The folder concerned.</summary>
    [JsonPropertyName("folder")]
    public string Folder { get; init; } = "";

    /// <summary>The destination, new name or saved path.</summary>
    [JsonPropertyName("target")]
    public string Target { get; init; } = "";
}
