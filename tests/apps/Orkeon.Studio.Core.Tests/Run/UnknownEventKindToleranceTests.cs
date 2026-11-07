using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Run;

namespace Orkeon.Studio.Core.Tests.Run;

/// <summary>
/// FWK-05, the first lot to add a kind (llm.fallback): a screen folding a stream must ignore a kind it
/// does not fold, so a kind added later never breaks an older Studio.
/// </summary>
public sealed class UnknownEventKindToleranceTests
{
    [Theory]
    [InlineData(Orkeon.Constants.Protocol.RunEventKinds.LlmFallback)]
    [InlineData("kind.from.the.future")]
    public void A_kind_the_model_does_not_fold_changes_nothing_and_raises_nothing(string kind)
    {
        var model = new RunProgressModel();
        var raised = 0;
        model.Changed += (_, _) => raised++;
        Assert.True(OrkeonEventParser.TryParse(
            $$"""{"v":2,"seq":1,"ts":"t","kind":"{{kind}}","profile":"main","from":"a","to":"b","reason":"server_error"}""",
            out var orkeonEvent));

        model.Apply(orkeonEvent!);

        Assert.Equal(0, raised);
        Assert.Null(model.Cost);
    }

    [Fact]
    public void The_fallback_kind_is_a_declared_kind_of_the_protocol()
    {
        Assert.Equal("llm.fallback", Orkeon.Constants.Protocol.RunEventKinds.LlmFallback);
        Assert.Contains(Orkeon.Constants.Protocol.RunEventKinds.LlmFallback, Orkeon.Constants.Protocol.RunEventKinds.All);
    }
}
