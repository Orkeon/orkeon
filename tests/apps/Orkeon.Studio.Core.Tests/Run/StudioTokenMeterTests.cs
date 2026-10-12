using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Run;

namespace Orkeon.Studio.Core.Tests.Run;

/// <summary>The live count of every model call: exact figure plus the estimates of calls in flight.</summary>
public sealed class StudioTokenMeterTests
{
    private static OrkeonEvent Line(string json)
    {
        Assert.True(OrkeonEventParser.TryParse(json, out var line), json);
        return line!;
    }

    private static OrkeonEvent Started(string id, int prompt, string mode = "Stream") =>
        Line($$"""{"v":2,"seq":1,"ts":"t","kind":"llm.call.started","callId":"{{id}}","mode":"{{mode}}","estimatedPromptTokens":{{prompt}}}""");

    private static OrkeonEvent Progress(string id, int completion) =>
        Line($$"""{"v":2,"seq":2,"ts":"t","kind":"llm.call.progress","callId":"{{id}}","estimatedCompletionTokens":{{completion}}}""");

    private static OrkeonEvent Finished(string id, string counts = "") =>
        Line($$"""{"v":2,"seq":3,"ts":"t","kind":"llm.call.finished","callId":"{{id}}","mode":"Stream","fellBack":false{{counts}}}""");

    private static OrkeonEvent Cost(long prompt, long completion) =>
        Line($$"""{"v":2,"seq":4,"ts":"t","kind":"cost.updated","tokens":{{prompt + completion}},"promptTokens":{{prompt}},"completionTokens":{{completion}},"operation":"agent"}""");

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldShowEstimatedTokensWithATilde_WhileACallIsInFlight()
    {
        var meter = new StudioTokenMeter();
        var run = meter.Begin(ExecutionKind.Run);

        run.Apply(Started("c1", 100));
        run.Apply(Progress("c1", 40));

        Assert.Equal(new TokenFigure(100, 40, true), run.Figure);
        Assert.Equal(new TokenFigure(100, 40, true), meter.Session);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldReplaceTheEstimateByTheProvidersCount_WhenTheCallEnds()
    {
        var meter = new StudioTokenMeter();
        var run = meter.Begin(ExecutionKind.Run);
        run.Apply(Started("c1", 100));
        run.Apply(Progress("c1", 40));

        run.Apply(Finished("c1", ""","promptTokens":90,"completionTokens":35"""));
        Assert.Equal(new TokenFigure(0, 0, false), run.Figure);   // never the estimate on top of the count

        run.Apply(Cost(90, 35));
        Assert.Equal(new TokenFigure(90, 35, false), run.Figure);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldKeepTheTilde_WhenTheProviderNeverCounts()
    {
        var meter = new StudioTokenMeter();
        var run = meter.Begin(ExecutionKind.Run);
        run.Apply(Cost(50, 10));
        run.Apply(Started("c1", 100));
        run.Apply(Progress("c1", 40));

        run.Apply(Finished("c1"));

        Assert.Equal(new TokenFigure(150, 50, true), run.Figure);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldKeepTheEstimateWithATilde_WhenACallFailsWithoutACount()
    {
        var meter = new StudioTokenMeter();
        var run = meter.Begin(ExecutionKind.Run);
        run.Apply(Started("c1", 100));
        run.Apply(Progress("c1", 40));

        run.Apply(Finished("c1", ""","success":false,"promptTokens":null,"completionTokens":null"""));

        Assert.Equal(new TokenFigure(100, 40, true), run.Figure);
    }

    [Fact]
    [Trait("BR", "BR-14")]
    public void ShouldSumEveryExecutionSinceStart_WhenTheScopeIsSession()
    {
        var meter = new StudioTokenMeter();
        var run = meter.Begin(ExecutionKind.Run);
        var forge = meter.Begin(ExecutionKind.Forge);
        var rephrase = meter.Begin(ExecutionKind.Rephrase);
        run.Apply(Cost(100, 10));
        forge.Apply(Cost(200, 20));
        rephrase.Apply(Cost(300, 30));

        run.End();

        Assert.Equal(new TokenFigure(600, 60, false), meter.Session);
    }

    [Fact]
    [Trait("BR", "BR-09")]
    public void ShouldAddTheTokensOfSimultaneousCalls()
    {
        var meter = new StudioTokenMeter();
        var run = meter.Begin(ExecutionKind.Run);

        run.Apply(Started("c1", 100));
        run.Apply(Started("c2", 70, "Buffer"));
        run.Apply(Progress("c1", 40));
        run.Apply(Progress("c2", 25));
        run.Apply(Progress("c1", 60));   // cumulative per call: replaces, never adds

        Assert.Equal(new TokenFigure(170, 85, true), run.Figure);
        Assert.Equal(new TokenFigure(170, 85, true), meter.Session);
    }
}
