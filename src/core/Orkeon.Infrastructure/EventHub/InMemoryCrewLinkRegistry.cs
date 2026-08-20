using System.Collections.Concurrent;
using System.Collections.Immutable;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;
using Orkeon.Domain.EventHub;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// Holds the <c>links:</c> declarations of every crew created in this process (HUB-03).
/// <para>
/// A lookup happens on the path of every targeted message, so it has to be synchronous and
/// allocation-free; going back to <c>ICrewRepository</c> for each publish would make the ACL
/// an I/O stage. The registry is filled once, at crew creation, by <c>CrewFactory</c>.
/// </para>
/// <para>
/// In-process only, deliberately: rc.2's hub is in-process too. A distributed deployment needs
/// a distributed hub first, and a shared link source is the smaller half of that problem.
/// </para>
/// </summary>
public sealed class InMemoryCrewLinkRegistry : ICrewLinkRegistry
{
    private readonly ConcurrentDictionary<CrewId, ImmutableArray<CrewLink>> _links = new();

    /// <inheritdoc />
    public void Register(CrewId crew, IReadOnlyList<CrewLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        // Last declaration wins: recreating a crew from an edited configuration must not leave
        // the door open on the links the previous version declared.
        _links[crew] = [.. links];
    }

    /// <inheritdoc />
    public ImmutableArray<CrewLink> LinksFor(CrewId source) =>
        _links.TryGetValue(source, out var links) ? links : [];
}
