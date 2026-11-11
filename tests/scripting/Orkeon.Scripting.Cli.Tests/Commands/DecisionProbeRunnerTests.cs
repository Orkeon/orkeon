using System.Buffers.Binary;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Constants.Configuration;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.Decision.SystemOne;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The decision modes behind <c>orkeon llm probe -p typesafe</c>, judged against a stubbed
/// model: a right answer passes, a wrong one fails, and a throw is a finding, never a crash.
/// </summary>
public sealed class DecisionProbeRunnerTests
{
    private static async Task<LlmProbeResult> RunAsync(StubDecisionModel model, LlmProbeMode mode)
    {
        var results = await new DecisionProbeRunner(model).RunAsync([mode], TestContext.Current.CancellationToken);
        return Assert.Single(results);
    }

    private static DecisionModelException Refusal(int status, bool transient, string message = "refused") =>
        new(message, status, transient);

    [Fact]
    public async Task ShouldPassD1_WhenTheNoulLeansYesAndTokensAreCounted()
    {
        var result = await RunAsync(new StubDecisionModel(), LlmProbeMode.D1);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("noul=0.9", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSendTheNoulWithItsInstructions_ForD1()
    {
        var model = new StubDecisionModel();

        await RunAsync(model, LlmProbeMode.D1);

        var question = Assert.IsType<NoulQuestion>(Assert.Single(Assert.Single(model.Requests).Questions).Value);
        Assert.NotNull(question.Instructions);
    }

    [Theory]
    [InlineData(0.2, 300)]
    [InlineData(0.9, null)]
    public async Task ShouldFailD1_WhenTheNoulLeansNoOrNoTokenIsCounted(double noul, int? tokens)
    {
        var model = new StubDecisionModel { Noul = new NoulAnswer { Noul = noul }, InputTokens = tokens };

        var result = await RunAsync(model, LlmProbeMode.D1);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldPassD2_WhenTheObviousOptionIsChosen()
    {
        var result = await RunAsync(new StubDecisionModel(), LlmProbeMode.D2);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task ShouldFailD2_WhenAnotherOptionIsChosen()
    {
        var model = new StubDecisionModel();
        model.Choice = model.Choice with { Choice = "billing" };

        var result = await RunAsync(model, LlmProbeMode.D2);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("choice=billing", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2.0, true)]
    [InlineData(1.2, true)]
    [InlineData(0.3, false)]
    public async Task ShouldJudgeD3_ByWhereTheScoreLands(double score, bool passes)
    {
        var model = new StubDecisionModel();
        model.Score = model.Score with { Score = score };

        var result = await RunAsync(model, LlmProbeMode.D3);

        Assert.Equal(passes ? LlmProbeOutcome.Passed : LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldPassD4_WhenTheBoxTouchesTheDigits()
    {
        var result = await RunAsync(new StubDecisionModel(), LlmProbeMode.D4);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Contains("IoU=", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0, 1.0)]
    [InlineData(0.8, 0.95, 0.8, 0.95)]
    [InlineData(0.6, 0.4, 0.3, 0.7)]
    public async Task ShouldFailD4_WhenTheBoxIsTheWholeImageMissesTheDigitsOrIsInverted(
        double top, double bottom, double left, double right)
    {
        var model = new StubDecisionModel
        {
            Box = new BoxAnswer { Box = new BoxCoordinates { Top = top, Bottom = bottom, Left = left, Right = right } },
        };

        var result = await RunAsync(model, LlmProbeMode.D4);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldMarkD4NotApplicable_WhenTheProviderTakesNoBox()
    {
        var model = new StubDecisionModel();
        var runner = new DecisionProbeRunner(model) { SupportsBox = false };

        var result = Assert.Single(await runner.RunAsync([LlmProbeMode.D4], TestContext.Current.CancellationToken));

        Assert.Equal(LlmProbeOutcome.NotApplicable, result.Outcome);
        Assert.Empty(model.Requests);
    }

    /// <summary>TypeSafe rejects a side under 280 pixels: the image D4 sends must clear it.</summary>
    [Fact]
    public async Task ShouldSendAPngOfAtLeast280PixelsASide_ForD4()
    {
        var model = new StubDecisionModel();

        await RunAsync(model, LlmProbeMode.D4);

        var image = Assert.Single(Assert.Single(model.Requests).Assets!).Value;
        Assert.Equal("png", image.Format);
        var png = Convert.FromBase64String(image.DataBase64);
        Assert.True(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) >= 280);
        Assert.True(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) >= 280);
    }

    [Fact]
    public async Task ShouldPassD5_WhenTheOversizedStateIsRefusedWithTheLimitNamed()
    {
        var model = new StubDecisionModel
        {
            Failure = Refusal(400, false, $"the state is over the model's context limit ({DecisionLimits.StandardContextTokens} tokens)"),
        };

        var result = await RunAsync(model, LlmProbeMode.D5);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task ShouldFailD5_WhenTheOversizedStateIsAnswered()
    {
        var result = await RunAsync(new StubDecisionModel(), LlmProbeMode.D5);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldFailD5_WhenTheRefusalDoesNotNameTheLimit()
    {
        var model = new StubDecisionModel { Failure = Refusal(400, false, "bad request") };

        var result = await RunAsync(model, LlmProbeMode.D5);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldPassD6_WhenTheInvalidModelIsATypedPermanentError()
    {
        var model = new StubDecisionModel { Failure = Refusal(400, false) };

        var result = await RunAsync(model, LlmProbeMode.D6);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
        Assert.Equal(DecisionProbeRunner.NonexistentModel, Assert.Single(model.Requests).Model);
    }

    [Theory]
    [InlineData(503, true)]
    [InlineData(0, false)]
    public async Task ShouldFailD6_WhenTheErrorIsTransientOrHasNoClientStatus(int status, bool transient)
    {
        var model = new StubDecisionModel { Failure = Refusal(status, transient) };

        var result = await RunAsync(model, LlmProbeMode.D6);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldFailD6_WhenTheInvalidModelIsAnswered()
    {
        var result = await RunAsync(new StubDecisionModel(), LlmProbeMode.D6);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldPassD7_WhenTheCallStopsOnCancellation()
    {
        var result = await RunAsync(new StubDecisionModel { Hang = true }, LlmProbeMode.D7);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task ShouldFailD7_WhenTheCallCompletesDespiteCancellation()
    {
        var result = await RunAsync(new StubDecisionModel(), LlmProbeMode.D7);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldPassD8_WhenTheLongStateIsAnsweredYesOverTheStandardContext()
    {
        var model = new StubDecisionModel { InputTokens = 100_500 };

        var result = await RunAsync(model, LlmProbeMode.D8);

        Assert.Equal(LlmProbeOutcome.Passed, result.Outcome);
    }

    [Fact]
    public async Task ShouldSendTheOversizedStateWithLongContextAndTheNeedleInside_ForD8()
    {
        var model = new StubDecisionModel { InputTokens = 100_500 };

        await RunAsync(model, LlmProbeMode.D8);

        var request = Assert.Single(model.Requests);
        Assert.True(request.LongContext);
        var state = request.State!.GetValue<string>();
        Assert.Equal(1, state.Split(DecisionProbeRunner.LongContextNeedle).Length - 1);
        Assert.True(state.IndexOf(DecisionProbeRunner.LongContextNeedle, StringComparison.Ordinal) > state.Length / 4);
    }

    [Theory]
    [InlineData(0.2, 100_500)]
    [InlineData(0.9, 30_000)]
    [InlineData(0.9, null)]
    public async Task ShouldFailD8_WhenTheNeedleIsMissedOrTheStateWasNotReadWhole(double noul, int? tokens)
    {
        var model = new StubDecisionModel { Noul = new NoulAnswer { Noul = noul }, InputTokens = tokens };

        var result = await RunAsync(model, LlmProbeMode.D8);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldFailD8_WhenTheLongStateIsRefused()
    {
        var model = new StubDecisionModel { Failure = Refusal(400, false, "over the long context limit") };

        var result = await RunAsync(model, LlmProbeMode.D8);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task ShouldMarkD8NotApplicable_WhenTheProviderServesNoLongContext()
    {
        var model = new StubDecisionModel();
        var runner = new DecisionProbeRunner(model) { SupportsLongContext = false };

        var result = Assert.Single(await runner.RunAsync([LlmProbeMode.D8], TestContext.Current.CancellationToken));

        Assert.Equal(LlmProbeOutcome.NotApplicable, result.Outcome);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void ShouldLeaveThePaidD8OutOfTheDefaultModes()
    {
        Assert.Contains(LlmProbeMode.D8, DecisionProbeRunner.SupportedModes);
        Assert.DoesNotContain(LlmProbeMode.D8, DecisionProbeRunner.DefaultModes);
        Assert.Equal(DecisionProbeRunner.SupportedModes.Count - 1, DecisionProbeRunner.DefaultModes.Count);
    }

    [Fact]
    public void ShouldKeepTheOversizedStateFreeOfAnyRefundRequest_WithoutTheNeedle()
    {
        var state = DecisionProbeRunner.OversizedState(needle: null);

        Assert.DoesNotContain("refund", state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("money", state, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRecordAFailure_RatherThanThrow_WhenTheModelThrowsUnexpectedly()
    {
        var model = new StubDecisionModel { Failure = new InvalidOperationException("boom") };

        var result = await RunAsync(model, LlmProbeMode.D1);

        Assert.Equal(LlmProbeOutcome.Failed, result.Outcome);
        Assert.Contains("boom", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldComputeTheOverlapOfTwoBoxes()
    {
        var a = new BoxCoordinates { Top = 0, Bottom = 0.5, Left = 0, Right = 0.5 };
        var b = new BoxCoordinates { Top = 0.25, Bottom = 0.75, Left = 0.25, Right = 0.75 };

        Assert.Equal(1.0, DecisionProbeRunner.IntersectionOverUnion(a, a), 6);
        Assert.Equal(0.0625 / 0.4375, DecisionProbeRunner.IntersectionOverUnion(a, b), 6);
    }

    [Fact]
    public void ShouldReadTheProvidersOwnKeyVariable_WhenTheProbeDefaultIsLeft()
    {
        var options = new LlmProbeCommandOptions { Provider = "typesafe" };

        var settings = LlmCommand.BuildDecisionOptions(options, DecisionProviderDescriptor.TypeSafe, "TYPESAFE_API_KEY");

        Assert.Equal("TYPESAFE_API_KEY", settings.ApiKeyEnvVar);
        Assert.Equal(DecisionProviderDefaults.TypeSafeModel, settings.Model);
        Assert.Equal(new Uri(DecisionProviderDefaults.TypeSafeBaseUrl), settings.BaseUrl);
        Assert.Null(settings.ApiKey);
        Assert.Equal(EnvironmentVariableNames.LlmCommandApiKey, options.ApiKeyEnv);
    }

    [Fact]
    public void ShouldCarryTheModelBaseUrlAndTimeout_WhenTheOptionsAreSet()
    {
        var options = new LlmProbeCommandOptions
        {
            Provider = "typesafe",
            Model = "jev-preview",
            BaseUrl = "https://eu.example.test/v1",
            TimeoutSeconds = 42,
        };

        var settings = LlmCommand.BuildDecisionOptions(options, DecisionProviderDescriptor.TypeSafe, "MY_KEY");

        Assert.Equal("jev-preview", settings.Model);
        Assert.Equal("eu.example.test", settings.BaseUrl.Host);
        Assert.Equal(42, settings.TimeoutSeconds);
        Assert.Equal("MY_KEY", settings.ApiKeyEnvVar);
    }
}
