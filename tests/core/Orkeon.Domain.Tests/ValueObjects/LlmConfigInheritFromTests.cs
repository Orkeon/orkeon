using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// GAP-29: <see cref="LlmConfig.InheritFrom"/> is the one rule by which a call's configuration
/// completes the configuration its provider was built with — every field the call leaves unset
/// takes the provider's value, every field it sets wins. The providers used to take a call's
/// configuration whole, so a caller that named a temperature lost the key, the endpoint and the
/// timeout of the provider it reached.
/// </summary>
public sealed class LlmConfigInheritFromTests
{
    private static readonly Uri ProviderUrl = new("https://provider.example.test/v1");

    /// <summary>A provider configured the way a host configures one, every inheritable field set.</summary>
    private static LlmConfig Provider() => LlmConfig.Create("provider-model", "sk-provider") with
    {
        Profile = "claude",
        ApiKeySecretName = "PROVIDER_KEY",
        BaseUrl = ProviderUrl,
        MaxTokens = 2048,
        Seed = 7,
        StopSequences = ["<END>"],
        SystemMessage = "You are the provider.",
        ApiVersion = "2025-04-01-preview",
        WorkspaceId = "wrkspc_provider",
        CustomParameters = new Dictionary<string, object> { ["api_version"] = "v1", ["region"] = "eu" },
        TimeoutSeconds = 600,
        Tools = [new ToolSchema("provider_tool", "A provider tool", [])],
        GrammarGbnf = "root ::= \"x\"",
        Thinking = new LlmThinkingConfig { Enabled = false },
        Cache = new LlmCacheConfig(),
        ResponseFormat = LlmResponseFormat.JsonObject(),
        Temperature = 0.2,
        TopP = 0.9,
        FrequencyPenalty = 0.5,
        PresencePenalty = 0.4,
        MaxRetries = 3,
        GrammarEnabled = true,
        ToolMode = ToolCallMode.Required,
    };

    [Fact]
    public void A_call_that_names_nothing_about_the_connection_takes_the_providers()
    {
        var effective = (LlmConfig.OnProfile() with { Temperature = 0.3 }).InheritFrom(Provider());

        Assert.Equal("sk-provider", effective.ApiKey);
        Assert.Equal("PROVIDER_KEY", effective.ApiKeySecretName);
        Assert.Equal(ProviderUrl, effective.BaseUrl);
        Assert.Equal(600, effective.TimeoutSeconds);
        Assert.Equal("2025-04-01-preview", effective.ApiVersion);
        Assert.Equal("wrkspc_provider", effective.WorkspaceId);
    }

    [Fact]
    public void A_call_that_names_no_model_takes_the_providers()
    {
        Assert.Equal("provider-model", LlmConfig.OnProfile().InheritFrom(Provider()).Model);
        Assert.Equal("provider-model", (LlmConfig.OnProfile() with { Model = "   " }).InheritFrom(Provider()).Model);
    }

    [Fact]
    public void The_nullable_request_options_a_call_leaves_unset_are_the_providers()
    {
        var effective = LlmConfig.OnProfile().InheritFrom(Provider());

        Assert.Equal("claude", effective.Profile);
        Assert.Equal(2048, effective.MaxTokens);
        Assert.Equal(7, effective.Seed);
        Assert.Equal(["<END>"], effective.StopSequences);
        Assert.Equal("You are the provider.", effective.SystemMessage);
        Assert.Equal("provider_tool", Assert.Single(effective.Tools!).Name);
        Assert.Equal("root ::= \"x\"", effective.GrammarGbnf);
        Assert.Equal(false, effective.Thinking?.Enabled);
        Assert.NotNull(effective.Cache);
        Assert.Equal("json_object", effective.ResponseFormat?.Type);
    }

    [Fact]
    public void What_the_call_sets_wins()
    {
        var call = LlmConfig.Create("call-model", "sk-call") with
        {
            Profile = "local",
            ApiKeySecretName = "CALL_KEY",
            BaseUrl = new Uri("https://call.example.test"),
            MaxTokens = 100,
            Seed = 1,
            StopSequences = ["<STOP>"],
            SystemMessage = "You are the call.",
            ApiVersion = "v1",
            WorkspaceId = "wrkspc_call",
            TimeoutSeconds = 45,
            Tools = [new ToolSchema("call_tool", "A call tool", [])],
            GrammarGbnf = "root ::= \"y\"",
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = "high" },
            ResponseFormat = LlmResponseFormat.Text(),
        };

        var effective = call.InheritFrom(Provider());

