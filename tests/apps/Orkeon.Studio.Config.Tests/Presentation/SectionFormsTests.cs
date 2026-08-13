using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Tests.Presentation;

public class LlmFormTests
{
    [Fact]
    public void Load_renders_every_field_as_text()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": {
                "Model": "gpt-4o-mini",
                "BaseUrl": "https://api.openai.com/v1",
                "ApiKey": "sk-test",
                "Temperature": 0.4,
                "MaxTokens": 2048,
                "TimeoutSeconds": 90
              }
            }
            """);

        var form = new LlmForm();
        form.LoadFrom(document);

        Assert.Equal("gpt-4o-mini", form.Model);
        Assert.Equal("https://api.openai.com/v1", form.BaseUrl);
        Assert.Equal("sk-test", form.ApiKey);
        Assert.Equal("0.4", form.Temperature);
        Assert.Equal("2048", form.MaxTokens);
        Assert.Equal("90", form.TimeoutSeconds);
    }

    [Fact]
    public void Provider_is_detected_from_the_base_url_and_never_written()
    {
        var form = new LlmForm { BaseUrl = "http://localhost:11434" };
        Assert.Equal("ollama", form.DetectedProvider);

        var document = AppSettingsDocument.CreateEmpty();
        Assert.Empty(form.ApplyTo(document));
        Assert.False(document.ContainsPath("Llm:Provider"));
    }

    [Fact]
    public void Apply_writes_the_typed_values()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var form = new LlmForm
        {
            Model = "llama3",
            BaseUrl = "http://localhost:11434",
            Temperature = "0.2",
            MaxTokens = "512",
        };

        Assert.Empty(form.ApplyTo(document));

        Assert.Equal("llama3", document.Llm.Model);
        Assert.Equal(0.2, document.Llm.Temperature);
        Assert.Equal(512, document.Llm.MaxTokens);
        Assert.Null(document.Llm.TimeoutSeconds);
    }

    [Fact]
    public void A_number_field_holding_letters_is_reported_and_nothing_is_written()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var form = new LlmForm { Model = "llama3", MaxTokens = "many" };

        var errors = form.ApplyTo(document);

        Assert.Single(errors);
        Assert.Contains("Llm:MaxTokens", errors[0]);
        Assert.Null(document.Llm.Model);
    }

    [Fact]
    public void Clearing_a_field_removes_the_key()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m", "ApiKey": "sk-test" } }""");
        var form = new LlmForm();
        form.LoadFrom(document);

        form.ApiKey = "";
        Assert.Empty(form.ApplyTo(document));

        Assert.False(document.ContainsPath("Llm:ApiKey"));
        Assert.Equal("m", document.Llm.Model);
    }

    [Fact]
    public void A_real_inline_key_is_flagged_but_the_docker_placeholder_is_not()
    {
        Assert.True(new LlmForm { ApiKey = "sk-live" }.StoresApiKeyInClearText);
        Assert.False(new LlmForm { ApiKey = "not-needed" }.StoresApiKeyInClearText);
        Assert.False(new LlmForm().StoresApiKeyInClearText);
    }

    [Fact]
    public void The_api_key_recommendation_names_the_environment_variable_the_runtime_reads()
    {
        Assert.Contains("ORKEON_Llm__ApiKey", LlmForm.ApiKeyRecommendation);
    }
}

