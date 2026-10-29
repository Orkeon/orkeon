using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.Decision.DTOs;

namespace Orkeon.Application.Tests.Decision;

/// <summary>
/// The decision DTOs are the wire format of the decision API: a renamed field or a lost
/// discriminator is a 422 from the provider, or an answer read as null.
/// </summary>
public sealed class DecisionDtoJsonTests
{
    [Fact]
    public void Request_SerializesEveryQuestionTypeWithItsDiscriminator()
    {
        var request = new DecisionRequest
        {
            Model = "jev-latest",
            State = JsonValue.Create("I was charged twice."),
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty
                .Add("team", new ChoiceQuestion
                {
                    Instructions = JsonValue.Create("Which team handles this"),
                    Criteria = ImmutableDictionary<string, JsonNode?>.Empty.Add("billing", JsonValue.Create("Payments")),
                })
                .Add("urgency", new ScoreQuestion { Criteria = [JsonValue.Create("low")!, JsonValue.Create("high")!] })
                .Add("spam", new NoulQuestion { Criteria = new NoulCriteria { True = JsonValue.Create("Advertising") } }),
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(request))!;

        Assert.Equal("jev-latest", (string?)json["model"]);
        Assert.Equal("I was charged twice.", (string?)json["state"]);
        Assert.Equal("choice", (string?)json["questions"]!["team"]!["type"]);
        Assert.Equal("Payments", (string?)json["questions"]!["team"]!["criteria"]!["billing"]);
        Assert.Equal("score", (string?)json["questions"]!["urgency"]!["type"]);
        Assert.Equal(2, json["questions"]!["urgency"]!["criteria"]!.AsArray().Count);
        Assert.Equal("noul", (string?)json["questions"]!["spam"]!["type"]);
        Assert.Equal("Advertising", (string?)json["questions"]!["spam"]!["criteria"]!["true"]);
        Assert.Null(json["questions"]!["spam"]!["criteria"]!["false"]);
        Assert.Null(json["assets"]);
        Assert.Null(json["long_context"]);
        Assert.Null(json["LongContext"]);
    }

    [Fact]
    public void Request_SerializesImagesUnderAssets()
    {
        var request = new DecisionRequest
        {
            State = JsonValue.Create("Find the cat in img-1")!,
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add("cat", new BoxQuestion()),
            Assets = ImmutableDictionary<string, DecisionImage>.Empty
                .Add("img-1", new DecisionImage { Format = "png", DataBase64 = "iVBORw0KGgo=" }),
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(request))!;

        Assert.Equal("image", (string?)json["assets"]!["img-1"]!["type"]);
        Assert.Equal("png", (string?)json["assets"]!["img-1"]!["format"]);
        Assert.Equal("iVBORw0KGgo=", (string?)json["assets"]!["img-1"]!["data_base64"]);
        Assert.True(request.UsesImages);
    }

    [Fact]
    public void Response_DeserializesEveryAnswerType()
    {
        const string body = """
            {
              "model": "jev-1.14.0",
              "answers": {
                "team": { "type": "choice", "choice": "billing", "confidence": 0.9, "probabilities": { "billing": 0.95, "technical": 0.05 } },
                "urgency": { "type": "score", "score": 1.7, "confidence": 0.8, "legend": { "0": "low", "1": "mid", "2": "high" }, "probabilities": { "0": 0.1, "1": 0.1, "2": 0.8 } },
                "spam": { "type": "noul", "noul": 0.02 },
                "cat": { "type": "box", "box": { "top": 0.2, "bottom": 0.66, "left": 0.19, "right": 0.68 }, "confidence": null }
              },
              "usage": { "input_tokens": 120, "output_tokens": 12 }
            }
            """;

        var response = JsonSerializer.Deserialize<DecisionResponse>(body)!;

        Assert.Equal("jev-1.14.0", response.Model);
        Assert.Equal("billing", response.Get<ChoiceAnswer>("team").Choice);
        Assert.Equal(0.95, response.Get<ChoiceAnswer>("team").Probabilities["billing"]);
        Assert.Equal(1.7, response.Get<ScoreAnswer>("urgency").Score);
        Assert.Equal("high", (string?)response.Get<ScoreAnswer>("urgency").Legend["2"]);
        Assert.Equal(0.02, response.Get<NoulAnswer>("spam").Noul);
        Assert.Equal(0.66, response.Get<BoxAnswer>("cat").Box.Bottom);
        Assert.Null(response.Get<BoxAnswer>("cat").Confidence);
        Assert.Equal(120, response.Usage.InputTokens);
    }

    [Fact]
    public void ShouldReadNullTokenCounts_WhenTheUsageCarriesNone()
    {
        const string body = """
            {"model":"jev-1.14.0","answers":{"spam":{"type":"noul","noul":0.02}},"usage":{"input_tokens":null,"output_tokens":null}}
            """;

        var response = JsonSerializer.Deserialize<DecisionResponse>(body)!;

        Assert.Null(response.Usage.InputTokens);
        Assert.Null(response.Usage.OutputTokens);
    }

    [Fact]
    public void Get_Throws_WhenTheAnswerIsMissingOrOfAnotherType()
    {
        var response = new DecisionResponse
        {
            Model = "m",
            Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add("spam", new NoulAnswer { Noul = 0.5 }),
        };

        Assert.Throws<KeyNotFoundException>(() => response.Get<NoulAnswer>("other"));
        var ex = Assert.Throws<InvalidOperationException>(() => response.Get<ChoiceAnswer>("spam"));
        Assert.Contains("NoulAnswer", ex.Message, StringComparison.Ordinal);
    }
}
