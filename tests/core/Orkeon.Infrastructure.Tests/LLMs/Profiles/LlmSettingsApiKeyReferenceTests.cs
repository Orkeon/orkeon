using System.Security;
using Microsoft.Extensions.Configuration;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// STUDIO-49: a section of the <c>Llm</c> shape names the environment variable holding its key —
/// <c>ApiKeyEnvVar</c>, the name, never the key —, and the runtime reads it when the
/// configuration resolves no <c>ApiKey</c>: the process environment first, then, on Windows, the
/// user's persistent scope, which is where Studio remembers a key. Studio remembered keys under
/// the provider's variable (<c>ZAI_API_KEY</c>), which no run read: outside Studio — a terminal,
/// a scheduled team, the REPL — every call answered "API key is required".
/// <para>
/// The environment is the reader's seam (<see cref="LlmKeyEnvironment"/>): no test sets a
/// variable of the machine but the one with a unique name that proves the process is not written.
/// </para>
/// </summary>
public sealed class LlmSettingsApiKeyReferenceTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static LlmKeyEnvironment Environment(
        IReadOnlyDictionary<string, string>? process = null,
        IReadOnlyDictionary<string, string>? user = null) =>
        new(name => process?.GetValueOrDefault(name), name => user?.GetValueOrDefault(name));

    private static readonly LlmKeyEnvironment Empty = Environment();

#pragma warning disable CS0618 // ApiKey is the field every provider reads.
    private static string? KeyOf(LlmConfig config) => config.ApiKey;
