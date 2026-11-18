using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs.Replay;

namespace Orkeon.Infrastructure.Tests.LLMs.Replay;

/// <summary>
/// The key of a call with tools: the members a tool schema gained after v1.2.0 weigh nothing while they hold their
/// default, so a cassette recorded by v1.2.0 still answers; one that is set changes the key.
/// </summary>
public sealed class LlmCallKeyTests
{
    private static readonly LlmMessage[] Messages = [new LlmMessage { Role = "user", Content = "ping" }];

    private static List<ToolSchema> Tools(Func<ParameterSchema, ParameterSchema>? count = null, bool allowsAdditionalParameters = true)
    {
        var limit = new ParameterSchema("integer", "Most results", false, Default: 10);
        return
        [
            new ToolSchema("lookup", "Finds", new Dictionary<string, ParameterSchema>
            {
                ["query"] = new("string", "Text", true),
                ["limit"] = count is null ? limit : count(limit),
                ["tags"] = new("array", "Tags", false, ItemsType: "object")
                {
                    ItemProperties = new Dictionary<string, ParameterSchema> { ["id"] = new("integer", "Id", true) },
                },
                ["labels"] = new("object", "Labels", false, Values: new ParameterSchema("string", "value", false)),
            })
            {
                AllowsAdditionalParameters = allowsAdditionalParameters,
            },
        ];
    }

    /// <summary>One parameter as v1.2.0 serialized it: the members of v1.2.0's ParameterSchema, in their order.</summary>
    private static string V120Parameter(
        string type, string description, bool required, string defaultValue = "null", string itemsType = "null",
        string values = "null", string itemProperties = "null") =>
        $$"""{"Type":"{{type}}","Description":"{{description}}","Required":{{(required ? "true" : "false")}},"Default":{{defaultValue}},"Enum":null,"Format":null,"ItemsType":{{itemsType}},"ItemsFormat":null,"EnumMap":null,"Example":null,"Values":{{values}},"ItemProperties":{{itemProperties}},"IsMap":{{(values == "null" ? "false" : "true")}}}""";

    [Fact]
    public void ShouldReadTheJsonOfV120_WhenNoAddedMemberIsSet()
    {
        // v1.2.0's ToolSchema serialized Name, Description, Parameters, Returns and Types, and nothing else.
        var parameters =
            "{\"query\":" + V120Parameter("string", "Text", true)
            + ",\"limit\":" + V120Parameter("integer", "Most results", false, defaultValue: "10")
            + ",\"tags\":" + V120Parameter("array", "Tags", false, itemsType: "\"object\"",
                itemProperties: "{\"id\":" + V120Parameter("integer", "Id", true) + "}")
            + ",\"labels\":" + V120Parameter("object", "Labels", false, values: V120Parameter("string", "value", false))
            + "}";
        var expected = "[{\"Name\":\"lookup\",\"Description\":\"Finds\",\"Parameters\":" + parameters + ",\"Returns\":null,\"Types\":null}]";

        Assert.Equal(expected, LlmCallKey.ToolsKeyJson(Tools()));
    }

    [Fact]
    public void ShouldKeepTheKey_WhenTheToolsAreBuiltAgainTheSameWay()
    {
        var config = LlmConfig.OnProfile() with { Tools = Tools() };

        Assert.Equal(LlmCallKey.Of("p", Messages, config), LlmCallKey.Of("p", Messages, LlmConfig.OnProfile() with { Tools = Tools() }));
    }

    [Fact]
    public void ShouldChangeTheKey_WhenAnAddedMemberIsSet()
    {
        var plain = LlmCallKey.Of("p", Messages, LlmConfig.OnProfile() with { Tools = Tools() });

        Assert.NotEqual(plain, LlmCallKey.Of("p", Messages, LlmConfig.OnProfile() with { Tools = Tools(p => p with { Minimum = 1 }) }));
        Assert.NotEqual(plain, LlmCallKey.Of("p", Messages, LlmConfig.OnProfile() with { Tools = Tools(p => p with { AllowsAdditionalProperties = false }) }));
        Assert.NotEqual(plain, LlmCallKey.Of("p", Messages, LlmConfig.OnProfile() with { Tools = Tools(allowsAdditionalParameters: false) }));
        Assert.Contains("\"Minimum\":1", LlmCallKey.ToolsKeyJson(Tools(p => p with { Minimum = 1 })));
    }

    [Fact]
    public void ShouldTrimTheNestedSchemas_AndKeepTheirSetMembers()
    {
        var tools = Tools(p => p with
        {
            Items = new ParameterSchema("string", "Item", false),
            AnyOf = [new ParameterSchema("integer", "Int", false) { Maximum = 3 }],
        });

        var json = LlmCallKey.ToolsKeyJson(tools);

        Assert.Contains("\"Items\":{\"Type\":\"string\"", json);
        Assert.Contains("\"Maximum\":3", json);
        // The nested schemas lose their unset added members as the top-level ones do: one Maximum, the one set.
        Assert.Equal(json.IndexOf("\"Maximum\"", StringComparison.Ordinal), json.LastIndexOf("\"Maximum\"", StringComparison.Ordinal));
        Assert.DoesNotContain("\"MinLength\"", json);
        Assert.DoesNotContain("\"AllowsAdditionalProperties\"", json);
    }
}
