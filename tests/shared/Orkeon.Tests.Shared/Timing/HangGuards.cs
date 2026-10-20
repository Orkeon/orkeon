namespace Orkeon.Tests.Shared.Timing;

/// <summary>
/// The named bounds under which a test waits for something that must never hang.
/// </summary>
public static class HangGuards
{
    /// <summary>
    /// The single bound under which a test waits for a Jint engine (its <c>PromiseTimeout</c>): never
    /// reached by a slow runner, only by a real hang.
    /// </summary>
    public static readonly TimeSpan EnginePromise = TimeSpan.FromMinutes(2);
}
