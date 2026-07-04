using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed class FqnNotFoundException : Exception
{
    public string Fqn { get; }
    public string Reason { get; }
    public ImmutableArray<string> Suggestions { get; }

    public FqnNotFoundException(string fqn, string reason, ImmutableArray<string> suggestions)
        : base($"FQN '{fqn}' not found ({reason})")
    {
        Fqn = fqn;
        Reason = reason;
        Suggestions = suggestions;
    }

    public FqnNotFoundException()
    {
        Fqn = string.Empty;
        Reason = string.Empty;
        Suggestions = ImmutableArray<string>.Empty;
    }

    public FqnNotFoundException(string message)
        : base(message)
    {
        Fqn = string.Empty;
        Reason = string.Empty;
        Suggestions = ImmutableArray<string>.Empty;
    }

    public FqnNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        Fqn = string.Empty;
        Reason = string.Empty;
        Suggestions = ImmutableArray<string>.Empty;
    }
}
