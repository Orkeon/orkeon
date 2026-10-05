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
    /// <summary>The integer fields among those the validator checks as numbers — each one a screen writes with SetInt32.</summary>
    private static readonly string[] IntegerFields =
    [
        "Llm:MaxTokens",
        "Llm:TimeoutSeconds",
        "RateLimiting:MaxConcurrentRequests",
        "RateLimiting:GlobalRequestsPerMinute",
        "RateLimiting:ProviderRequestsPerMinute",
        "RateLimiting:AgentRequestsPerMinute",
        "RateLimiting:QueueLimit",
        "LlmLogging:MaxBodyLengthChars",
        "Orkeon:Rag:Corrective:MaxIterations",
    ];

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
        Assert.Equal(AppSettingsValidator.LlmNotConfiguredWarning, message.Text);
        Assert.StartsWith(AppSettingsValidator.LlmNotConfiguredMessage, message.Text, StringComparison.Ordinal);
        Assert.Contains("<undefined-llm>", message.Text, StringComparison.Ordinal);

        // Studio only sees the file: it must say the environment can already have answered
        // this, or it sends users hunting for a problem ORKEON_Llm__* already solved.
        Assert.Contains("ORKEON_Llm__BaseUrl", message.Text, StringComparison.Ordinal);
        Assert.Contains("emits no warning", message.Text, StringComparison.Ordinal);
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
    public void The_thinking_switch_must_be_a_boolean_and_its_effort_a_string()
    {
        // LLM-11: Llm:Thinking:{Enabled,Effort} are the keys the runner reads.
        var wrongSwitch = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "kimi-k2.6", "Thinking": { "Enabled": "yes" } } }""");
        var switchMessage = Find(Validator().Validate(wrongSwitch), ValidationCodes.InvalidFieldType);
        Assert.NotNull(switchMessage);
        Assert.Equal("Llm:Thinking:Enabled", switchMessage.Path);

        var wrongEffort = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "kimi-k2.6", "Thinking": { "Effort": { "level": "high" } } } }""");
        var effortMessage = Find(Validator().Validate(wrongEffort), ValidationCodes.InvalidFieldType);
        Assert.NotNull(effortMessage);
        Assert.Equal("Llm:Thinking:Effort", effortMessage.Path);

        var right = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "kimi-k2.6", "Thinking": { "Enabled": false, "Effort": "high" } } }""");
        Assert.Null(Find(Validator().Validate(right), ValidationCodes.InvalidFieldType));
        Assert.False(right.Llm.ThinkingEnabled);
        Assert.Equal("high", right.Llm.ThinkingEffort);
    }

    [Fact]
    public void A_numeric_field_spelled_as_a_numeric_string_is_accepted()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "m", "MaxTokens": "4096" } }""");

        Assert.Null(Find(Validator().Validate(document), ValidationCodes.InvalidFieldType));
    }

    [Fact]
    public void Every_integer_field_a_screen_writes_is_a_number_before_the_file_is_reloaded()
    {
        // STUDIO-53: electing a setting that pins a timeout wrote Llm:TimeoutSeconds through
        // SetInt32, and the validator refused the node as « not a number » — the save with it.
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" } }""");
        foreach (var path in IntegerFields)
            document.SetInt32(path, 600);

        var refused = Validator().Validate(document)
            .Where(m => m.Code == ValidationCodes.InvalidFieldType)
            .Select(m => m.Path);

        Assert.Empty(refused);
    }

    [Fact]
    public void A_temperature_json_cannot_write_is_not_a_number()
    {
        // NaN has no JSON text: the file could not be written, so the save is refused by name.
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" } }""");
        document.Llm.Temperature = double.NaN;

        var message = Find(Validator().Validate(document), ValidationCodes.InvalidFieldType);

        Assert.NotNull(message);
        Assert.Equal("Llm:Temperature", message.Path);
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

    /// <summary>VFS-90 D-03: two settings entries of one root, each with an id, are a valid file.</summary>
    /// <summary>
    /// STUDIO-48 (GAP-19): <c>Orkeon:Rag:LlmProfile</c> naming a profile the file does not define
    /// refuses the start of every run — said before the file is written. A warning: the profile can
    /// come from the launch environment, which is how Studio passes its own model settings.
    /// </summary>
    [Fact]
    public void A_rag_llm_profile_the_file_does_not_define_is_a_warning_listing_the_defined_ones()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": { "Model": "qwen3", "Profiles": { "claude": { "Model": "claude-sonnet-5" } } },
              "Orkeon": { "Rag": { "LlmProfile": "claud" } }
            }
            """);

        var message = Find(Validator().Validate(document), ValidationCodes.UnknownRagLlmProfile);

        Assert.NotNull(message);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
        Assert.Contains("'claud'", message.Text, StringComparison.Ordinal);
        Assert.Contains("default, claude.", message.Text, StringComparison.Ordinal);
        Assert.Equal("Orkeon:Rag:LlmProfile", message.Path);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("CLAUDE")]
    [InlineData("default")]
    [InlineData("")]
    public void A_rag_llm_profile_the_file_defines_or_the_default_is_accepted(string profile)
    {
        var document = AppSettingsDocument.Parse($$"""
            {
              "Llm": { "Model": "qwen3", "Profiles": { "claude": { "Model": "claude-sonnet-5" } } },
              "Orkeon": { "Rag": { "LlmProfile": "{{profile}}" } }
            }
            """);

        Assert.Null(Find(Validator().Validate(document), ValidationCodes.UnknownRagLlmProfile));
    }

    [Fact]
    public void Two_entries_of_one_root_with_ids_validate_without_an_error()
    {
        var a = Orkeon.Domain.Common.MountId.Create();
        var b = Orkeon.Domain.Common.MountId.Create();
        var document = AppSettingsDocument.Parse(
            $$"""{ "Llm": { "Model": "m" }, "Orkeon": { "FileSystem": { "Mounts": [ "{{a}}|/srv/a:/output:rw", "{{b}}|/srv/b:/output:rw" ] } } }""");

        var messages = Validator("/srv/a", "/srv/b").Validate(document, ValidationScope.Saving);

        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
        Assert.NotNull(Find(messages, ValidationCodes.MountSharedRoot));
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
    public void Saving_a_document_without_mounts_is_flagged_and_never_blocked()
    {
        // STUDIO-57: the runner mounts nothing and runs the crew all the same — a team that reads
        // a mail and answers a mail needs no folder, and a team's own folders travel with it.
        // Saving used to block here, on the claim that the runtime refuses to start.
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" } }""");

        var messages = Validator().Validate(document, ValidationScope.Saving);
        var message = Find(messages, ValidationCodes.MountsEmpty);

        Assert.NotNull(message);
        Assert.Equal(ValidationSeverity.Warning, message.Severity);
        Assert.Equal(MountsSection.SectionPath, message.Path);
        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void The_save_scope_reaches_the_json_entry_point_too()
    {
        var messages = Validator().ValidateJson("""{ "Llm": { "Model": "m" } }""", ValidationScope.Saving);

        Assert.Equal(ValidationSeverity.Warning, Assert.Single(messages, m => m.Code == ValidationCodes.MountsEmpty).Severity);
        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void A_document_with_mounts_is_saveable()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" } }""");
        document.Mounts.SetRaw(["/srv/data:/workspace:ro"]);

        var messages = Validator("/srv/data").Validate(document, ValidationScope.Saving);

        Assert.Null(Find(messages, ValidationCodes.MountsEmpty));
        Assert.DoesNotContain(messages, m => m.Severity == ValidationSeverity.Error);
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
        // STUDIO-49: a key the configuration holds wins — it masks the variable the reference names.
        Assert.Contains("masks Llm:ApiKeyEnvVar", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_docker_model_runner_placeholder_key_is_not_reported()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "m", "ApiKey": "not-needed" } }""");

        Assert.Null(Find(Validator().Validate(document), ValidationCodes.InlineApiKey));
    }

    /// <summary>
    /// STUDIO-54, decision 4: beside a reference — a file edited by hand —, the placeholder masks it
    /// like any key the file holds: the run would send <c>not-needed</c> to the endpoint whose key the
    /// variable holds.
    /// </summary>
    [Fact]
    public void The_placeholder_key_beside_a_reference_is_reported_as_it_masks_it()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Model": "deepseek-v4-flash", "ApiKey": "not-needed", "ApiKeyEnvVar": "DEEPSEEK_API_KEY" } }""");

        var message = Find(Validator().Validate(document), ValidationCodes.InlineApiKey);

        Assert.NotNull(message);
        Assert.Equal(ValidationSeverity.Information, message.Severity);
        Assert.Equal("Llm:ApiKey", message.Path);
        Assert.Contains("not-needed", message.Text, StringComparison.Ordinal);
        Assert.Contains("masks Llm:ApiKeyEnvVar", message.Text, StringComparison.Ordinal);
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
