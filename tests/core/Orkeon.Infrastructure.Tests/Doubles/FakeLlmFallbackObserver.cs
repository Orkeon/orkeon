using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>An <see cref="ILlmFallbackObserver"/> that keeps what it is told.</summary>
public sealed class FakeLlmFallbackObserver : ILlmFallbackObserver
{
    /// <summary>The switches told so far: profile, from, to, reason.</summary>
    public readonly List<(string Profile, string From, string To, string Reason)> Taken = [];

    /// <inheritdoc />
    public void FallbackTaken(string profile, string fromProfile, string toProfile, string reason) => Taken.Add((profile, fromProfile, toProfile, reason));
}
