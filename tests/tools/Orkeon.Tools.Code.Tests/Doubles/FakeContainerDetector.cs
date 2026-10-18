using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Tests.Doubles;

/// <summary>A hand-written <see cref="IContainerDetector"/>: answers <see cref="InContainer"/> and counts the calls.</summary>
internal sealed class FakeContainerDetector : IContainerDetector
{
    /// <summary>What <see cref="IsInContainer"/> answers.</summary>
    public bool InContainer { get; set; }

    /// <summary>How many times the detector was asked.</summary>
    public int Calls { get; private set; }

    public bool IsInContainer()
    {
        Calls++;
        return InContainer;
    }
}
