namespace Orkeon.Tools.Code.Tests.Doubles;

/// <summary>
/// A hand-written stand-in for the process environment: <see cref="Read"/> answers from
/// <see cref="Variables"/>, so a test never reads nor changes the machine's real variables.
/// </summary>
internal sealed class StubEnvironment
{
    /// <summary>The variables the environment holds.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>The value of <paramref name="name"/>, or null when it is unset.</summary>
    public string? Read(string name) => Variables.TryGetValue(name, out var value) ? value : null;
}
