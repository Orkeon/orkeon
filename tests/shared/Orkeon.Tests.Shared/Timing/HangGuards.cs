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

    /// <summary>
    /// The bound under which an integration test waits for a real program of the machine to start (a
    /// headless Firefox opening its remote protocol): a cold start on a loaded runner took more than the
    /// product's 30 s default (#379), so a test that is not about that default never runs under it.
    /// </summary>
    public static readonly TimeSpan ProcessStart = TimeSpan.FromMinutes(2);
}
