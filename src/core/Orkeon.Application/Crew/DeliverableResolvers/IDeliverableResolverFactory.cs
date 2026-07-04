using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.DeliverableResolvers;

/// <summary>
/// Dispatches to the appropriate <see cref="IDeliverableResolver"/> for a given
/// <see cref="DeliverableSource"/>. Returns <c>null</c> when no resolver is registered
/// for the requested source (e.g. <see cref="DeliverableSource.ToolCall"/> keeps
/// legacy behavior — the agent handles persistence via file_write tool).
/// </summary>
public interface IDeliverableResolverFactory
{
    /// <summary>Returns the resolver for <paramref name="source"/>, or <c>null</c> when none exists.</summary>
    IDeliverableResolver? GetFor(DeliverableSource source);
}
