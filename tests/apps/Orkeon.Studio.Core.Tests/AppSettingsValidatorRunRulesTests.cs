using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-55: the check before saving judges <c>Llm</c> and each entry of <c>Llm:Profiles</c> as the
/// run reads them at start — an integer by <c>int.TryParse</c>, a temperature that must be a finite
/// number, an absolute http(s) address, a variable name, a key that is no <c>${NAME}</c> placeholder,
/// no entry named <c>default</c>. Each finding names its path; what Studio writes itself passes.
/// </summary>
public sealed class AppSettingsValidatorRunRulesTests
{
    /// <summary>Every field the run reads as an integer.</summary>
    public static TheoryData<string> IntegerFields() =>
    [
        "Llm:MaxTokens",
        "Llm:TimeoutSeconds",
        "Llm:StreamIdleSeconds",
        "Llm:MaxRetries",
        "RateLimiting:MaxConcurrentRequests",
        "RateLimiting:GlobalRequestsPerMinute",
        "RateLimiting:ProviderRequestsPerMinute",
        "RateLimiting:AgentRequestsPerMinute",
        "RateLimiting:QueueLimit",
        "LlmLogging:MaxBodyLengthChars",
        "Orkeon:Rag:Corrective:MaxIterations",
    ];

    private static AppSettingsValidator Validator() => new(new FakeDirectoryProbe());

