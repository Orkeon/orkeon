using System.Text.Json;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The guard the settings files and the blocks of the documentation go through: it refuses what the
/// start validation refuses — a key no section carries, a value its key cannot take, a retired key —
/// on a text nobody starts, against everything Orkeon reads.
/// </summary>
public sealed class SettingsFileGuardTests
{
    private static IReadOnlyList<string> Problems(string text, bool openRoot = false)
    {
        using var settings = SettingsFileGuard.Read(text);
        Assert.NotNull(settings);
        return SettingsFileGuard.Complete.Problems(settings.RootElement, openRoot);
    }

    [Fact]
    public void The_keys_of_the_catalogue_pass_whatever_their_case_and_the_form_of_their_value() =>
        Assert.Empty(Problems("""
            {
              "RateLimiting": { "GlobalRequestsPerMinute": 60, "maxconcurrentrequests": "4", "QueueLimit": "" },
              "Llm": { "Model": "qwen3:8b", "TimeoutSeconds": 600, "Grammar": "true", "Temperature": 0.2 },
              "Logging": { "LogLevel": { "Default": "information", "Orkeon.Infrastructure": "Debug" }, "Console": { "FormatterName": "simple" } },
              "Orkeon": { "Rag": { "WebFallback": { "Timeout": "00:00:10" } } }
            }
            """));

