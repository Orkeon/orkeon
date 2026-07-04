using System.Collections.Immutable;
using Orkeon.Domain.Common;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Optional parameters for <c>IEventHub.PublishAsync</c>.
/// </summary>
public sealed record PublishOptions
{
    /// <summary>Scope the publish to a single crew. <see langword="null"/> ⇒ global broadcast.</summary>
    public CrewId? TargetCrewId { get; init; }

    /// <summary>Metadata copied verbatim into the message envelope.</summary>
    public ImmutableDictionary<string, string>? Metadata { get; init; }

    /// <summary>When <see langword="true"/>, retain the payload in the <c>LastValueCache</c> under <see cref="LastValueKey"/>.</summary>
    public bool RetainAsLastValue { get; init; }

    /// <summary>Cache key for <see cref="RetainAsLastValue"/>. Ignored when <see cref="RetainAsLastValue"/> is <see langword="false"/>.</summary>
    public string? LastValueKey { get; init; }

    /// <summary>Optional schema identifier propagated into the <c>Message.SchemaId</c> field.</summary>
    public string? SchemaId { get; init; }
}
