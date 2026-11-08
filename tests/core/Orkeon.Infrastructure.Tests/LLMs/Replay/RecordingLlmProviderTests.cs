using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Replay;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Infrastructure.Tests.LLMs.Replay.CassetteFixtures;

namespace Orkeon.Infrastructure.Tests.LLMs.Replay;

public sealed class RecordingLlmProviderTests
{
    [Fact]
    public async Task A_chat_call_is_answered_by_the_inner_provider_and_written_to_the_cassette()
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "pong", Model = "m1", PromptTokens = 3, CompletionTokens = 1 } };
        var provider = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");

        var response = await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", response.Content);
        Assert.Equal(1, inner.Calls);
        var line = Assert.Single(Lines(await ReadAsync(fs)));
        Assert.Contains("\"content\":\"pong\"", line, StringComparison.Ordinal);
        Assert.Contains("\"profile\":\"default\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_key_of_a_call_is_stable_across_runs()
    {
        var keys = new List<string>();
        for (var run = 0; run < 2; run++)
        {
            var fs = NewFileSystem();
            var provider = new RecordingLlmProvider(new FakeScriptedLlmProvider(), new LlmCassette(fs, CassettePath), "default");
            await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);
            keys.Add(Lines(await ReadAsync(fs))[0]);
        }

        Assert.Equal(keys[0], keys[1]);
        Assert.Matches("\"key\":\"[0-9a-f]{64}\"", keys[0]);
    }

    [Fact]
    public void A_timestamp_or_an_injected_identifier_does_not_change_the_key()
    {
        var first = LlmCallKey.Of("p", User("now 2026-11-08T10:11:12Z run 3f2504e0-4f89-11d3-9a0c-0305e82c3301 go"), null);
        var second = LlmCallKey.Of("p", User("now 2027-01-02T01:02:03.456+01:00 run 00000000-0000-0000-0000-000000000001 go"), null);
        var other = LlmCallKey.Of("p", User("now 2026-11-08T10:11:12Z run 3f2504e0-4f89-11d3-9a0c-0305e82c3301 stop"), null);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void The_profile_the_tools_and_the_format_are_part_of_the_key()
    {
        var messages = User("ping");
        var plain = LlmCallKey.Of("a", messages, null);
        var tools = LlmConfig.OnProfile() with { Tools = [new Orkeon.Domain.Tools.Protocol.ToolSchema("lookup", "finds", new Dictionary<string, Orkeon.Domain.Tools.Protocol.ParameterSchema>())] };
        var json = LlmConfig.OnProfile() with { ResponseFormat = LlmResponseFormat.JsonObject() };

        Assert.NotEqual(plain, LlmCallKey.Of("b", messages, null));
        Assert.NotEqual(plain, LlmCallKey.Of("a", messages, tools));
        Assert.NotEqual(plain, LlmCallKey.Of("a", messages, json));
        Assert.Equal(plain, LlmCallKey.Of("a", messages, LlmConfig.OnProfile()));
    }

    [Fact]
    public async Task The_configured_key_and_authorization_values_never_reach_the_cassette()
    {
        var fs = NewFileSystem();
        // Built from pieces so that no secret-shaped literal sits in the source.
        var configured = string.Concat("plain", "-value-", "9999");
        var vendor = string.Concat("s", "k-live-", "0123456789abcdef0123");
        var bearer = string.Concat("Bear", "er ", "abcdefghijklmnop1234");
        var inner = new FakeScriptedLlmProvider
        {
            BaseConfig = LlmConfig.OnProfile() with { ApiKey = configured },
            Response = new LlmResponse
            {
                Content = "echo " + configured + " and " + bearer,
                RawResponseBody = "{\"k\":\"" + vendor + "\"}",
            },
        };
        var provider = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");

        await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        var text = await ReadAsync(fs);
        Assert.DoesNotContain(configured, text, StringComparison.Ordinal);
        Assert.DoesNotContain(vendor, text, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop1234", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_streamed_call_is_recorded_as_its_list_of_deltas()
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider
        {
            StreamEvents =
            [
                LlmStreamEvent.Content("po"),
                LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, "c1", "lookup", "{\"q\":")),
                LlmStreamEvent.Content("ng"),
                LlmStreamEvent.Complete(new LlmResponse { Content = "pong" }),
            ],
        };
        var provider = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");

        var seen = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
            seen.Add(ev);

        Assert.Equal(4, seen.Count);
        var line = Assert.Single(Lines(await ReadAsync(fs)));
        Assert.Contains("\"deltas\"", line, StringComparison.Ordinal);
        Assert.Contains("lookup", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identical_calls_are_numbered_in_order()
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider();
        var n = 0;
        inner.OnCall = () => inner.Response = new LlmResponse { Content = "answer " + n++ };
        var provider = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");

        await provider.ChatAsync(User("same"), null, TestContext.Current.CancellationToken);
        await provider.ChatAsync(User("same"), null, TestContext.Current.CancellationToken);

        var lines = Lines(await ReadAsync(fs));
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"occurrence\":0", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"occurrence\":1", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_run_starts_a_fresh_cassette()
    {
        var fs = NewFileSystem();
        for (var run = 0; run < 2; run++)
        {
            var provider = new RecordingLlmProvider(new FakeScriptedLlmProvider(), new LlmCassette(fs, CassettePath), "default");
            await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);
        }

        Assert.Single(Lines(await ReadAsync(fs)));
    }

    private static async Task<string> RecordedTextAsync(string content, string? rawBody = null)
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = content, RawResponseBody = rawBody } };
        var provider = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");

        await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        return await ReadAsync(fs);
    }

    [Fact]
    public async Task A_google_key_never_reaches_the_cassette()
    {
        // Built from pieces so that no secret-shaped literal sits in the source.
        var google = string.Concat("AI", "za", "Sy", "A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q");

        var text = await RecordedTextAsync("the key is " + google + " ok", "{\"k\":\"" + google + "\"}");

        Assert.DoesNotContain(google, text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_credential_in_the_query_of_a_url_is_redacted_and_the_other_parameters_stay()
    {
        var url = "https://api.example.com/v1/chat?alpha=1&key=" + string.Concat("q", "uery-one-11")
            + "&api_key=" + string.Concat("q", "uery-two-22") + "&token=" + string.Concat("q", "uery-three-33") + "&beta=2";

        var text = await RecordedTextAsync("called " + url);

        Assert.DoesNotContain("query-one-11", text, StringComparison.Ordinal);
        Assert.DoesNotContain("query-two-22", text, StringComparison.Ordinal);
        Assert.DoesNotContain("query-three-33", text, StringComparison.Ordinal);
        Assert.Contains("alpha=1", text, StringComparison.Ordinal);
        Assert.Contains("beta=2", text, StringComparison.Ordinal);
        Assert.Contains("key=[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_info_in_a_url_is_redacted()
    {
        var withPassword = "https://" + string.Concat("ali", "ce:pw-0123456") + "@host.example/path";
        var tokenOnly = "http://" + string.Concat("tok", "-abcdef123456") + "@other.example/";

        var text = await RecordedTextAsync("a " + withPassword + " b " + tokenOnly);

        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pw-0123456", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-abcdef123456", text, StringComparison.Ordinal);
        Assert.Contains("https://[REDACTED]@host.example/path", text, StringComparison.Ordinal);
        Assert.Contains("http://[REDACTED]@other.example/", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_url_and_an_e_mail_address_are_left_alone()
    {
        var text = await RecordedTextAsync("see https://docs.example.com/page?lang=en and write to team@example.com");

        Assert.Contains("https://docs.example.com/page?lang=en", text, StringComparison.Ordinal);
        Assert.Contains("team@example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[REDACTED]", text, StringComparison.Ordinal);
    }
}
