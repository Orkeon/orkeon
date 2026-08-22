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
    /// The links declared **by** <paramref name="source"/>. A <c>default</c> array means the
    /// crew never declared a <c>links:</c> block at all — <see cref="ICrewLinkPolicy"/>
    /// arbitrates what that implies. An **empty** array means the crew declared one whose
    /// entries all failed to parse (or wrote none): the door is closed, and a malformed
    /// authorization must never become a permissive one.
    /// </summary>
    ImmutableArray<CrewLink> LinksFor(CrewId source);

    /// <summary>
    /// The declared <c>name:</c> of <paramref name="crew"/>, or <see langword="null"/> when
    /// the crew is unknown to this provider. Links name crews by name — the only identity a
    /// YAML author has — while messages carry ids, so the ACL needs this translation on the
    /// path of every targeted message.
    /// </summary>
    string? NameOf(CrewId crew);
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
    /// Records what <paramref name="crew"/> (named <paramref name="crewName"/>) declared.
    /// Every crew registers — even one with no <c>links:</c> block, passing
    /// <see langword="null"/> — because other crews' links name it and the ACL must be able
    /// to resolve its id back to its name. A <see langword="null"/> <paramref name="links"/>
    /// means "never declared"; an empty list means "declared, and the door is closed".
    /// </summary>
    void Register(CrewId crew, string crewName, IReadOnlyList<CrewLink>? links);
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
    /// deployment that wants a closed door answers <see langword="false"/> — an undeclared
    /// sender then only gets through when the *target* crew granted it an inbound link
    /// (spec §10.3).
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
