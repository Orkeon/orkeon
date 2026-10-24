using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Tests.Doubles;

internal sealed class FakeOverlapProbe
{
    private int _active;
    public int MaxActive;
    public List<string> Events { get; } = new();

    public void InCallback(string name) => Enter(name);

    public async Task InToolAsync()
    {
        Enter("tool");
        await Task.Delay(10).ConfigureAwait(false);
        Interlocked.Decrement(ref _active);
    }

    private void Enter(string name)
    {
        var now = Interlocked.Increment(ref _active);
        lock (Events)
        {
            Events.Add(name);
            MaxActive = Math.Max(MaxActive, now);
        }
        if (name != "tool")
        {
            Thread.Sleep(10);
            Interlocked.Decrement(ref _active);
        }
    }
}
