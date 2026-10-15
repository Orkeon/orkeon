using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>
/// An <see cref="IReplLineInterceptor"/> whose prompt and answer the test sets, and which keeps the lines the loop
/// offered it.
/// </summary>
public sealed class StubReplLineInterceptor : IReplLineInterceptor
{
    /// <summary>The prompt the loop shows instead of its own; null leaves the runner's.</summary>
    public string? PromptOverride { get; set; }

    /// <summary>Whether a line is taken; every line by default.</summary>
    public Func<string, bool> Consume { get; set; } = _ => true;

    /// <summary>The lines offered, in order.</summary>
    public List<string> Offered { get; } = [];

    /// <inheritdoc />
    public bool TryConsume(string line)
    {
        Offered.Add(line);
        return Consume(line);
    }
}