        Assert.Equal("call-model", effective.Model);
        Assert.Equal("local", effective.Profile);
        Assert.Equal("sk-call", effective.ApiKey);
        Assert.Equal("CALL_KEY", effective.ApiKeySecretName);
        Assert.Equal(new Uri("https://call.example.test"), effective.BaseUrl);
        Assert.Equal(100, effective.MaxTokens);
        Assert.Equal(1, effective.Seed);
        Assert.Equal(["<STOP>"], effective.StopSequences);
        Assert.Equal("You are the call.", effective.SystemMessage);
        Assert.Equal("v1", effective.ApiVersion);
        Assert.Equal("wrkspc_call", effective.WorkspaceId);
        Assert.Equal(45, effective.TimeoutSeconds);
        Assert.Equal("call_tool", Assert.Single(effective.Tools!).Name);
        Assert.Equal("root ::= \"y\"", effective.GrammarGbnf);
        Assert.Equal("high", effective.Thinking?.Effort);
        Assert.Equal("text", effective.ResponseFormat?.Type);
    }

    [Fact]
    public void The_sampling_settings_and_the_tool_mode_are_the_callers()
    {
        // A caller that builds a configuration owns its sampling: these settings have no "unset"
        // value, so the call's — its own or the defaults it accepted — is the one sent.
        var effective = (LlmConfig.OnProfile() with { Temperature = 0.3 }).InheritFrom(Provider());

        Assert.Equal(0.3, effective.Temperature);
        Assert.Equal(1.0, effective.TopP);
        Assert.Equal(0.0, effective.FrequencyPenalty);
        Assert.Equal(0.0, effective.PresencePenalty);
        Assert.Equal(ToolCallMode.Auto, effective.ToolMode);
        // Read once, from the configuration the provider is built with — never from a call.
        Assert.Equal(LlmDefaults.DefaultMaxRetries, effective.MaxRetries);
        Assert.False(effective.GrammarEnabled);
    }

    [Fact]
    public void Custom_parameters_merge_and_the_calls_keys_win()
    {
        var call = LlmConfig.OnProfile() with
        {
            CustomParameters = new Dictionary<string, object> { ["api_version"] = "2024-02-01", ["trace"] = true },
        };

        var merged = call.InheritFrom(Provider()).CustomParameters;

        Assert.Equal("2024-02-01", merged["api_version"]);
        Assert.Equal("eu", merged["region"]);
        Assert.Equal(true, merged["trace"]);
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Inheriting_from_a_provider_that_sets_nothing_changes_nothing()
    {
        var call = LlmConfig.Create("call-model") with { Temperature = 0.1, MaxTokens = 50 };

        var effective = call.InheritFrom(LlmConfig.OnProfile());

        Assert.Equal("call-model", effective.Model);
        Assert.Null(effective.ApiKey);
        Assert.Null(effective.BaseUrl);
        Assert.Null(effective.TimeoutSeconds);
        Assert.Equal(50, effective.MaxTokens);
        Assert.Equal(0.1, effective.Temperature);
        Assert.Empty(effective.CustomParameters);
    }

    [Fact]
    public void Completing_twice_is_completing_once()
    {
        var once = (LlmConfig.OnProfile() with { Temperature = 0.3 }).InheritFrom(Provider());
        var twice = once.InheritFrom(Provider());

        Assert.Equal(once.ApiKey, twice.ApiKey);
        Assert.Equal(once.BaseUrl, twice.BaseUrl);
        Assert.Equal(once.TimeoutSeconds, twice.TimeoutSeconds);
        Assert.Equal(once.Model, twice.Model);
        Assert.Equal(once.CustomParameters.OrderBy(p => p.Key), twice.CustomParameters.OrderBy(p => p.Key));
    }

    [Fact]
    public void A_timeout_nobody_sets_is_unset_and_resolves_to_thirty_seconds()
    {
        Assert.Null(LlmConfig.OnProfile().TimeoutSeconds);
        Assert.Null(LlmConfig.Create("m").TimeoutSeconds);
        Assert.Equal(LlmDefaults.DefaultTimeoutSeconds, LlmConfig.OnProfile().ResolveTimeoutSeconds());
        Assert.Equal(30, LlmDefaults.DefaultTimeoutSeconds);
        Assert.Equal(600, (LlmConfig.OnProfile() with { TimeoutSeconds = 600 }).ResolveTimeoutSeconds());
    }

    [Fact]
    public void InheritFrom_refuses_a_missing_provider_configuration() =>
        Assert.Throws<ArgumentNullException>(() => LlmConfig.OnProfile().InheritFrom(null!));
}
