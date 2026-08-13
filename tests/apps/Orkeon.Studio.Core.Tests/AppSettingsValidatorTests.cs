using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Pre-save validation, including the WIN-01 onboarding trap: no <c>Llm</c> section
/// means the runtime degrades silently to the echo provider.
/// </summary>
public sealed class AppSettingsValidatorTests
{
    private static AppSettingsValidator Validator(params string[] existingDirectories) =>
        new(new FakeDirectoryProbe(existingDirectories));

    private static ValidationMessage? Find(IEnumerable<ValidationMessage> messages, string code) =>
        messages.FirstOrDefault(m => m.Code == code);

    [Fact]
    public void A_document_without_an_llm_section_reproduces_the_win01_warning()
    {
        var document = AppSettingsDocument.Parse("""{ "RateLimiting": { "QueueLimit": 32 } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.LlmSectionMissing);

        Assert.NotNull(message);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
        Assert.Equal(AppSettingsValidator.LlmNotConfiguredMessage, message.Text);
        Assert.Contains("<undefined-llm>", message.Text, StringComparison.Ordinal);
        Assert.Equal("Llm", message.Path);
    }

    [Fact]
    public void An_empty_llm_section_also_triggers_win01()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": {} }""");

        Assert.NotNull(Find(Validator().Validate(document), ValidationCodes.LlmSectionMissing));
    }

    [Fact]
    public void A_configured_llm_section_does_not_trigger_win01()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "llama3.2", "BaseUrl": "http://localhost:11434" } }""");

        Assert.Null(Find(Validator().Validate(document), ValidationCodes.LlmSectionMissing));
    }

    [Fact]
    public void A_malformed_document_yields_a_single_json_error()
    {
        var messages = Validator().ValidateJson("{ oops");

        Assert.Equal(ValidationCodes.MalformedJson, Assert.Single(messages).Code);
    }

    [Fact]
    public void A_known_numeric_field_holding_text_is_an_error()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "m" }, "RateLimiting": { "QueueLimit": "many" } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.InvalidFieldType);

        Assert.NotNull(message);
        Assert.Equal("RateLimiting:QueueLimit", message.Path);
    }

    [Fact]
    public void A_numeric_field_spelled_as_a_numeric_string_is_accepted()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "m", "MaxTokens": "4096" } }""");

        Assert.Null(Find(Validator().Validate(document), ValidationCodes.InvalidFieldType));
    }

    [Fact]
    public void A_known_string_field_holding_an_object_is_an_error()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": { "name": "m" } } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.InvalidFieldType);

        Assert.NotNull(message);
        Assert.Equal("Llm:Model", message.Path);
    }

    [Fact]
    public void A_non_absolute_base_url_is_an_error()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m", "BaseUrl": "localhost:11434" } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.InvalidFieldType);

        Assert.NotNull(message);
        Assert.Equal("Llm:BaseUrl", message.Path);
    }

    [Fact]
    public void An_unknown_rag_profile_is_an_error_listing_the_known_ones()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "m" }, "Orkeon": { "Rag": { "Profile": "turbo" } } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.UnknownRagProfile);

        Assert.NotNull(message);
        foreach (var known in RagSection.KnownProfiles)
            Assert.Contains(known, message.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("balanced")]
    [InlineData("quality")]
    [InlineData("adaptive")]
    [InlineData("corrective")]
    public void Every_known_rag_profile_is_accepted(string profile)
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" } }""");
        document.Rag.Profile = profile;

        Assert.Null(Find(Validator().Validate(document), ValidationCodes.UnknownRagProfile));
    }

    [Fact]
    public void A_document_without_mounts_warns_rather_than_fails()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.MountsEmpty);

        Assert.NotNull(message);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
    }

    [Fact]
    public void Declared_mounts_are_validated_against_the_disk()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" } }""");
        document.Mounts.SetRaw(["/srv/data:/workspace:ro", "/gone:/output:rw"]);

        var messages = Validator("/srv/data").Validate(document);

        var message = Find(messages, ValidationCodes.MountPathMissing);
        Assert.NotNull(message);
        Assert.Contains("/gone", message.Text, StringComparison.Ordinal);
        Assert.Null(Find(messages, ValidationCodes.MountsEmpty));
    }

    [Fact]
    public void An_inline_api_key_is_reported_as_advice_not_as_a_failure()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "m", "ApiKey": "sk-live-123" } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.InlineApiKey);

        Assert.NotNull(message);
        Assert.Equal(ValidationSeverity.Information, message.Severity);
        Assert.Contains("ORKEON_Llm__ApiKey", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_docker_model_runner_placeholder_key_is_not_reported()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "m", "ApiKey": "not-needed" } }""");

        Assert.Null(Find(Validator().Validate(document), ValidationCodes.InlineApiKey));
    }

    [Fact]
    public void The_shipped_sample_validates_with_only_the_mounts_warning()
    {
        const string sample = """
            {
              "Llm": {
                "Model": "ai/granite-4.0-h-tiny",
                "BaseUrl": "http://localhost:12434/engines/llama.cpp/v1",
                "ApiKey": "not-needed",
                "Temperature": 0.7,
                "MaxTokens": 4096,
                "TimeoutSeconds": 120
              },
              "RateLimiting": {
                "MaxConcurrentRequests": 1,
                "GlobalRequestsPerMinute": 60,
                "ProviderRequestsPerMinute": 30,
                "AgentRequestsPerMinute": 20,
                "QueueLimit": 32
              }
            }
            """;

        var messages = Validator().ValidateJson(sample);

        Assert.Equal(ValidationCodes.MountsEmpty, Assert.Single(messages).Code);
    }
}
