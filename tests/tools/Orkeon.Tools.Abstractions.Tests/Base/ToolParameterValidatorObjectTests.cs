using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Abstractions.Tests.Base;

/// <summary>Characterisation: optional, object and unknown-type parameters under validation.</summary>
public class ToolParameterValidatorObjectTests
{
    private static ToolSchema SchemaOf(string type) =>
        new("t", "t", new Dictionary<string, ParameterSchema> { ["p"] = new(type, "p", false) });

    [Fact]
    [Trait("BR", "CH-05")]
    public void An_absent_or_null_optional_parameter_is_ignored()
    {
        var schema = SchemaOf("object");

        Assert.True(ToolParameterValidator.ValidateParameters([], schema).IsValid);
        Assert.True(ToolParameterValidator.ValidateParameters(new() { ["p"] = null }, schema).IsValid);
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public void An_object_passes_for_object()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "k": "v" }""");
        var schema = SchemaOf("object");

        Assert.True(ToolParameterValidator.IsValidType(document.RootElement.Clone(), "object"));
        Assert.True(ToolParameterValidator.IsValidType(new Dictionary<string, object> { ["k"] = "v" }, "object"));
        Assert.True(ToolParameterValidator.ValidateParameters(
            new() { ["p"] = new Dictionary<string, object> { ["k"] = "v" } }, schema).IsValid);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void An_expando_object_passes_for_an_object_parameter()
    {
        // A JS object from a .ork.ts script reaches the validator as an ExpandoObject, which
        // implements IDictionary<string, object?> only.
        var expando = new System.Dynamic.ExpandoObject();
        ((IDictionary<string, object?>)expando)["k"] = "v";

        Assert.True(ToolParameterValidator.IsValidType(expando, "object"));
        Assert.True(ToolParameterValidator.ValidateParameters(
            new() { ["p"] = expando }, SchemaOf("object")).IsValid);
        Assert.True(ToolParameterValidator.ValidateParameters(
            new() { ["p"] = expando }, MapSchema()).IsValid);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void An_absent_type_accepts_any_value()
    {
        var schema = SchemaOf(null!);
        using var document = System.Text.Json.JsonDocument.Parse("""{ "k": "v" }""");

        object[] values =
        [
            "text",
            42,
            true,
            new Dictionary<string, object> { ["k"] = "v" },
            document.RootElement.Clone(),
            new object[] { 1, 2 },
        ];

        foreach (var value in values)
        {
            Assert.True(ToolParameterValidator.ValidateParameters(new() { ["p"] = value }, schema).IsValid);
        }
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public void An_unknown_type_passes()
    {
        var schema = SchemaOf("custom");

        Assert.True(ToolParameterValidator.IsValidType("text", "custom"));
        Assert.True(ToolParameterValidator.IsValidType(42, "custom"));
        Assert.True(ToolParameterValidator.ValidateParameters(new() { ["p"] = 3.5 }, schema).IsValid);
    }

    private static ToolSchema MapSchema(string valueType = "string") =>
        new("t", "t", new Dictionary<string, ParameterSchema>
        {
            ["p"] = new("object", "p", false, Values: new ParameterSchema(valueType, "v", false))
        });

    private static ValidationResult Validate(string json, string valueType = "string")
    {
        using var document = JsonDocument.Parse(json);
        return ToolParameterValidator.ValidateParameters(
            new() { ["p"] = document.RootElement.Clone() }, MapSchema(valueType));
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public void A_key_value_array_passes_for_a_dictionary_parameter()
    {
        Assert.True(Validate("""[ { "key": "a", "value": "1" }, { "key": "b", "value": "2" } ]""").IsValid);
        Assert.True(Validate("""[]""").IsValid);
        Assert.True(Validate("""{ "a": "1" }""").IsValid);
    }

    [Theory]
    [InlineData("""[ { "name": "a", "value": "1" } ]""")]
    [InlineData("""[ "a", "b" ]""")]
    [InlineData("""[ { "key": 1, "value": "1" } ]""")]
    [Trait("BR", "CH-05")]
    public void Any_other_array_is_refused_for_a_dictionary_parameter(string json)
    {
        var result = Validate(json);

        Assert.False(result.IsValid);
        Assert.Contains("'p'", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public void A_repeated_key_is_refused()
    {
        var result = Validate("""[ { "key": "dup", "value": "1" }, { "key": "dup", "value": "2" } ]""");

        Assert.False(result.IsValid);
        Assert.Contains("dup", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public void A_value_of_the_wrong_type_is_refused_for_a_typed_dictionary()
    {
        Assert.False(Validate("""[ { "key": "a", "value": "not a number" } ]""", "integer").IsValid);
        Assert.False(Validate("""{ "a": "not a number" }""", "integer").IsValid);
        Assert.True(Validate("""[ { "key": "a", "value": 3 } ]""", "integer").IsValid);
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public async System.Threading.Tasks.Task An_empty_array_is_read_as_an_empty_dictionary()
    {
        using var tool = new LabelsTool();
        using var document = JsonDocument.Parse("[]");

        var response = await tool.CallAsync(
            new ToolCallRequest("labels", new Dictionary<string, object?> { ["labels"] = document.RootElement.Clone() }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.NotNull(tool.Received);
        Assert.Empty(tool.Received);
    }

    public sealed record LabelsRequest
    {
        [FieldSchema(Description = "labels")]
        public ImmutableDictionary<string, string>? Labels { get; init; }
    }

    public sealed record LabelsResponse
    {
        public bool Ok { get; init; }
    }

    private sealed class LabelsTool : ToolBase<LabelsRequest, LabelsResponse>
    {
        public ImmutableDictionary<string, string>? Received { get; private set; }

        public override string Name => "labels";
        public override string Description => "records its labels";

        protected override System.Threading.Tasks.Task<LabelsResponse> ExecuteTypedAsync(
            LabelsRequest request, CancellationToken cancellationToken)
        {
            Received = request.Labels;
            return System.Threading.Tasks.Task.FromResult(new LabelsResponse { Ok = true });
        }
    }
}
