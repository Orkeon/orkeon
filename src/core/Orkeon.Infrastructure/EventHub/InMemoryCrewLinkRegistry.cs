using System.Collections.Concurrent;
using System.Collections.Immutable;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;
using Orkeon.Domain.EventHub;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// Holds the <c>links:</c> declarations — and the id ↔ name mapping the ACL needs to read
/// them — of every crew created in this process (HUB-03).
/// <para>
/// A lookup happens on the path of every targeted message, so it has to be synchronous and
/// allocation-free; going back to <c>ICrewRepository</c> for each publish would make the ACL
/// an I/O stage. The registry is filled once, at crew creation, by <c>CrewFactory</c> — every
/// crew registers its name even when it declared no links, because *other* crews' links name
/// it and an unresolvable target would silently match nothing.
/// </para>
/// <para>
/// In-process only, deliberately: rc.2's hub is in-process too. A distributed deployment needs
/// a distributed hub first, and a shared link source is the smaller half of that problem.
/// </para>
/// </summary>
public sealed class InMemoryCrewLinkRegistry : ICrewLinkRegistry
{
    private readonly ConcurrentDictionary<CrewId, Entry> _crews = new();

    /// <inheritdoc />
    public void Register(CrewId crew, string crewName, IReadOnlyList<CrewLink>? links)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentException.ThrowIfNullOrWhiteSpace(crewName);

        // Last declaration wins: recreating a crew from an edited configuration must not leave
        // the door open on the links the previous version declared. `default` (never declared)
        // and `[]` (declared, closed) are distinct on purpose — see ICrewLinkProvider.
        _crews[crew] = new Entry(crewName, links is null ? default : [.. links]);
    }

    /// <inheritdoc />
    public ImmutableArray<CrewLink> LinksFor(CrewId source) =>
        _crews.TryGetValue(source, out var entry) ? entry.Links : default;

    /// <inheritdoc />
    public string? NameOf(CrewId crew) =>
        _crews.TryGetValue(crew, out var entry) ? entry.Name : null;

    private sealed record Entry(string Name, ImmutableArray<CrewLink> Links);
}
