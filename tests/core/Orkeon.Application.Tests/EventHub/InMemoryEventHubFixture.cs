using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.EventHub;

namespace Orkeon.Application.Tests.EventHub;

/// <summary>
/// xUnit fixture providing a fresh <see cref="InMemoryEventHub"/> + caller context per test class.
/// Disposed (and channels closed) at the end of the test suite.
/// </summary>
public sealed class InMemoryEventHubFixture : IDisposable
{
    /// <summary>The hub under test.</summary>
    public InMemoryEventHub Hub { get; }

    /// <summary>The caller-context used to scope publishes/subscribes.</summary>
    public DefaultEventHubCallerContext Caller { get; }

    /// <summary>Initializes the fixture.</summary>
    public InMemoryEventHubFixture()
    {
        Caller = new DefaultEventHubCallerContext();
        Hub = new InMemoryEventHub(Caller, NullLogger<InMemoryEventHub>.Instance);
    }

    /// <inheritdoc/>
    public void Dispose() => Hub.Dispose();
}
