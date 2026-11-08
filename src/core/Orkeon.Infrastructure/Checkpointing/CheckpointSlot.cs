namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// The slot of a task invocation among invocations of the same task that run at once (Consensual runs one task for several
/// agents concurrently): the checkpoint key of the invocation counts its ordinal within the slot (<c>{TaskKey}@{slot}#{n}</c>),
/// so the key does not depend on which concurrent invocation reached the decorator first and a resumed run finds each
/// agent's own checkpoint. It flows with the asynchronous control flow, like <see cref="TaskInputScope"/>.
/// </summary>
internal static class CheckpointSlot
{
    private static readonly AsyncLocal<string?> Slot = new();

    /// <summary>The slot of the invocation on this control flow, or null.</summary>
    public static string? Current => Slot.Value;

    /// <summary>Makes <paramref name="slot"/> the slot until the returned token is disposed.</summary>
    /// <param name="slot">A stable name of the invocation among its concurrent siblings (the agent's position in the vote).</param>
    /// <returns>The token that restores the previous slot.</returns>
    public static IDisposable Enter(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        var previous = Slot.Value;
        Slot.Value = slot;
        return new Pop(previous);
    }

    private sealed class Pop(string? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
