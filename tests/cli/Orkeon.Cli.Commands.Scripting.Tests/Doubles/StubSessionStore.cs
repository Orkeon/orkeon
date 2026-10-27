namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

#pragma warning disable IDE1006 // camelCase method: exposed to scripts as ctx.services.get("session")
/// <summary>
/// A session store a script reads through a <see cref="Task"/>: <see cref="readMode"/> stays pending until the test
/// settles <see cref="Mode"/>, so a <c>completed</c> handler that awaits it really suspends.
/// </summary>
internal sealed class StubSessionStore
{
    private int _reads;

    /// <summary>The value the next read resolves to, once the test sets it.</summary>
    public TaskCompletionSource<string> Mode { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many reads the script made.</summary>
    public int Reads => Volatile.Read(ref _reads);

    public Task<string> readMode()
    {
        Interlocked.Increment(ref _reads);
        return Mode.Task;
    }
}
#pragma warning restore IDE1006
