namespace Orkeon.Infrastructure.Tests.Flows.Base;

public class FlowStepBaseTestsFixture
{
    private readonly TestFlowStep _step = new();

    public FlowStepBaseTestsFixture()
    {
    }

    public FlowStepBaseTestsFixture WithStep(TestFlowStep value)
    {
        // Configure _step as needed
        return this;
    }

    public TestFlowStep GetStep() => _step;

}
