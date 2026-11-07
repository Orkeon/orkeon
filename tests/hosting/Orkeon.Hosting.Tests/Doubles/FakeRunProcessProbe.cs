using Orkeon.Application.Runs;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>An <see cref="IRunProcessProbe"/> the test scripts: the processes it was told are alive are, every other one is gone.</summary>
internal sealed class FakeRunProcessProbe : IRunProcessProbe
{
    /// <summary>The process ids this probe finds.</summary>
    public HashSet<int> Alive { get; } = [];

    /// <summary>The ids the probe was asked about, in order.</summary>
    public List<int> Asked { get; } = [];

    /// <inheritdoc />
    public bool IsAlive(int pid, DateTimeOffset? startedAt)
    {
        Asked.Add(pid);
        return Alive.Contains(pid);
    }
}
