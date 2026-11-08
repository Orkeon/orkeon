using Orkeon.Application.Runs;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>An <see cref="IRunProcessProbe"/> the test scripts: the processes it was told are alive are, every other one is gone.</summary>
internal sealed class FakeRunProcessProbe : IRunProcessProbe
{
    /// <summary>The process ids this probe finds.</summary>
    public HashSet<int> Alive { get; } = [];

    /// <inheritdoc />
    public bool IsAlive(int pid, DateTimeOffset? startedAt) => Alive.Contains(pid);
}
