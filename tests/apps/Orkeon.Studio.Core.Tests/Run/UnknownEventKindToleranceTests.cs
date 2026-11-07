using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Run;

namespace Orkeon.Studio.Core.Tests.Run;

/// <summary>
/// A run's event stream grows kinds over time (<c>llm.fallback</c>, FWK-05; <c>graph.node.entered</c>,
/// FWK-27): a screen folding a stream ignores a kind it has not been taught, keeps what it shows and
/// says nothing changed, so a kind added later never breaks an older Studio. One class for every kind
/// a lot adds.
/// </summary>
public sealed class UnknownEventKindToleranceTests
{
    private static OrkeonEvent Parse(string line)
    {
        Assert.True(OrkeonEventParser.TryParse(line, out var orkeonEvent), line);
        return orkeonEvent!;
    }

    [Theory]
    [InlineData(Orkeon.Constants.Protocol.RunEventKinds.LlmFallback)]
    [InlineData(Orkeon.Constants.Protocol.RunEventKinds.GraphNodeEntered)]
    [InlineData("kind.from.the.future")]
    public void A_kind_the_model_does_not_fold_changes_nothing_and_raises_nothing(string kind)
    {
        var model = new RunProgressModel();
        var raised = 0;
        model.Changed += (_, _) => raised++;

        model.Apply(Parse($$"""{"v":2,"seq":1,"ts":"t","kind":"{{kind}}","node":"first","visit":1,"profile":"main","from":"a","to":"b"}"""));

        Assert.Equal(0, raised);
        Assert.Empty(model.Tasks);
        Assert.Null(model.Cost);
        Assert.False(model.Finished);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","kind":"cost.updated","tokens":10}"""));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void The_new_kinds_are_declared_kinds_of_the_protocol()
    {
        Assert.Equal("llm.fallback", Orkeon.Constants.Protocol.RunEventKinds.LlmFallback);
        Assert.Equal("graph.node.entered", RunEventKinds.GraphNodeEntered);
        Assert.Contains(Orkeon.Constants.Protocol.RunEventKinds.LlmFallback, Orkeon.Constants.Protocol.RunEventKinds.All);
        Assert.Contains(RunEventKinds.GraphNodeEntered, Orkeon.Constants.Protocol.RunEventKinds.All);
        Assert.Equal(19, Orkeon.Constants.Protocol.RunEventKinds.All.Count);
    }
}
