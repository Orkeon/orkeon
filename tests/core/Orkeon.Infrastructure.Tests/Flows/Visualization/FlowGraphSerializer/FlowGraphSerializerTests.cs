using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using Orkeon.Infrastructure.Flows.Visualization;

namespace Orkeon.Infrastructure.Tests.Flows.Visualization;

public class FlowGraphSerializerTests
{
    private readonly FlowGraphSerializerTestsFixture _fixture = new();

    [Fact]
    public void Serialize_CreatesNodesFromSteps()
    {
        // Arrange
        var id1 = FlowStepId.Create();
        var id2 = FlowStepId.Create();
        var id3 = FlowStepId.Create();
        var steps = new[]
        {
            new FlowStep { Id = id1, Name = "Step One", Type = "tool" },
            new FlowStep { Id = id2, Name = "Step Two", Type = "llm" },
            new FlowStep { Id = id3, Name = "Step Three", Type = "crew" }
        };

        _fixture.WithFlowName("My Flow").WithSteps(steps);

        // Act
        var graph = _fixture.Serialize();

        // Assert
        Assert.Equal("My Flow", graph.FlowName);
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Contains(graph.Nodes, n => n.Id == (string)id1 && n.Name == "Step One" && n.Type == "tool");
        Assert.Contains(graph.Nodes, n => n.Id == (string)id2 && n.Name == "Step Two" && n.Type == "llm");
        Assert.Contains(graph.Nodes, n => n.Id == (string)id3 && n.Name == "Step Three" && n.Type == "crew");
    }

    [Fact]
    public void Serialize_CreatesEdgesFromDependencies()
    {
        // Arrange
        var id1 = FlowStepId.Create();
        var id2 = FlowStepId.Create();
        var id3 = FlowStepId.Create();
        var steps = new[]
        {
            new FlowStep { Id = id1, Name = "Step One", Type = "tool" },
            new FlowStep
            {
                Id = id2, Name = "Step Two", Type = "llm",
                Dependencies = [id1]
            },
            new FlowStep
            {
                Id = id3, Name = "Step Three", Type = "tool",
                Dependencies = [id1, id2]
            }
        };

        _fixture.WithSteps(steps);

        // Act
        var graph = _fixture.Serialize();

        // Assert
        Assert.Equal(3, graph.Edges.Count);
        Assert.Contains(graph.Edges, e => e.SourceId == (string)id1 && e.TargetId == (string)id2);
        Assert.Contains(graph.Edges, e => e.SourceId == (string)id1 && e.TargetId == (string)id3);
        Assert.Contains(graph.Edges, e => e.SourceId == (string)id2 && e.TargetId == (string)id3);
    }

    [Fact]
    public void Serialize_NoDependencies_ProducesNoEdges()
    {
        // Arrange
        var steps = new[]
        {
            new FlowStep { Id = FlowStepId.Create(), Name = "Step One", Type = "tool" },
            new FlowStep { Id = FlowStepId.Create(), Name = "Step Two", Type = "llm" }
        };

        _fixture.WithSteps(steps);

        // Act
        var graph = _fixture.Serialize();

        // Assert
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Serialize_ThrowsOnNullDefinition()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            FlowGraphSerializerTestsFixture.SerializeDefinition(null!));
    }

    [Fact]
    public void ExportToMermaid_ProducesValidFormat()
    {
        // Arrange
        var graph = new FlowGraph(
            "Test Flow",
            [
                new("s1", "Step One", "tool"),
                new("s2", "Step Two", "llm")
            ],
            [
                new("s1", "s2")
            ]);

        // Act
        var mermaid = FlowGraphSerializerTestsFixture.ExportToMermaid(graph);

        // Assert
        Assert.StartsWith("graph TD", mermaid);
        Assert.Contains("s1[\"Step One\"]", mermaid);
        Assert.Contains("s2[\"Step Two\"]", mermaid);
        Assert.Contains("s1 --> s2", mermaid);
    }

    [Fact]
    public void ExportToMermaid_ConditionalNodes_UseDiamondShape()
    {
        // Arrange
        var graph = new FlowGraph(
            "Conditional Flow",
            [
                new("c1", "Check Condition", "conditional")
            ],
            []);

        // Act
        var mermaid = FlowGraphSerializerTestsFixture.ExportToMermaid(graph);

        // Assert — Mermaid diamond shape uses double braces: {{}}, which in the C# interpolated
        // string become escaped to {{...}} in the output
        Assert.Contains("c1{{Check Condition}}", mermaid);
    }

    [Fact]
    public void ExportToMermaid_CrewNodes_UseBracketShape()
    {
        // Arrange
        var graph = new FlowGraph(
            "Crew Flow",
            [
                new("cr1", "Run Crew", "crew")
            ],
            []);

        // Act
        var mermaid = FlowGraphSerializerTestsFixture.ExportToMermaid(graph);

        // Assert
        Assert.Contains("cr1[[\"Run Crew\"]]", mermaid);
    }

    [Fact]
    public void ExportToMermaid_EdgeLabels_AreIncluded()
    {
        // Arrange
        var graph = new FlowGraph(
            "Labeled Flow",
            [
                new("s1", "Start", "tool"),
                new("s2", "End", "tool")
            ],
            [
                new("s1", "s2", "on success")
            ]);

        // Act
        var mermaid = FlowGraphSerializerTestsFixture.ExportToMermaid(graph);

        // Assert
        Assert.Contains("s1 -->|on success| s2", mermaid);
    }

    [Fact]
    public void ExportToMermaid_ThrowsOnNullGraph()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            FlowGraphSerializerTestsFixture.ExportToMermaid(null!));
    }

    [Fact]
    public void Serialize_StepWithNullType_DefaultsToUnknown()
    {
        // Arrange
        var step = new FlowStep { Id = FlowStepId.Create(), Name = "Step One", Type = null! };
        _fixture.WithSteps(step);

        // Act
        var graph = _fixture.Serialize();

        // Assert
        Assert.Single(graph.Nodes);
        Assert.Equal("unknown", graph.Nodes[0].Type);
    }
}
