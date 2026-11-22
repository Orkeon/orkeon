using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Issue #459: the Anthropic provider owns its JSON Schema dialect. It closes every object, moves the
/// constraints the API does not take into the node's description, and refuses before any HTTP call
/// what it cannot rewrite without changing the schema's meaning, naming the keyword and its path.
/// </summary>
public sealed class AnthropicSchemaDialectPayloadTests : IDisposable
{
    private const string MinimalSuccessResponse =
        """{"content":[{"type":"text","text":"{\"colour\":\"red\"}"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly LlmConfig _config;

    public AnthropicSchemaDialectPayloadTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with
        {
            BaseUrl = new Uri("https://api.anthropic.com")
        };
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }

    private AnthropicLlmProvider CreateProvider() =>
        new(_config, _httpClientFactory, Policy.NoOpAsync<HttpResponseMessage>());

    private LlmConfig WithSchema(string schema) =>
        _config with { ResponseFormat = LlmResponseFormat.JsonSchema("extract", schema, strict: false) };

    private async Task<JsonElement> SendAndReadSchemaAsync(string schema)
    {
        using var provider = CreateProvider();
        var response = await provider.GenerateAsync("Name one colour.", WithSchema(schema), TestContext.Current.CancellationToken);
        Assert.Null(response.Error);
        Assert.NotNull(_handler.LastRequestBody);
        using var doc = JsonDocument.Parse(_handler.LastRequestBody!);
        return doc.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("schema").Clone();
    }

    [Fact]
    public async Task ShouldCloseTheObject_WhenTheIssueSchemaLeavesItOpen()
    {
        var schema = await SendAndReadSchemaAsync(
            """{"type":"object","properties":{"colour":{"type":"string"}},"required":["colour"]}""");

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("colour").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ShouldCloseEveryObject_WhenAnObjectSitsInAnArrayInAnObject()
    {
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{
              "items":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"}}}},
              "choice":{"anyOf":[{"type":"object","properties":{"a":{"type":"string"}}},{"type":"null"}]},
              "both":{"allOf":[{"$ref":"#/$defs/base"}]}
            },
            "$defs":{"base":{"type":"object","properties":{"id":{"type":"integer"}}}},
            "definitions":{"other":{"type":["object","null"],"properties":{"x":{"type":"string"}}}}}
            """);

        var properties = schema.GetProperty("properties");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(properties.GetProperty("items").GetProperty("items").GetProperty("additionalProperties").GetBoolean());
        Assert.False(properties.GetProperty("choice").GetProperty("anyOf")[0].GetProperty("additionalProperties").GetBoolean());
        Assert.False(properties.GetProperty("choice").GetProperty("anyOf")[1].TryGetProperty("additionalProperties", out _));
        Assert.False(schema.GetProperty("$defs").GetProperty("base").GetProperty("additionalProperties").GetBoolean());
        Assert.False(schema.GetProperty("definitions").GetProperty("other").GetProperty("additionalProperties").GetBoolean());
        Assert.False(properties.GetProperty("items").TryGetProperty("additionalProperties", out _));
    }

    [Fact]
    public async Task ShouldLeaveAnExplicitFalse_AsWritten()
    {
        var schema = await SendAndReadSchemaAsync(
            """{"type":"object","additionalProperties":false,"properties":{"a":{"type":"string"}}}""");

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["type", "additionalProperties", "properties"],
            schema.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task ShouldMoveTheConstraintsTheApiDoesNotTake_IntoTheDescription()
    {
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{
              "age":{"type":"integer","description":"Age in years","minimum":18,"maximum":120},
              "code":{"type":"string","minLength":2,"maxLength":5,"pattern":"^[A-Z]+$"},
              "tags":{"type":"array","items":{"type":"string"},"minItems":2,"maxItems":4},
              "one":{"type":"array","items":{"type":"string"},"minItems":1},
              "when":{"type":"string","format":"date"},
              "site":{"type":"string","format":"iri"}
            }}
            """);

        var properties = schema.GetProperty("properties");
        var age = properties.GetProperty("age");
        Assert.False(age.TryGetProperty("minimum", out _));
        Assert.False(age.TryGetProperty("maximum", out _));
        Assert.Equal("Age in years\n\n{minimum: 18, maximum: 120}", age.GetProperty("description").GetString());

        // The API takes minLength, maxLength and pattern: they are sent as written.
        var code = properties.GetProperty("code");
        Assert.Equal(2, code.GetProperty("minLength").GetInt32());
        Assert.Equal(5, code.GetProperty("maxLength").GetInt32());
        Assert.Equal("^[A-Z]+$", code.GetProperty("pattern").GetString());
        Assert.False(code.TryGetProperty("description", out _));

        var tags = properties.GetProperty("tags");
        Assert.False(tags.TryGetProperty("minItems", out _));
        Assert.Equal("{minItems: 2, maxItems: 4}", tags.GetProperty("description").GetString());

        Assert.Equal(1, properties.GetProperty("one").GetProperty("minItems").GetInt32());
        Assert.Equal("date", properties.GetProperty("when").GetProperty("format").GetString());

        var site = properties.GetProperty("site");
        Assert.False(site.TryGetProperty("format", out _));
        Assert.Equal("{format: iri}", site.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ShouldLeaveTheCallersFormatUntouched_WhenItSendsTheAdaptedCopy()
    {
        const string original = """{"type":"object","properties":{"n":{"type":"integer","minimum":1}}}""";
        var config = WithSchema(original);
        using var provider = CreateProvider();

        await provider.GenerateAsync("go", config, TestContext.Current.CancellationToken);

        Assert.Equal(original, config.ResponseFormat!.Schema!.Schema);
    }

    [Fact]
    public async Task ShouldSendTheSchema_WhenTwoPropertiesShareOneDefinition()
    {
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{"from":{"$ref":"#/$defs/point"},"to":{"$ref":"#/$defs/point"}},
             "$defs":{"point":{"type":"object","properties":{"x":{"type":"number"}}}}}
            """);

        Assert.False(schema.GetProperty("$defs").GetProperty("point").GetProperty("additionalProperties").GetBoolean());
    }

    public static TheoryData<string, string> RefusedSchemas => new()
    {
        { """{"type":"object","properties":{"choice":{"oneOf":[{"type":"string"},{"type":"integer"}]}}}""", "`oneOf` at `#/properties/choice`" },
        { """{"type":"object","properties":{"n":{"not":{"type":"string"}}}}""", "`not` at `#/properties/n`" },
        { """{"type":"object","additionalProperties":true}""", "`additionalProperties` at `#`" },
        { """{"type":"object","properties":{"map":{"type":"object","additionalProperties":{"type":"string"}}}}""", "`additionalProperties` at `#/properties/map`" },
        { """{"type":"object","properties":{"a":{"$ref":"https://example.com/a.json"}}}""", "external `$ref` at `#/properties/a`" },
        { """{"type":"object","properties":{"child":{"$ref":"#"}}}""", "recursive `$ref` at `#/properties/child`" },
        { """{"type":"object","properties":{"root":{"$ref":"#/$defs/node"}},"$defs":{"node":{"type":"object","properties":{"next":{"$ref":"#/$defs/node"}}}}}""", "recursive `$ref` at `#/$defs/node/properties/next`" },
        // A free-form object: closing it would leave the model nothing to answer but {}.
        { """{"type":"object","properties":{"metadata":{"type":"object"}}}""", "the object at `#/properties/metadata` declares no `properties`" },
        { """{"type":"object","properties":{"bag":{"type":"object","properties":{}}}}""", "the object at `#/properties/bag` declares no `properties`" },
        { """{"type":"object","properties":{"bag":{"type":["object","null"]}}}""", "the object at `#/properties/bag` declares no `properties`" },
        { """{"type":"object"}""", "the object at `#` declares no `properties`" },
        // Keywords the API refuses on an object node.
        { """{"type":"object","properties":{"a":{"type":"string"}},"if":{"properties":{"a":{"const":"x"}}},"then":{"required":["a"]}}""", "`if` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"then":{"required":["a"]}}""", "`then` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"else":{"required":["a"]}}""", "`else` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"patternProperties":{"^x":{"type":"object"}}}""", "`patternProperties` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"dependentSchemas":{"a":{"type":"object"}}}""", "`dependentSchemas` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"dependencies":{"a":{"type":"object"}}}""", "`dependencies` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"contains":{"const":"x"}}""", "`contains` at `#` is not supported on an object node" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"unevaluatedItems":false}""", "`unevaluatedItems` at `#` is not supported on an object node" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"additionalItems":false}""", "`additionalItems` at `#` is not supported on an object node" },
        // Keywords the API refuses on an array node, and an items that is not one schema.
        { """{"type":"object","properties":{"l":{"type":"array","items":{"type":"string"},"contains":{"type":"object"}}}}""", "`contains` at `#/properties/l` is not supported on an array node" },
        { """{"type":"object","properties":{"l":{"type":"array","prefixItems":[{"type":"string"}]}}}""", "`prefixItems` at `#/properties/l` is not supported on an array node" },
        { """{"type":"object","properties":{"l":{"type":"array","items":[{"type":"string"}]}}}""", "`items` at `#/properties/l` must be one schema object" },
        { """{"type":"object","properties":{"l":{"type":"array","items":true}}}""", "`items` at `#/properties/l` must be one schema object" },
        // if/then/else on an anyOf node.
        { """{"type":"object","properties":{"c":{"anyOf":[{"type":"string"}],"if":{"const":"a"}}}}""", "`if` at `#/properties/c` is not supported on an `anyOf` node" },
        // A $ref with a constraint beside it.
        { """{"type":"object","properties":{"p":{"$ref":"#/$defs/p","type":"string"}},"$defs":{"p":{"type":"string"}}}""", "`$ref` at `#/properties/p` cannot be combined with `type`" },
        // Schemas that accept any value, or name no type.
        { """{"type":"object","properties":{"any":{}}}""", "the empty schema `{}` at `#/properties/any`" },
        { """{"type":"object","properties":{"d":{"description":"x"}}}""", "the schema at `#/properties/d` names no `type`" },
        { """{"type":"object","properties":{"t":true}}""", "the schema at `#/properties/t` must be a JSON object" },
        { "true", "the root must be a JSON object" },
        { "{}", "the empty schema `{}` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"propertyNames":{"pattern":"^a"}}""", "`propertyNames` at `#`" },
        { """{"type":"object","properties":{"a":{"type":"string"}},"unevaluatedProperties":false}""", "`unevaluatedProperties` at `#`" },
        // A description that is not a string is not a valid schema; the moved constraints would overwrite it.
        { """{"type":"object","properties":{"n":{"type":"integer","minimum":1,"description":{"en":"count"}}}}""", "`description` at `#/properties/n` must be a string" },
        { """{"type":"object","properties":{"n":{"type":"integer","description":null}}}""", "`description` at `#/properties/n` must be a string" },
    };

    [Fact]
    public async Task ShouldSendAnEmptyObject_WhenTheCallerClosesItThemselves()
    {
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{
              "none":{"type":"object","properties":{},"additionalProperties":false},
              "bare":{"type":"object","additionalProperties":false}
            }}
            """);

        var properties = schema.GetProperty("properties");
        Assert.False(properties.GetProperty("none").GetProperty("additionalProperties").GetBoolean());
        Assert.False(properties.GetProperty("bare").GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task ShouldMoveTheNumericAndArrayConstraints_IntoTheDescription()
    {
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{
              "price":{"type":"number","multipleOf":0.5,"exclusiveMinimum":0,"exclusiveMaximum":1000},
              "set":{"type":"array","items":{"type":"string"},"uniqueItems":true},
              "list":{"type":"array","items":{"type":"string"},"uniqueItems":false},
              "huge":{"type":"array","items":{"type":"string"},"minItems":1e40},
              "beyond":{"type":"array","items":{"type":"string"},"minItems":1e400}
            }}
            """);

        var properties = schema.GetProperty("properties");
        var price = properties.GetProperty("price");
        Assert.False(price.TryGetProperty("multipleOf", out _));
        Assert.False(price.TryGetProperty("exclusiveMinimum", out _));
        Assert.Equal("{multipleOf: 0.5, exclusiveMinimum: 0, exclusiveMaximum: 1000}", price.GetProperty("description").GetString());

        var set = properties.GetProperty("set");
        Assert.False(set.TryGetProperty("uniqueItems", out _));
        Assert.Equal("{uniqueItems: true}", set.GetProperty("description").GetString());

        var list = properties.GetProperty("list");
        Assert.False(list.TryGetProperty("uniqueItems", out _));
        Assert.False(list.TryGetProperty("description", out _));

        Assert.Equal("{minItems: 1e40}", properties.GetProperty("huge").GetProperty("description").GetString());
        Assert.Equal("{minItems: 1e400}", properties.GetProperty("beyond").GetProperty("description").GetString());
    }

    [Fact]
    public async Task ShouldCloseTheObjects_InANullableObjectAndAnAllOf()
    {
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{
              "maybe":{"type":["object","null"],"properties":{"a":{"type":"string"}}},
              "both":{"allOf":[{"type":"object","properties":{"id":{"type":"integer"}}}]}
            }}
            """);

        var properties = schema.GetProperty("properties");
        Assert.False(properties.GetProperty("maybe").GetProperty("additionalProperties").GetBoolean());
        Assert.False(properties.GetProperty("both").GetProperty("allOf")[0].GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task ShouldWalkARootThatIsNotAnObject_AndCloseTheObjectsBelowIt()
    {
        var schema = await SendAndReadSchemaAsync(
            """{"type":"array","items":{"type":"object","properties":{"n":{"type":"integer","maximum":3}}}}""");

        Assert.False(schema.TryGetProperty("additionalProperties", out _));
        var item = schema.GetProperty("items");
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("{maximum: 3}", item.GetProperty("properties").GetProperty("n").GetProperty("description").GetString());
    }

    [Fact]
    public async Task ShouldSendTheKeywordsTheApiTakesOffAnObject_AsWritten()
    {
        // Measured: a string, integer or allOf node takes if/then/else (without enforcing them), an
        // array node takes unevaluatedItems and additionalItems, and a $ref takes annotations beside it.
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{
              "word":{"type":"string","if":{"const":"a"},"then":{"maxLength":3},"else":{"type":"object"}},
              "n":{"type":"integer","if":{"const":1},"then":{"const":1}},
              "both":{"allOf":[{"type":"string"}],"if":{"const":"a"},"then":{"const":"a"}},
              "list":{"type":"array","items":{"type":"string"},"unevaluatedItems":false,"additionalItems":{"type":"object"}},
              "point":{"$ref":"#/$defs/point","description":"Where","title":"Point","default":{"x":0}}
            },
            "$defs":{"point":{"type":"object","properties":{"x":{"type":"number"}}}}}
            """);

        var properties = schema.GetProperty("properties");
        var word = properties.GetProperty("word");
        Assert.Equal("a", word.GetProperty("if").GetProperty("const").GetString());
        // The API walks no sub-schema under if/then/else: none of them is closed.
        Assert.False(word.GetProperty("else").TryGetProperty("additionalProperties", out _));
        Assert.Equal(1, properties.GetProperty("n").GetProperty("then").GetProperty("const").GetInt32());
        Assert.True(properties.GetProperty("both").TryGetProperty("if", out _));
        var list = properties.GetProperty("list");
        Assert.False(list.GetProperty("unevaluatedItems").GetBoolean());
        Assert.False(list.GetProperty("additionalItems").TryGetProperty("additionalProperties", out _));
        Assert.Equal(
            ["$ref", "description", "title", "default"],
            properties.GetProperty("point").EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task ShouldMoveAConstraintBesideARef_AndSendTheRef()
    {
        var schema = await SendAndReadSchemaAsync("""
            {"type":"object","properties":{"n":{"$ref":"#/$defs/n","minimum":1}},"$defs":{"n":{"type":"integer"}}}
            """);

        var n = schema.GetProperty("properties").GetProperty("n");
        Assert.Equal(["$ref", "description"], n.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("{minimum: 1}", n.GetProperty("description").GetString());
    }

    [Theory]
    [MemberData(nameof(RefusedSchemas))]
    public async Task ShouldRefuseBeforeTheCall_WhenTheSchemaCannotBeRewritten(string schema, string named)
    {
        using var provider = CreateProvider();

        var response = await provider.GenerateAsync("go", WithSchema(schema), TestContext.Current.CancellationToken);

        Assert.Equal(0, _handler.SendCallCount);
        Assert.NotNull(response.Error);
        Assert.Contains(named, response.Error, StringComparison.Ordinal);
        Assert.StartsWith("Anthropic cannot take the response schema 'extract': ", response.Error, StringComparison.Ordinal);
        Assert.Equal(AnthropicSchemaDialect.RefusedErrorType, response.ErrorType);
        Assert.Equal(LlmFailureKind.Refused, LlmFailureClassifier.Classify(response));
        Assert.False(LlmFailureClassifier.FailsOver(LlmFailureClassifier.Classify(response)));
    }

    [Fact]
    public async Task ShouldRefuseBeforeTheCall_OnChat()
    {
        using var provider = CreateProvider();

        var response = await provider.ChatAsync(
            [LlmMessage.User("go")],
            WithSchema("""{"type":"object","properties":{"c":{"oneOf":[{"type":"string"}]}}}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, _handler.SendCallCount);
        Assert.Contains("`oneOf` at `#/properties/c`", response.Error, StringComparison.Ordinal);
        Assert.Equal(AnthropicSchemaDialect.RefusedErrorType, response.ErrorType);
    }

    [Fact]
    public async Task ShouldRefuseBeforeTheCall_OnTheEventStream()
    {
        using var provider = CreateProvider();
        var events = new List<LlmStreamEvent>();

        await foreach (var ev in provider.ChatStreamingAsync(
            [LlmMessage.User("go")],
            WithSchema("""{"type":"object","properties":{"c":{"oneOf":[{"type":"string"}]}}}"""),
            TestContext.Current.CancellationToken))
        {
            events.Add(ev);
        }

        Assert.Equal(0, _handler.SendCallCount);
        var completed = Assert.Single(events);
        Assert.Equal(LlmStreamEventKind.Completed, completed.Kind);
        Assert.Contains("`oneOf` at `#/properties/c`", completed.FinalResponse!.Error, StringComparison.Ordinal);
        Assert.Equal(AnthropicSchemaDialect.RefusedErrorType, completed.FinalResponse.ErrorType);
    }

    [Fact]
    public async Task ShouldRefuseBeforeTheCall_OnTheTextStream()
    {
        using var provider = CreateProvider();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync(
                "go",
                WithSchema("""{"type":"object","properties":{"c":{"not":{"type":"string"}}}}"""),
                TestContext.Current.CancellationToken))
            {
                // Nothing to read: the refusal comes while the stream is opened.
            }
        });

        Assert.Equal(0, _handler.SendCallCount);
        Assert.Contains("`not` at `#/properties/c`", ex.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(LlmFailureKind.Refused, LlmFailureClassifier.Classify(ex));
    }

    [Fact]
    public async Task ShouldRefuseBeforeTheCall_WhenTheMessagesEndOnAnAssistantTurn()
    {
        using var provider = CreateProvider();

        var response = await provider.ChatAsync(
            [LlmMessage.User("Extract"), LlmMessage.Assistant("{\"colour\":")],
            WithSchema("""{"type":"object","properties":{"colour":{"type":"string"}}}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, _handler.SendCallCount);
        Assert.Contains("assistant turn", response.Error, StringComparison.Ordinal);
        Assert.Equal(AnthropicSchemaDialect.RefusedErrorType, response.ErrorType);
    }

    [Fact]
    public async Task ShouldSendTheAssistantTurn_WhenNoResponseFormatIsSet()
    {
        using var provider = CreateProvider();

        await provider.ChatAsync(
            [LlmMessage.User("Extract"), LlmMessage.Assistant("Sure")],
            _config,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, _handler.SendCallCount);
    }
}
