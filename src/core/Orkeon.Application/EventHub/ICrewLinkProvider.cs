using System.Collections.Immutable;
using Orkeon.Domain.Common;
using Orkeon.Domain.EventHub;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Supplies the <see cref="CrewLink"/> declarations that authorize hub traffic. A port so the
/// links can come from a crew's YAML today and from a registry later, without the ACL stage
/// knowing which.
/// </summary>
public interface ICrewLinkProvider
{
    /// <summary>
    /// The links declared **by** <paramref name="source"/>. An empty result means the crew
    /// declared none — see <see cref="ICrewLinkPolicy"/> for what that implies.
    /// </summary>
    ImmutableArray<CrewLink> LinksFor(CrewId source);
}

/// <summary>
/// The write side of the link source. A crew's <c>links:</c> block is read from YAML long
/// before the crew has an identity, so the factory hands the links over once the
/// <see cref="CrewId"/> exists — the ACL then reads them synchronously, on the path of every
/// published message, without touching a repository.
/// </summary>
public interface ICrewLinkRegistry : ICrewLinkProvider
{
    /// <summary>
    /// Records what <paramref name="crew"/> declared. Registering an empty list is not the
    /// same as never registering: it still means "this crew declared nothing", which is what
    /// <see cref="ICrewLinkPolicy"/> arbitrates.
    /// </summary>
    void Register(CrewId crew, IReadOnlyList<CrewLink> links);
}

/// <summary>
/// What the ACL does when a crew declared no link at all. Separated from the provider because
/// it is a *decision*, not data, and the wrong default here is either an open door or a
/// subsystem nobody can use.
/// </summary>
public interface ICrewLinkPolicy
{
    /// <summary>
    /// Whether a crew that declared no links may still send. Default deployments answer
    /// <see langword="true"/>: the hub shipped without any ACL, so refusing undeclared
    /// traffic would break every existing crew the day the stage is switched on. A
    /// deployment that wants a closed door answers <see langword="false"/>.
    /// </summary>
    bool AllowsUndeclared { get; }
}

/// <summary>
/// The permissive policy, and the default: a crew with no declared link keeps sending as it
/// did before the ACL existed. Opting into <c>CrewLink</c> is what closes the door.
/// </summary>
public sealed class PermissiveCrewLinkPolicy : ICrewLinkPolicy
{
    /// <summary>Shared instance.</summary>
    public static PermissiveCrewLinkPolicy Instance { get; } = new();

    /// <inheritdoc />
    public bool AllowsUndeclared => true;
}

/// <summary>
/// The closed policy: nothing travels unless a link says so. Appropriate once every crew of a
/// deployment declares its links, and the only safe choice for a host exposed to the outside.
/// </summary>
public sealed class RestrictiveCrewLinkPolicy : ICrewLinkPolicy
{
    /// <summary>Shared instance.</summary>
    public static RestrictiveCrewLinkPolicy Instance { get; } = new();

    /// <inheritdoc />
    public bool AllowsUndeclared => false;
}
