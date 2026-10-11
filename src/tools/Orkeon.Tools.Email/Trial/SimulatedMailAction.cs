using System.Collections.Immutable;

namespace Orkeon.Tools.Email.Trial;

/// <summary>An action a trial simulated.</summary>
public sealed record SimulatedMailAction
{
    /// <summary>What was done.</summary>
    public required SimulatedMailActionKind Kind { get; init; }

    /// <summary>The account it was done for.</summary>
    public string Account { get; init; } = "";

    /// <summary>The recipients of a send or a draft.</summary>
    public ImmutableList<string> Recipients { get; init; } = [];

    /// <summary>The subject of a send or a draft.</summary>
    public string Subject { get; init; } = "";

    /// <summary>The body of a send or a draft.</summary>
    public string Body { get; init; } = "";

    /// <summary>The message concerned.</summary>
    public string MessageId { get; init; } = "";

    /// <summary>The folder concerned.</summary>
    public string Folder { get; init; } = "";

    /// <summary>The destination, new name or saved path.</summary>
    public string Target { get; init; } = "";
}
