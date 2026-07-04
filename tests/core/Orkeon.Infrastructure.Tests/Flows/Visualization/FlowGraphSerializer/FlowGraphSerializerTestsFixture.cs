using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using Orkeon.Infrastructure.Flows.Visualization;

namespace Orkeon.Infrastructure.Tests.Flows.Visualization;

/// <summary>
/// Simple test double implementing IFlowDefinition for visualization tests.
/// </summary>
public class TestFlowDefinition : IFlowDefinition
{
    public FlowId Id { get; set; } = FlowId.Create();
    public string Name { get; set; } = "Test Flow";
    public string Description { get; set; } = "A test flow definition";
    public FlowType Type { get; set; } = FlowType.Sequential;
    public IReadOnlyList<FlowStep> Steps { get; set; } = Array.Empty<FlowStep>();
    public FlowConfiguration Configuration { get; set; } = new();

    public bool Validate(out IReadOnlyList<string> errors)
    {
        errors = [];
        return true;
    }
}

public class FlowGraphSerializerTestsFixture
{
    private TestFlowDefinition _definition = new();

    public FlowGraphSerializerTestsFixture WithDefinition(TestFlowDefinition definition)
    {
        _definition = definition;
        return this;
    }

    public FlowGraphSerializerTestsFixture WithSteps(params FlowStep[] steps)
    {
        _definition.Steps = steps.ToList().AsReadOnly();
        return this;
    }

    public FlowGraphSerializerTestsFixture WithFlowName(string name)
    {
        _definition.Name = name;
        return this;
    }

    public FlowGraph Serialize() => FlowGraphSerializer.Serialize(_definition);

    public static FlowGraph SerializeDefinition(IFlowDefinition definition)
        => FlowGraphSerializer.Serialize(definition);

    public static string ExportToMermaid(FlowGraph graph)
        => FlowGraphSerializer.ExportToMermaid(graph);

    public TestFlowDefinition GetDefinition() => _definition;
}
