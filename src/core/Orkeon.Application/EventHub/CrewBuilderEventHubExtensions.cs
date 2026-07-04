using System.Runtime.CompilerServices;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using CrewBuilder = Orkeon.Domain.Crew.CrewBuilder;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Topic + handler binding declared via <see cref="CrewBuilderEventHubExtensions.OnEvent"/>.
/// </summary>
public sealed record CrewEventBinding(
    string Topic,
    Func<CrewEventContext, System.Threading.Tasks.Task> Handler);

/// <summary>
/// Context passed to a <c>OnEvent</c> handler when a matching <see cref="EventHub.Message"/> arrives.
/// </summary>
/// <param name="Crew">The crew that owns the binding.</param>
/// <param name="Message">The triggering message.</param>
public sealed record CrewEventContext(DomainCrew Crew, Message Message);

/// <summary>
/// v1.0 minimalist fluent extension allowing crew authors to declare event handlers.
/// The wiring to <see cref="IEventHub"/> proper arrives with v1.2; v1.0 simply records
/// the bindings on the builder for downstream consumers to fetch.
/// </summary>
public static class CrewBuilderEventHubExtensions
{
    private static readonly ConditionalWeakTable<CrewBuilder, List<CrewEventBinding>> s_bindings = new();

    /// <summary>
    /// Records a (topic, handler) binding on <paramref name="builder"/>. Chainable.
    /// </summary>
    /// <remarks>
    /// v1.2 (per spec §15) will introduce <c>.Links</c> and the middleware pipeline that actually
    /// dispatches incoming messages to these handlers. v1.0 only persists the declarations.
    /// </remarks>
    public static CrewBuilder OnEvent(
        this CrewBuilder builder,
        string topic,
        Func<CrewEventContext, System.Threading.Tasks.Task> handler)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var list = s_bindings.GetValue(builder, _ => new List<CrewEventBinding>());
        list.Add(new CrewEventBinding(topic, handler));
        return builder;
    }

    /// <summary>
    /// Returns the bindings declared on <paramref name="builder"/>. Empty when none were declared.
    /// </summary>
    public static IReadOnlyList<CrewEventBinding> GetEventBindings(this CrewBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return s_bindings.TryGetValue(builder, out var list)
            ? list.AsReadOnly()
            : Array.Empty<CrewEventBinding>();
    }
}
