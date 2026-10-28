using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;

namespace Orkeon.Application.Tests.Decision;

public sealed class DecisionRequestValidatorTests
{
    private readonly DecisionRequestValidator _validator = new();

    private static DecisionRequest With(params (string Name, DecisionQuestion Question)[] questions) => new()
    {
        State = JsonValue.Create("state")!,
        Questions = questions.ToImmutableDictionary(q => q.Name, q => q.Question),
    };

    private static ImmutableDictionary<string, JsonNode?> Options(int count) =>
        Enumerable.Range(0, count).ToImmutableDictionary(i => $"o{i}", _ => (JsonNode?)null);

    [Fact]
    public void Validate_Accepts_AWellFormedRequest()
    {
        var result = _validator.Validate(With(
            ("team", new ChoiceQuestion { Criteria = Options(2) }),
            ("spam", new NoulQuestion())));

        Assert.True(result.IsValid, result.GetErrorMessage());
    }

    [Fact]
    public void Validate_Refuses_ARequestWithoutQuestion()
    {
        var result = _validator.Validate(With());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one question", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Validate_Refuses_AChoiceOutOfBounds(int count)
    {
        var result = _validator.Validate(With(("team", new ChoiceQuestion { Criteria = Options(count) })));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'team'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Validate_Refuses_AScoreOutOfBounds(int count)
    {
        var levels = Enumerable.Range(0, count).Select(i => (JsonNode)JsonValue.Create($"level {i}")!).ToImmutableList();

        var result = _validator.Validate(With(("urgency", new ScoreQuestion { Criteria = levels })));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'urgency'", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public void ShouldReadLimitsFromConstants_WhenValidatingRequest()
    {
        var maxOptions = Orkeon.Constants.Llm.DecisionLimits.MaxChoiceOptions;
        var maxLevels = Orkeon.Constants.Llm.DecisionLimits.MaxScoreLevels;
        ImmutableList<JsonNode> Levels(int count) =>
            Enumerable.Range(0, count).Select(i => (JsonNode)JsonValue.Create($"level {i}")!).ToImmutableList();

        Assert.True(_validator.Validate(With(("c", new ChoiceQuestion { Criteria = Options(maxOptions) }))).IsValid);
        Assert.False(_validator.Validate(With(("c", new ChoiceQuestion { Criteria = Options(maxOptions + 1) }))).IsValid);
        Assert.True(_validator.Validate(With(("s", new ScoreQuestion { Criteria = Levels(maxLevels) }))).IsValid);
        Assert.False(_validator.Validate(With(("s", new ScoreQuestion { Criteria = Levels(maxLevels + 1) }))).IsValid);
    }

    [Fact]
    public void Validate_Refuses_ABoxWithoutImage()
    {
        var result = _validator.Validate(With(("cat", new BoxQuestion())));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'cat'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Refuses_AnImageOfAnUnknownFormat()
    {
        var request = With(("cat", new BoxQuestion())) with
        {
            Assets = ImmutableDictionary<string, DecisionImage>.Empty
                .Add("img", new DecisionImage { Format = "gif", DataBase64 = "R0lGOD==" }),
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'gif'", StringComparison.Ordinal));
    }
}