public class RateLimitingFormTests
{
    [Fact]
    public void Round_trips_the_five_budgets()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "RateLimiting": {
                "MaxConcurrentRequests": 4,
                "GlobalRequestsPerMinute": 60,
                "ProviderRequestsPerMinute": 30,
                "AgentRequestsPerMinute": 10,
                "QueueLimit": 100
              }
            }
            """);

        var form = new RateLimitingForm();
        form.LoadFrom(document);
        Assert.Equal("4", form.MaxConcurrentRequests);
        Assert.Equal("100", form.QueueLimit);

        form.QueueLimit = "50";
        Assert.Empty(form.ApplyTo(document));
        Assert.Equal(50, document.RateLimiting.QueueLimit);
    }

    [Fact]
    public void Every_bad_number_is_reported_at_once()
    {
        var form = new RateLimitingForm { QueueLimit = "lots", AgentRequestsPerMinute = "some" };

        var errors = form.ApplyTo(AppSettingsDocument.CreateEmpty());

        Assert.Equal(2, errors.Count);
    }
}

public class RagFormTests
{
    [Fact]
    public void The_profile_list_is_closed_and_starts_with_the_unset_choice()
    {
        Assert.Equal(RagForm.UnsetProfileLabel, RagForm.ProfileChoices[0]);
        Assert.Contains("fast", RagForm.Profiles);
        Assert.Contains("corrective", RagForm.Profiles);
        Assert.Equal(RagForm.Profiles.Count + 1, RagForm.ProfileChoices.Count);
    }

    [Fact]
    public void Selecting_a_profile_by_index_round_trips()
    {
        var form = new RagForm();

        form.SelectProfile(2);
        Assert.Equal(RagForm.Profiles[1], form.Profile);
        Assert.Equal(2, form.ProfileChoiceIndex);

        form.SelectProfile(0);
        Assert.Null(form.Profile);
        Assert.Equal(0, form.ProfileChoiceIndex);
    }

    [Fact]
    public void An_unknown_profile_is_refused_rather_than_written()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var form = new RagForm { Profile = "turbo" };

        var errors = form.ApplyTo(document);

        Assert.Single(errors);
        Assert.False(document.ContainsPath("Orkeon:Rag:Profile"));
    }

    [Fact]
    public void Switches_are_tri_state_so_unset_and_false_stay_different()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var form = new RagForm { HybridRetrievalEnabled = false, WebFallbackEnabled = null };

        Assert.Empty(form.ApplyTo(document));

        Assert.Equal(false, document.Rag.HybridRetrievalEnabled);
        Assert.False(document.ContainsPath("Orkeon:Rag:WebFallback:Enabled"));
    }

    [Fact]
    public void Web_fallback_needs_both_switches()
    {
        var form = new RagForm { CorrectiveWebFallbackEnabled = true };
        Assert.False(form.WebFallbackFullyEnabled);

        form.WebFallbackEnabled = true;
        Assert.True(form.WebFallbackFullyEnabled);
    }
}

public class LoggingFormTests
{
    [Fact]
    public void The_default_level_round_trips_and_the_other_categories_are_listed()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Logging": { "LogLevel": { "Default": "Information", "Microsoft": "Warning" } }
            }
            """);

        var form = new LoggingForm();
        form.LoadFrom(document);

        Assert.Equal("Information", form.DefaultLevel);
        Assert.Contains("Microsoft = Warning", form.OtherCategories);

        form.SelectLevel(LoggingForm.LevelChoices.Count - 1);
        Assert.Empty(form.ApplyTo(document));
        Assert.Equal("None", document.Logging.DefaultLevel);

        // The category the editor has no field for is untouched.
        Assert.Equal("Warning", document.Logging.GetLevel("Microsoft"));
    }

    [Fact]
    public void An_unknown_level_is_refused()
    {
        var form = new LoggingForm { DefaultLevel = "Chatty" };

        Assert.Single(form.ApplyTo(AppSettingsDocument.CreateEmpty()));
    }
}

public class LlmLoggingFormTests
{
    [Fact]
    public void Round_trips_the_three_knobs()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "LlmLogging": { "FullEmbeddingLog": false, "LogStreamingExchanges": true, "MaxBodyLengthChars": 4096 }
            }
            """);

        var form = new LlmLoggingForm();
        form.LoadFrom(document);

        Assert.Equal(false, form.FullEmbeddingLog);
        Assert.Equal(true, form.LogStreamingExchanges);
        Assert.Equal("4096", form.MaxBodyLengthChars);

        form.MaxBodyLengthChars = "0";
        Assert.Empty(form.ApplyTo(document));
        Assert.Equal(0, document.LlmLogging.MaxBodyLengthChars);
    }

    [Fact]
    public void The_activation_notice_says_logging_is_turned_on_per_run()
    {
        Assert.Contains("--llm-log", LlmLoggingForm.ActivationNotice);
    }
}