    /// <summary>A document with the given JSON value at <paramref name="path"/>, beside a usable default.</summary>
    private static AppSettingsDocument With(string path, string json)
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" } }""");
        document.SetNode(path, System.Text.Json.Nodes.JsonNode.Parse(json));
        return document;
    }

    private static IReadOnlyList<ValidationMessage> Errors(AppSettingsDocument document, string? path = null) =>
        [.. Validator().Validate(document)
            .Where(message => message.Severity == ValidationSeverity.Error)
            .Where(message => path is null || message.Path == path)];

    [Theory]
    [MemberData(nameof(IntegerFields))]
    public void An_integer_field_refuses_what_the_run_does_not_read_as_an_integer(string path)
    {
        foreach (var json in new[] { "600.0", "0.5", "\"600.0\"", "1e3", "3000000000", "\"1e3\"" })
        {
            var error = Assert.Single(Errors(With(path, json), path));
            Assert.Equal(ValidationCodes.InvalidFieldType, error.Code);
            Assert.Contains("whole number", error.Text, StringComparison.Ordinal);
        }

        Assert.Empty(Errors(With(path, "600"), path));
        Assert.Empty(Errors(With(path, "\"600\""), path));
    }

    [Fact]
    public void The_temperature_refuses_a_number_that_is_not_finite()
    {
        foreach (var json in new[] { "\"NaN\"", "\"Infinity\"", "\"-Infinity\"", "1e400", "-1e400", "\"1e400\"" })
        {
            var error = Assert.Single(Errors(With("Llm:Temperature", json), "Llm:Temperature"));
            Assert.Equal(ValidationCodes.InvalidFieldType, error.Code);
            Assert.Contains("finite number", error.Text, StringComparison.Ordinal);
        }

        Assert.Empty(Errors(With("Llm:Temperature", "0.7"), "Llm:Temperature"));
        Assert.Empty(Errors(With("Llm:Temperature", "\"1\""), "Llm:Temperature"));
    }

    [Fact]
    public void A_reference_that_cannot_be_a_variable_name_is_refused_without_repeating_it()
    {
        var error = Assert.Single(Errors(With("Llm:ApiKeyEnvVar", "\"MY KEY\""), "Llm:ApiKeyEnvVar"));

        Assert.Equal(ValidationCodes.InvalidFieldType, error.Code);
        Assert.DoesNotContain("MY KEY", error.Text, StringComparison.Ordinal);
        Assert.Empty(Errors(With("Llm:ApiKeyEnvVar", "\"DEEPSEEK_API_KEY\""), "Llm:ApiKeyEnvVar"));
    }

    public static TheoryData<string, string> ProfileValuesTheRunRefuses() => new()
    {
        { "Temperature", "\"warm\"" },
        { "Temperature", "1e400" },
        { "MaxTokens", "600.0" },
        { "TimeoutSeconds", "\"600s\"" },
        { "StreamIdleSeconds", "\"45s\"" },
        { "MaxRetries", "1.5" },
        { "BaseUrl", "\"not a url\"" },
        { "BaseUrl", "\"localhost:8080/v1\"" },
        { "ApiKeyEnvVar", "\"MY KEY\"" },
        { "Grammar", "\"yes\"" },
        { "Thinking:Enabled", "1" },
        { "Model", "{ }" },
    };

    [Theory]
    [MemberData(nameof(ProfileValuesTheRunRefuses))]
    public void An_entry_of_the_profiles_is_judged_as_the_default_is(string key, string json)
    {
        var document = With("Llm:Profiles:local-gpu", """{ "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b" }""");
        document.SetNode($"Llm:Profiles:local-gpu:{key}", System.Text.Json.Nodes.JsonNode.Parse(json));

        var error = Assert.Single(Errors(document));

        Assert.Equal(ValidationCodes.InvalidFieldType, error.Code);
        Assert.Equal($"Llm:Profiles:local-gpu:{key}", error.Path);
    }

    [Fact]
    public void The_profiles_and_each_entry_must_be_objects()
    {
        Assert.Equal("Llm:Profiles", Assert.Single(Errors(With("Llm:Profiles", """[ { "Model": "x" } ]"""))).Path);
        Assert.Equal("Llm:Profiles:local", Assert.Single(Errors(With("Llm:Profiles:local", "\"qwen3\""))).Path);
    }

    [Fact]
    public void The_entries_studio_writes_carry_no_error()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" } }""");
        var set = ModelProfileSet.Empty
            .Upsert(new ModelProfile
            {
                Name = "DeepSeek", Provider = "DeepSeek", BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-v4-flash", KeyEnvName = "DEEPSEEK_API_KEY", TimeoutSeconds = 600,
                Temperature = 0.7, MaxTokens = 8192, ThinkingEnabled = true, ThinkingEffort = "high",
            })
            .Upsert(new ModelProfile { Name = "Local", Provider = "Ollama", BaseUrl = "http://localhost:11434", Model = "qwen3" });
        HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set);
        HostLlmProfiles.ElectDefault(document, set.Profiles[0]);

        Assert.Empty(Errors(document));
    }

    [Theory]
    [InlineData("${OPENAI_API_KEY}", "OPENAI_API_KEY")]
    [InlineData(" ${X} ", "X")]
    [InlineData("${}", "<the variable's name>")]
    [InlineData("${MY KEY}", "<the variable's name>")]
    public void A_key_written_as_a_placeholder_is_refused_with_the_run_s_remedy(string apiKey, string suggestion)
    {
        var document = With("Llm:ApiKey", System.Text.Json.JsonSerializer.Serialize(apiKey));

        var messages = Validator().Validate(document).Where(message => message.Path == "Llm:ApiKey").ToList();

        var error = Assert.Single(messages);
        Assert.Equal(ValidationSeverity.Error, error.Severity);
        Assert.Equal(ValidationCodes.LlmApiKeyPlaceholder, error.Code);
        Assert.Contains($"\"ApiKeyEnvVar\": \"{suggestion}\"", error.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_holding_a_placeholder_among_other_text_is_a_key_in_clear_as_the_run_reads_it()
    {
        var messages = Validator().Validate(With("Llm:ApiKey", "\"sk-${X}\""))
            .Where(message => message.Path == "Llm:ApiKey")
            .ToList();

        Assert.Equal(ValidationCodes.InlineApiKey, Assert.Single(messages).Code);
    }

    [Fact]
    public void A_placeholder_key_in_an_entry_is_refused_at_its_path()
    {
        var document = With("Llm:Profiles:openai", """{ "BaseUrl": "https://api.openai.com/v1", "Model": "gpt-5", "ApiKey": "${OPENAI_API_KEY}" }""");

        var error = Assert.Single(Errors(document));

        Assert.Equal(ValidationCodes.LlmApiKeyPlaceholder, error.Code);
        Assert.Equal("Llm:Profiles:openai:ApiKey", error.Path);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData(" default ")]
    public void An_entry_named_default_is_refused_at_its_path(string id)
    {
        // Parsed, not set by path: a path's segments are trimmed, a JSON property name is not.
        var document = AppSettingsDocument.Parse($$"""
            { "Llm": { "BaseUrl": "http://localhost:11434", "Model": "qwen3",
                       "Profiles": { "{{id}}": { "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b" } } } }
            """);

        var error = Assert.Single(Errors(document));

        Assert.Equal(ValidationCodes.LlmProfileReservedName, error.Code);
        Assert.Equal($"Llm:Profiles:{id}", error.Path);
    }

    [Fact]
    public void The_placeholder_rule_and_the_variable_rule_are_studio_core_functions()
    {
        Assert.True(LlmSection.IsKeyPlaceholder(" ${X} "));
        Assert.False(LlmSection.IsKeyPlaceholder("sk-${X}"));
        Assert.True(LlmSection.IsVariableName("DEEPSEEK_API_KEY"));
        Assert.False(LlmSection.IsVariableName("MY KEY"));
        Assert.False(LlmSection.IsVariableName("A=B"));
    }
}
