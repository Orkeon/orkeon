using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.LLMs.Replay;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Infrastructure.Tests.LLMs.Replay.CassetteFixtures;

namespace Orkeon.Infrastructure.Tests.LLMs.Replay;

/// <summary>FWK final review L1, L2, L6 and F11: what a cassette redacts, keys and records.</summary>
public sealed class LlmCassetteHardeningTests
{
    private static ReplayLlmProvider Replaying(FakeFileSystemService fs) =>
        new(new FakeScriptedLlmProvider(), new LlmCassette(fs, CassettePath), "default");

    [Fact]
    public async Task A_query_credential_followed_by_an_escaped_quote_leaves_a_cassette_that_still_loads()
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "see http://x.example/p?key=abc\\\"}" } };
        await new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default")
            .ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        var replayed = await Replaying(fs).ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        Assert.Contains("key=[REDACTED]", replayed.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", replayed.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_split_across_streamed_deltas_never_lands_in_clear_in_the_cassette()
    {
        // FWK review LLM-2: redacted delta by delta, a key cut in two by the stream matched nowhere.
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider
        {
            BaseConfig = LlmConfig.OnProfile() with { Model = "m", ApiKey = "configured-secret-0123" },
            StreamEvents =
            [
                LlmStreamEvent.Content("echo configured-sec"),
                LlmStreamEvent.Content("ret-0123 and sk-abcdefgh"),
                LlmStreamEvent.Content("ijklmnopqrst done"),
                LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, "c1", "fetch", "{\"url\":\"http://x.example/?api_")),
                LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, null, null, "key=hunter2hunter2\"}")),
                LlmStreamEvent.Complete(new LlmResponse { Content = "echo [REDACTED] and [REDACTED] done" }),
            ],
        };
        var recorder = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");
        await foreach (var unused in recorder.ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
        {
        }

        var cassette = await ReadAsync(fs);
        Assert.DoesNotContain("configured-sec", cassette, StringComparison.Ordinal);
        Assert.DoesNotContain("ret-0123", cassette, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefgh", cassette, StringComparison.Ordinal);
        Assert.DoesNotContain("ijklmnopqrst", cassette, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", cassette, StringComparison.Ordinal);

        var text = new System.Text.StringBuilder();
        var arguments = new System.Text.StringBuilder();
        await foreach (var ev in Replaying(fs).ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
        {
            if (ev.Kind == LlmStreamEventKind.ContentDelta)
                text.Append(ev.Delta);
            if (ev.ToolCall is { } call)
                arguments.Append(call.ArgumentsDelta);
        }

        Assert.Equal("echo [REDACTED] and [REDACTED] done", text.ToString());
        Assert.Equal("{\"url\":\"http://x.example/?api_key=[REDACTED]\"}", arguments.ToString());
    }

    public static TheoryData<string> ChangedOptions => new()
    {
        "systemMessage", "toolMode", "topP", "seed", "stop", "frequencyPenalty", "presencePenalty", "custom", "grammar",
    };

    private static LlmConfig Changed(LlmConfig config, string option) => option switch
    {
        "systemMessage" => config with { SystemMessage = "You are a pirate." },
        "toolMode" => config with { ToolMode = Orkeon.Domain.Tools.Protocol.ToolCallMode.Required },
        "topP" => config with { TopP = 0.5 },
        "seed" => config with { Seed = 7 },
        "stop" => config with { StopSequences = ["END"] },
        "frequencyPenalty" => config with { FrequencyPenalty = 0.4 },
        "presencePenalty" => config with { PresencePenalty = 0.4 },
        "custom" => config with { CustomParameters = new Dictionary<string, object> { ["system_message"] = "Be terse." } },
        "grammar" => config with { GrammarEnabled = true, GrammarGbnf = "root ::= \"yes\"" },
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

    [Theory]
    [MemberData(nameof(ChangedOptions))]
    public async Task A_call_whose_configuration_changed_what_the_model_answers_is_a_replay_miss(string option)
    {
        // FWK review LLM-3: a backstory edit (the system message) or another tool mode used to replay
        // the old answer.
        var fs = NewFileSystem();
        var recorded = LlmConfig.OnProfile() with { Model = "m", SystemMessage = "You are a clerk." };
        await new RecordingLlmProvider(new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "old" } }, new LlmCassette(fs, CassettePath), "default")
            .ChatAsync(User("ping"), recorded, TestContext.Current.CancellationToken);

        Assert.Equal("old", (await Replaying(fs).ChatAsync(User("ping"), recorded, TestContext.Current.CancellationToken)).Content);
        await Assert.ThrowsAsync<ReplayMissException>(
            () => Replaying(fs).ChatAsync(User("ping"), Changed(recorded, option), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_kebab_case_slug_is_not_taken_for_a_vendor_key()
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "open the desk-organisation-plan-2026 file" } };
        await new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default")
            .ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        var text = await ReadAsync(fs);

        Assert.Contains("desk-organisation-plan-2026", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redacted_answer_fed_back_in_the_next_turn_still_finds_its_recorded_answer()
    {
        // Built from pieces so that no secret-shaped literal sits in the source.
        var bearer = string.Concat("Bear", "er ", "abcdefghijklmnop1234");
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider();
        var recorder = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");
        inner.Response = new LlmResponse { Content = "the header is " + bearer };
        var first = await recorder.ChatAsync(User("turn one"), null, TestContext.Current.CancellationToken);
        inner.Response = new LlmResponse { Content = "turn two answer" };
        await recorder.ChatAsync(Turn2(first.Content), null, TestContext.Current.CancellationToken);

        var replay = Replaying(fs);
        var firstReplayed = await replay.ChatAsync(User("turn one"), null, TestContext.Current.CancellationToken);
        var second = await replay.ChatAsync(Turn2(firstReplayed.Content), null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("abcdefghijklmnop1234", firstReplayed.Content, StringComparison.Ordinal);
        Assert.Equal("turn two answer", second.Content);
    }

    private static LlmMessage[] Turn2(string assistantText) =>
    [
        new LlmMessage { Role = "user", Content = "turn one" },
        new LlmMessage { Role = "assistant", Content = assistantText },
        new LlmMessage { Role = "user", Content = "and then?" },
    ];

    [Fact]
    public void The_model_and_the_sampling_options_are_part_of_the_key()
    {
        var messages = User("ping");
        var plain = LlmCallKey.Of("a", messages, LlmConfig.Create("model-1"));

        Assert.NotEqual(plain, LlmCallKey.Of("a", messages, LlmConfig.Create("model-2")));
        Assert.NotEqual(plain, LlmCallKey.Of("a", messages, LlmConfig.Create("model-1") with { Temperature = 0.9 }));
        Assert.NotEqual(plain, LlmCallKey.Of("a", messages, LlmConfig.Create("model-1") with { MaxTokens = 33 }));
        Assert.Equal(plain, LlmCallKey.Of("a", messages, LlmConfig.Create("model-1")));
    }

    [Fact]
    public async Task A_failed_call_is_recorded_and_replayed_as_the_same_kind_of_failure()
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider
        {
            Throws = new HttpRequestException("the vendor is down", null, HttpStatusCode.ServiceUnavailable),
        };
        var recorder = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");
        await Assert.ThrowsAsync<HttpRequestException>(() => recorder.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken));
        inner.Throws = new TimeoutException("too slow");
        await Assert.ThrowsAsync<TimeoutException>(() => recorder.ChatAsync(User("pong"), null, TestContext.Current.CancellationToken));

        var replay = Replaying(fs);
        var http = await Assert.ThrowsAsync<HttpRequestException>(() => replay.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<TimeoutException>(() => replay.ChatAsync(User("pong"), null, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, http.StatusCode);
    }

    [Fact]
    public async Task A_stream_that_fails_is_replayed_with_its_deltas_and_then_the_failure()
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider
        {
            StreamEvents = [LlmStreamEvent.Content("par")],
            StreamThrowsAfter = new HttpRequestException("cut", null, HttpStatusCode.BadGateway),
        };
        var recorder = new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default");
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var unused in recorder.ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
            {
            }
        });

        var seen = new List<LlmStreamEvent>();
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var ev in Replaying(fs).ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
                seen.Add(ev);
        });

        Assert.Equal(HttpStatusCode.BadGateway, thrown.StatusCode);
        Assert.Equal("par", Assert.Single(seen).Delta);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The provider and its factory live as long as the test.")]
    public async Task A_replayed_call_with_audio_for_a_model_that_takes_none_is_refused_as_a_live_call_is()
    {
        var provider = new OpenAIProvider(
            LlmConfig.Create("gpt-4o", "sk-test"),
            new Orkeon.Infrastructure.Tests.Doubles.MockHttpClientFactory(),
            new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance),
            NullLogger<OpenAIProvider>.Instance);
        var spoken = new LlmMessage
        {
            Role = "user",
            Content = "listen",
            MultiModalContent = MultiModalContent.Empty().AddText("listen").AddAudio(AudioContentPart.FromBytes([1, 2, 3], "audio/wav")),
        };
        var replay = new ReplayLlmProvider(provider, new LlmCassette(NewFileSystem(), CassettePath), "default");

        var refusal = await Assert.ThrowsAsync<NotSupportedException>(
            () => replay.ChatAsync([spoken], null, TestContext.Current.CancellationToken));

        Assert.Contains("'gpt-4o'", refusal.Message, StringComparison.Ordinal);
    }
}
