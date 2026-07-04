using Orkeon.Infrastructure.Training;

namespace Orkeon.Infrastructure.Tests.Training;

public class AgentPerformanceTrackerTestsFixture
{
    public static AgentPerformanceTracker CreateTracker()
    {
        return new AgentPerformanceTracker();
    }
}