    [Fact]
    public void A_key_no_section_carries_is_refused_with_the_closest_one_and_where_the_keys_are_listed()
    {
        var problem = Assert.Single(Problems("""{ "RateLimiting": { "RequestsPerMinute": 60, "QueueLimit": 5 } }"""));

        Assert.StartsWith("RateLimiting:RequestsPerMinute is not a setting: RateLimiting carries AgentRequestsPerMinute, ", problem, StringComparison.Ordinal);
        Assert.Contains("Did you mean RateLimiting:AgentRequestsPerMinute?", problem, StringComparison.Ordinal);
        Assert.EndsWith("`orkeon settings RateLimiting` lists its keys.", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "RateLimiting": { "QueueLimit": "five" } }""", "RateLimiting:QueueLimit is 'five', which is not an integer.")]
    [InlineData("""{ "RateLimiting": { "QueueLimit": 2.5 } }""", "RateLimiting:QueueLimit is '2.5', which is not an integer.")]
    [InlineData("""{ "Llm": { "Grammar": "yes" } }""", "Llm:Grammar is 'yes', which is not a boolean.")]
    [InlineData("""{ "Llm": { "Temperature": "warm" } }""", "Llm:Temperature is 'warm', which is not a number.")]
    [InlineData("""{ "Orkeon": { "Rag": { "WebFallback": { "Timeout": "ten seconds" } } } }""", "Orkeon:Rag:WebFallback:Timeout is 'ten seconds', which is not a duration.")]
    [InlineData("""{ "MCP": { "Servers": { "files": { "Transport": "Pipe" } } } }""", "MCP:Servers:files:Transport is 'Pipe', which is none of Stdio, Sse.")]
    [InlineData("""{ "Logging": { "LogLevel": { "Default": "Verbose" } } }""", "Logging:LogLevel:Default is 'Verbose', which is none of Trace, Debug, Information, Warning, Error, Critical, None.")]
    public void A_value_its_key_cannot_take_is_refused_with_the_type_the_key_holds(string text, string expected) =>
        Assert.Equal([expected], Problems(text));

    [Fact]
    public void A_name_the_operator_chooses_and_an_index_stand_where_the_catalogue_writes_them()
    {
        Assert.Empty(Problems("""
            {
              "MCP": { "Servers": { "fichiers": { "Command": "npx", "Args": ["-y", "server"], "Env": { "NODE_ENV": "production" } } } },
              "Llm": { "Profiles": { "fast": { "Model": "qwen3:8b" } } },
              "Orkeon": {
                "Host": { "Crews": [ { "Name": "veille", "Path": "/crews/veille", "Mounts": ["/data:/docs:ro"] } ] },
                "Tools": { "Email": { "Accounts": { "perso": { "Provider": "gmail", "Rights": "Read, Organize, Draft", "Send": { "AllowedRecipients": ["*@example.org"] } } } } }
              }
            }
            """));

        Assert.Equal(
            ["Orkeon:Host:Crews:0:Nme is not a setting: Orkeon:Host:Crews:0 carries Description, Mounts, Name, Path, Profile. Did you mean Orkeon:Host:Crews:0:Name? `orkeon settings Orkeon:Host` lists its keys."],
            Problems("""{ "Orkeon": { "Host": { "Crews": [ { "Nme": "veille" } ] } } }"""));
        Assert.Equal(
            ["Orkeon:Tools:Email:Accounts:perso:Rights is 'Read, Everything', which is none of None, Read, Organize, Draft, Send, Delete, Purge."],
            Problems("""{ "Orkeon": { "Tools": { "Email": { "Accounts": { "perso": { "Rights": "Read, Everything" } } } } } }"""));
    }

    [Fact]
    public void A_retired_key_is_refused_with_its_migration()
    {
        var problem = Assert.Single(Problems("""{ "Memory": { "Provider": "sqlite", "ConnectionString": "Data Source=memory.db" } }"""));

        Assert.StartsWith("Memory:ConnectionString is not a setting any more: ", problem, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Sqlite:ConnectionString", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "RateLimiting": 5 }""", "RateLimiting is a section, written as a single value: it carries AgentRequestsPerMinute, GlobalRequestsPerMinute, MaxConcurrentRequests, ProviderRequestsPerMinute, QueueLimit.")]
    [InlineData("""{ "RateLimiting": { "QueueLimit": { "Value": 5 } } }""", "RateLimiting:QueueLimit is an integer, written as a section.")]
    [InlineData("""{ "RateLimiting": { "QueueLimit": [5] } }""", "RateLimiting:QueueLimit is an integer, written as a list.")]
    [InlineData("""{ "Orkeon": { "FileSystem": { "Mounts": "/data:/docs:ro" } } }""", "Orkeon:FileSystem:Mounts is a list of string, written as a single value: write [ … ].")]
    public void A_value_of_another_shape_than_its_key_is_refused(string text, string expected) =>
        Assert.Equal([expected], Problems(text));

    [Fact]
    public void A_file_writes_only_sections_orkeon_reads_and_a_block_may_show_one_beside_something_else()
    {
        const string Text = """{ "_comment": "copy me", "$schema": "x", "MonTruc": { "Cle": 1 }, "RateLimitng": { "QueueLimit": 5 } }""";

        Assert.Equal(
            ["MonTruc is no section of the settings catalogue.", "RateLimitng is no section of the settings catalogue. Did you mean RateLimiting?"],
            Problems(Text));
        Assert.Empty(Problems(Text, openRoot: true));
    }

    [Fact]
    public void A_text_is_read_as_the_settings_readers_read_it_and_a_fragment_as_the_object_holding_it()
    {
        Assert.Empty(Problems("""
            {
              // The calls to the model.
              "RateLimiting": { "QueueLimit": 5, },
            }
            """));
        Assert.Equal(
            ["RateLimiting:QueueLimt is not a setting: RateLimiting carries AgentRequestsPerMinute, GlobalRequestsPerMinute, MaxConcurrentRequests, ProviderRequestsPerMinute, QueueLimit. Did you mean RateLimiting:QueueLimit? `orkeon settings RateLimiting` lists its keys."],
            Problems("""
                "RateLimiting": { "QueueLimt": 5 }
                """));

        Assert.Null(SettingsFileGuard.Read("[1, 2]"));
        Assert.Null(SettingsFileGuard.Read("{ \"RateLimiting\": { … } }"));
    }

    [Fact]
    public void A_block_is_a_settings_text_when_its_root_names_a_section_as_the_catalogue_spells_it()
    {
        static bool Known(string text)
        {
            using var settings = JsonDocument.Parse(text);
            return SettingsFileGuard.Complete.WritesAKnownSection(settings.RootElement);
        }

        Assert.True(Known("""{ "RateLimiting": {} }"""));
        Assert.True(Known("""{ "name": "x", "Orkeon": {} }"""));
        // A crew, a tool call or an event carry their own keys: `llm`, `memory`, `security`.
        Assert.False(Known("""{ "llm": { "profile": "fast" }, "memory": true }"""));
        Assert.False(Known("""{ "check": "appsettings", "status": "ok" }"""));
    }
}