#pragma warning restore CS0618

    // ── the reference resolves ──────────────────────────────────────────────

    [Fact]
    public void The_default_section_reads_its_key_from_the_variable_it_names()
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.z.ai/api/paas/v4"),
            ("Llm:ApiKeyEnvVar", "ZAI_API_KEY"));

        var config = LlmSettings.ReadDefault(configuration, Environment(process: new Dictionary<string, string> { ["ZAI_API_KEY"] = "sk-zai" }));

        Assert.Equal("sk-zai", KeyOf(config));
    }

    [Fact]
    public void A_profile_reads_its_key_from_the_variable_it_names()
    {
        var configuration = Configuration(
            ("Llm:Profiles:z-ai:BaseUrl", "https://api.z.ai/api/paas/v4"),
            ("Llm:Profiles:z-ai:ApiKeyEnvVar", "ZAI_API_KEY"),
            ("Llm:Profiles:deepseek:BaseUrl", "https://api.deepseek.com"),
            ("Llm:Profiles:deepseek:ApiKeyEnvVar", "DEEPSEEK_API_KEY"));

        var profiles = LlmSettings.ReadProfiles(configuration, Environment(process: new Dictionary<string, string>
        {
            ["ZAI_API_KEY"] = "sk-zai",
            ["DEEPSEEK_API_KEY"] = "sk-deepseek",
        }));

        Assert.Equal("sk-zai", KeyOf(profiles.Single(p => p.Name == "z-ai").Config));
        Assert.Equal("sk-deepseek", KeyOf(profiles.Single(p => p.Name == "deepseek").Config));
    }

    [Fact]
    public void A_key_the_configuration_resolves_wins_over_the_reference()
    {
        // The file's ApiKey, ORKEON_Llm__ApiKey, a Studio launch: every existing installation
        // keeps the key it had, and "the environment wins over the file" stays true.
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.deepseek.com"),
            ("Llm:ApiKey", "sk-from-configuration"),
            ("Llm:ApiKeyEnvVar", "DEEPSEEK_API_KEY"),
            ("Llm:Profiles:z-ai:ApiKey", "sk-profile-from-configuration"),
            ("Llm:Profiles:z-ai:ApiKeyEnvVar", "ZAI_API_KEY"));
        var environment = Environment(process: new Dictionary<string, string>
        {
            ["DEEPSEEK_API_KEY"] = "sk-deepseek",
            ["ZAI_API_KEY"] = "sk-zai",
        });

        Assert.Equal("sk-from-configuration", KeyOf(LlmSettings.ReadDefault(configuration, environment)));
        Assert.Equal("sk-profile-from-configuration", KeyOf(Assert.Single(LlmSettings.ReadProfiles(configuration, environment)).Config));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_variable_absent_or_blank_gives_no_key(string? value)
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.deepseek.com"),
            ("Llm:ApiKeyEnvVar", "DEEPSEEK_API_KEY"));
        var process = new Dictionary<string, string>();
        if (value is not null)
            process["DEEPSEEK_API_KEY"] = value;

        Assert.Null(KeyOf(LlmSettings.ReadDefault(configuration, Environment(process: process, user: process))));
    }

    [Fact]
    public void A_variable_only_in_the_user_scope_is_found_and_never_copied_into_the_process()
    {
        // Studio remembers a key in the user scope (HKCU\Environment): a terminal opened before
        // it, or a scheduled task, has no copy in its environment block. The run reads the user
        // scope, and leaves the process alone — what it starts must not inherit a key it had not.
        var name = "ORKEON_TEST_" + Guid.NewGuid().ToString("N");
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.z.ai/api/paas/v4"),
            ("Llm:ApiKeyEnvVar", name));
        var environment = new LlmKeyEnvironment(
            System.Environment.GetEnvironmentVariable,
            variable => variable == name ? "sk-user-scope" : null);

        var config = LlmSettings.ReadDefault(configuration, environment);

        Assert.Equal("sk-user-scope", KeyOf(config));
        Assert.Null(System.Environment.GetEnvironmentVariable(name));
    }

    [Fact]
    public void The_process_environment_is_read_before_the_user_scope()
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.z.ai/api/paas/v4"),
            ("Llm:ApiKeyEnvVar", "ZAI_API_KEY"));

        var config = LlmSettings.ReadDefault(configuration, Environment(
            process: new Dictionary<string, string> { ["ZAI_API_KEY"] = "sk-process" },
            user: new Dictionary<string, string> { ["ZAI_API_KEY"] = "sk-user" }));

        Assert.Equal("sk-process", KeyOf(config));
    }

    [Fact]
    public void A_user_scope_that_cannot_be_read_counts_as_an_absent_variable()
    {
        // A registry read refused (a virtual service account, a locked-down profile) is a key not
        // found — the call then fails as it does without a key — never a host that will not start.
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.z.ai/api/paas/v4"),
            ("Llm:ApiKeyEnvVar", "ZAI_API_KEY"),
            ("Llm:Profiles:z-ai:ApiKeyEnvVar", "ZAI_API_KEY"));
        var environment = new LlmKeyEnvironment(_ => null, _ => throw new SecurityException("registry access denied"));

        Assert.Null(KeyOf(LlmSettings.ReadDefault(configuration, environment)));
        Assert.Null(KeyOf(Assert.Single(LlmSettings.ReadProfiles(configuration, environment)).Config));
    }

    // ── what refuses the start ──────────────────────────────────────────────

    [Theory]
    [InlineData("ZAI=API_KEY")]
    [InlineData("ZAI API KEY")]
    [InlineData("ZAI_API_KEY\n")]
    [InlineData("\tZAI_API_KEY")]
    public void A_reference_that_is_not_a_variable_name_refuses_the_start_without_repeating_it(string reference)
    {
        var defaultError = Assert.Throws<InvalidOperationException>(() =>
            LlmSettings.ReadDefault(Configuration(("Llm:Model", "glm-5"), ("Llm:ApiKeyEnvVar", reference)), Empty));
        var profileError = Assert.Throws<InvalidOperationException>(() =>
            LlmSettings.ReadProfiles(Configuration(("Llm:Profiles:z-ai:ApiKeyEnvVar", reference)), Empty));

        Assert.Contains("Llm:ApiKeyEnvVar", defaultError.Message, StringComparison.Ordinal);
        Assert.Contains("Llm:Profiles:z-ai:ApiKeyEnvVar", profileError.Message, StringComparison.Ordinal);
        // The value may be a key pasted in the wrong field: no message repeats it.
        foreach (var message in new[] { defaultError.Message, profileError.Message })
        {
            Assert.DoesNotContain("ZAI", message, StringComparison.Ordinal);
            Assert.DoesNotContain("API_KEY", message, StringComparison.Ordinal);
            Assert.DoesNotContain("API KEY", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_ApiKey_written_as_a_template_refuses_the_start_and_names_the_fix()
    {
        // The old examples/appsettings templates wrote "ApiKey": "${DEEPSEEK_API_KEY}". Nothing
        // ever expanded it: copied as is, the text went out as the key and came back a 401.
        var defaultError = Assert.Throws<InvalidOperationException>(() =>
            LlmSettings.ReadDefault(Configuration(
                ("Llm:BaseUrl", "https://api.deepseek.com"), ("Llm:ApiKey", "${DEEPSEEK_API_KEY}")), Empty));
        var profileError = Assert.Throws<InvalidOperationException>(() =>
            LlmSettings.ReadProfiles(Configuration(("Llm:Profiles:z-ai:ApiKey", "${ZAI_API_KEY}")), Empty));

        Assert.Contains("Llm:ApiKey", defaultError.Message, StringComparison.Ordinal);
        Assert.Contains("\"ApiKeyEnvVar\": \"DEEPSEEK_API_KEY\"", defaultError.Message, StringComparison.Ordinal);
        Assert.Contains("Llm:Profiles:z-ai:ApiKey", profileError.Message, StringComparison.Ordinal);
        Assert.Contains("\"ApiKeyEnvVar\": \"ZAI_API_KEY\"", profileError.Message, StringComparison.Ordinal);
    }

    // ── blank values: the overlay of a Studio launch ────────────────────────

    [Fact]
    public void Blank_values_read_as_absent_Thinking_Effort_included()
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.z.ai/api/paas/v4"),
            ("Llm:Model", "glm-5"),
            ("Llm:ApiKey", ""),
            ("Llm:ApiKeyEnvVar", ""),
            ("Llm:Temperature", ""),
            ("Llm:TimeoutSeconds", ""),
            ("Llm:StreamIdleSeconds", ""),
            ("Llm:MaxTokens", ""),
            ("Llm:MaxRetries", ""),
            ("Llm:Thinking:Enabled", ""),
            ("Llm:Thinking:Effort", ""),
            ("Llm:Profiles:z-ai:Model", "glm-5"),
            ("Llm:Profiles:z-ai:Temperature", ""),
            ("Llm:Profiles:z-ai:TimeoutSeconds", " "),
            ("Llm:Profiles:z-ai:StreamIdleSeconds", " "),
            ("Llm:Profiles:z-ai:Thinking:Effort", ""));

        var config = LlmSettings.ReadDefault(configuration, Empty);
        var profile = Assert.Single(LlmSettings.ReadProfiles(configuration, Empty)).Config;

        Assert.Null(KeyOf(config));
        // A blank temperature sets none: the model applies its own (GAP-36).
        Assert.Null(config.Temperature);
        Assert.Null(config.TimeoutSeconds);
        Assert.Null(config.StreamIdleSeconds);
        Assert.Null(config.MaxTokens);
        Assert.Equal(LlmDefaults.DefaultMaxRetries, config.MaxRetries);
        Assert.Null(config.Thinking);
        Assert.Null(profile.Temperature);
        Assert.Null(profile.TimeoutSeconds);
        Assert.Null(profile.StreamIdleSeconds);
        Assert.Null(profile.Thinking);
    }

    [Fact]
    public void StreamIdleSeconds_binds_on_the_default_and_on_a_profile()
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.z.ai/api/paas/v4"),
            ("Llm:Model", "glm-5"),
            ("Llm:StreamIdleSeconds", "45"),
            ("Llm:Profiles:z-ai:Model", "glm-5"),
            ("Llm:Profiles:z-ai:StreamIdleSeconds", "120"));

        var config = LlmSettings.ReadDefault(configuration, Empty);
        var profile = Assert.Single(LlmSettings.ReadProfiles(configuration, Empty)).Config;

        Assert.Equal(45, config.StreamIdleSeconds);
        Assert.Equal(120, profile.StreamIdleSeconds);
    }

    [Fact]
    public void A_blanked_reference_over_the_file_sends_no_key()
    {
        // Decision 4: a team launched on another setting than the default blanks every default
        // field it does not set, ApiKeyEnvVar first — otherwise the default's key would go to
        // the team's endpoint, another vendor.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:BaseUrl"] = "https://api.deepseek.com",
                ["Llm:ApiKeyEnvVar"] = "DEEPSEEK_API_KEY",
                ["Llm:TimeoutSeconds"] = "600",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:BaseUrl"] = "https://api.z.ai/api/paas/v4",
                ["Llm:ApiKey"] = "",
                ["Llm:ApiKeyEnvVar"] = "",
                ["Llm:TimeoutSeconds"] = "",
            })
            .Build();

        var config = LlmSettings.ReadDefault(configuration, Environment(process: new Dictionary<string, string> { ["DEEPSEEK_API_KEY"] = "sk-deepseek" }));

        Assert.Equal(new Uri("https://api.z.ai/api/paas/v4"), config.BaseUrl);
        Assert.Null(KeyOf(config));
        Assert.Null(config.TimeoutSeconds);
    }

    [Fact]
    public void A_section_whose_every_value_is_blank_configures_no_default()
    {
        // A team launched on the « no model » setting blanks the whole default: it runs on the
        // echo provider, as the setting says — not on an endpoint-less OpenAI provider.
        var configuration = Configuration(
            ("Llm:BaseUrl", ""),
            ("Llm:Model", ""),
            ("Llm:ApiKeyEnvVar", ""),
            ("Llm:Thinking:Enabled", ""),
            ("Llm:Profiles:local:BaseUrl", "http://localhost:11434"));

        Assert.False(LlmSettings.HasDefault(configuration));
        Assert.True(LlmSettings.HasDefault(Configuration(("Llm:Model", ""), ("Llm:Thinking:Enabled", "false"))));
    }

    // ── where the key came from, never the key nor the variable's name ──────

    [Fact]
    public void The_description_says_where_each_key_came_from_and_never_the_key_nor_the_variable()
    {
        var configuration = Configuration(
            ("Llm:ApiKey", "sk-inline"),
            ("Llm:Profiles:process:ApiKeyEnvVar", "PROCESS_KEY_VAR"),
            ("Llm:Profiles:user:ApiKeyEnvVar", "USER_KEY_VAR"),
            ("Llm:Profiles:unset:ApiKeyEnvVar", "UNSET_KEY_VAR"),
            ("Llm:Profiles:none:Model", "m"));
        var environment = Environment(
            process: new Dictionary<string, string> { ["PROCESS_KEY_VAR"] = "sk-process" },
            user: new Dictionary<string, string> { ["USER_KEY_VAR"] = "sk-user" });
        string Describe(string path) => LlmSettings.DescribeApiKey(configuration.GetSection(path), environment);

        var described = new Dictionary<string, string>
        {
            ["Llm"] = Describe("Llm"),
            ["process"] = Describe("Llm:Profiles:process"),
            ["user"] = Describe("Llm:Profiles:user"),
            ["unset"] = Describe("Llm:Profiles:unset"),
            ["none"] = Describe("Llm:Profiles:none"),
        };

        Assert.Contains("Llm:ApiKey", described["Llm"], StringComparison.Ordinal);
        Assert.Contains("Llm:Profiles:process:ApiKeyEnvVar", described["process"], StringComparison.Ordinal);
        Assert.DoesNotContain("user environment", described["process"], StringComparison.Ordinal);
        Assert.Contains("Llm:Profiles:user:ApiKeyEnvVar", described["user"], StringComparison.Ordinal);
        Assert.Contains("user environment", described["user"], StringComparison.Ordinal);
        Assert.Contains("Llm:Profiles:unset:ApiKeyEnvVar", described["unset"], StringComparison.Ordinal);
        Assert.Contains("not set", described["unset"], StringComparison.Ordinal);
        Assert.Equal("none", described["none"]);
        foreach (var text in described.Values)
        {
            Assert.DoesNotContain("sk-", text, StringComparison.Ordinal);
            Assert.DoesNotContain("_KEY_VAR", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_unresolved_references_are_the_sections_whose_variable_is_unset_and_not_masked()
    {
        var configuration = Configuration(
            ("Llm:BaseUrl", "https://api.deepseek.com"),
            ("Llm:ApiKeyEnvVar", "DEEPSEEK_API_KEY"),
            ("Llm:Profiles:z-ai:ApiKeyEnvVar", "ZAI_API_KEY"),
            ("Llm:Profiles:kimi:ApiKeyEnvVar", "MOONSHOT_API_KEY"),
            ("Llm:Profiles:masked:ApiKey", "sk-inline"),
            ("Llm:Profiles:masked:ApiKeyEnvVar", "UNSET_KEY_VAR"),
            ("Llm:Profiles:local:BaseUrl", "http://localhost:11434"));
        var environment = Environment(user: new Dictionary<string, string> { ["MOONSHOT_API_KEY"] = "sk-kimi" });

        var unresolved = LlmSettings.UnresolvedApiKeyReferences(configuration, environment);

        Assert.Equal(["Llm:ApiKeyEnvVar", "Llm:Profiles:z-ai:ApiKeyEnvVar"], unresolved);
    }
}
