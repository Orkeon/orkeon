using Orkeon.Application.EventHub;
using Orkeon.Domain.Crew;

namespace Orkeon.Application.Tests.EventHub;

public sealed class CrewBuilderOnEventTests
{
    private static readonly string[] ExpectedTopics = ["a.created", "b.updated", "c.removed"];

    [Fact]
    public void OnEvent_records_binding_on_builder()
    {
        var builder = new CrewBuilder().Goal("test-goal");
        builder.OnEvent("orders.created", _ => System.Threading.Tasks.Task.CompletedTask);

        var bindings = builder.GetEventBindings();
        Assert.Single(bindings);
        Assert.Equal("orders.created", bindings[0].Topic);
    }

    [Fact]
    public void OnEvent_chains_fluently_with_multiple_topics()
    {
        var builder = new CrewBuilder()
            .Goal("test-goal")
            .OnEvent("a.created", _ => System.Threading.Tasks.Task.CompletedTask)
            .OnEvent("b.updated", _ => System.Threading.Tasks.Task.CompletedTask)
            .OnEvent("c.removed", _ => System.Threading.Tasks.Task.CompletedTask);

        var bindings = builder.GetEventBindings();
        Assert.Equal(3, bindings.Count);
        Assert.Equal(ExpectedTopics, bindings.Select(b => b.Topic));
    }

    [Fact]
    public void OnEvent_rejects_null_or_empty_topic()
    {
        var builder = new CrewBuilder().Goal("g");
        Assert.Throws<ArgumentException>(() => builder.OnEvent("", _ => System.Threading.Tasks.Task.CompletedTask));
        Assert.Throws<ArgumentException>(() => builder.OnEvent("   ", _ => System.Threading.Tasks.Task.CompletedTask));
    }

    [Fact]
    public void OnEvent_rejects_null_handler()
    {
        var builder = new CrewBuilder().Goal("g");
        Assert.Throws<ArgumentNullException>(() => builder.OnEvent("topic", handler: null!));
    }

    [Fact]
    public void GetEventBindings_returns_empty_when_none_declared()
    {
        var builder = new CrewBuilder().Goal("g");
        Assert.Empty(builder.GetEventBindings());
    }
}
