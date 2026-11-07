using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Mock;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.LLMs.Mock;

public sealed class JsonlMockLlmProviderTests
{
    private const string Path = "/llm-mock/answers.jsonl";

    private static JsonlMockLlmProvider Provider(params string[] lines)
    {
        var fs = new FakeFileSystemService().AddMount("/llm-mock").AddFile(Path, string.Join('\n', lines));
        return new JsonlMockLlmProvider(fs, Path);
    }

    private static LlmMessage[] User(string text) => [new LlmMessage { Role = "user", Content = text }];

    [Fact]
    public async Task Literal_match_is_case_insensitive_and_served()
    {
        var provider = Provider("""{"match":"Weather","content":"sunny"}""");

        var response = await provider.ChatAsync(User("what is the WEATHER today"), null, TestContext.Current.CancellationToken);

        Assert.Equal("sunny", response.Content);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task Regex_match_is_served_when_regex_is_true()
    {
        var provider = Provider("""{"match":"^plan \\d+$","regex":true,"content":"planned"}""");

        var hit = await provider.ChatAsync(User("plan 42"), null, TestContext.Current.CancellationToken);
        var miss = await provider.ChatAsync(User("plan x"), null, TestContext.Current.CancellationToken);

        Assert.Equal("planned", hit.Content);
        Assert.NotNull(miss.Error);
    }

    [Fact]
    public async Task First_matching_line_wins()
    {
        var provider = Provider(
            """{"match":"hello","content":"first"}""",
            """{"match":"hello","content":"second"}""");

        var response = await provider.GenerateAsync("hello", null, TestContext.Current.CancellationToken);

        Assert.Equal("first", response.Content);
    }

    [Fact]
    public async Task No_match_is_a_failed_call_naming_the_prompt()
    {
        var provider = Provider("""{"match":"hello","content":"hi"}""");

        var response = await provider.GenerateAsync("something else", null, TestContext.Current.CancellationToken);

        Assert.Contains("something else", response.Error);
    }

    [Fact]
    public async Task Tool_call_is_replayed_until_the_tool_result_is_in_the_conversation()
    {
        var provider = Provider(
            """{"match":"weather","toolCalls":[{"name":"get_weather","arguments":{"city":"Paris"}}]}""",
            """{"match":"weather","content":"It is sunny in Paris."}""");

        var first = await provider.ChatAsync(User("weather in Paris?"), null, TestContext.Current.CancellationToken);

        Assert.NotNull(first.RawResponseBody);
        using var doc = JsonDocument.Parse(first.RawResponseBody!);
        var call = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0];
        Assert.Equal("get_weather", call.GetProperty("function").GetProperty("name").GetString());
        using var arguments = JsonDocument.Parse(call.GetProperty("function").GetProperty("arguments").GetString()!);
        Assert.Equal("Paris", arguments.RootElement.GetProperty("city").GetString());

        var afterTool = new[]
        {
            new LlmMessage { Role = "user", Content = "weather in Paris?" },
            new LlmMessage { Role = "assistant", Content = string.Empty },
            new LlmMessage { Role = "tool", Content = "{\"temp\":21}", ToolCallId = "x" },
        };
        var second = await provider.ChatAsync(afterTool, null, TestContext.Current.CancellationToken);

        Assert.Equal("It is sunny in Paris.", second.Content);
        Assert.Null(second.RawResponseBody);
    }

    [Fact]
    public async Task Streaming_is_one_content_chunk_then_the_completed_response()
    {
        var provider = Provider("""{"match":"hello","content":"hi there"}""");
        var events = new List<LlmStreamEvent>();

        await foreach (var e in ((IStreamingLlmProvider)provider).ChatStreamingAsync(User("hello"), null, TestContext.Current.CancellationToken))
            events.Add(e);

        Assert.Equal(2, events.Count);
        Assert.Equal("hi there", events[0].Delta);
        Assert.Equal(LlmStreamEventKind.Completed, events[1].Kind);
        Assert.Equal("hi there", events[1].FinalResponse!.Content);
    }

    [Fact]
    public async Task Missing_file_is_a_failed_call_naming_the_path()
    {
        var provider = new JsonlMockLlmProvider(new FakeFileSystemService().AddMount("/llm-mock"), Path);

        var response = await provider.GenerateAsync("hello", null, TestContext.Current.CancellationToken);

        Assert.Contains(Path, response.Error);
    }

    private static LlmMessage[] AfterToolRounds(string prompt, int rounds)
    {
        var messages = new List<LlmMessage> { new() { Role = "user", Content = prompt } };
        for (var i = 0; i < rounds; i++)
        {
            messages.Add(new LlmMessage { Role = "assistant", Content = string.Empty });
            messages.Add(new LlmMessage { Role = "tool", Content = "{}", ToolCallId = "c" + i });
        }

        return [.. messages];
    }

    private static string FirstToolName(LlmResponse response)
    {
        Assert.NotNull(response.RawResponseBody);
        using var doc = JsonDocument.Parse(response.RawResponseBody!);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("tool_calls")[0]
            .GetProperty("function").GetProperty("name").GetString()!;
    }

    [Fact]
    public void Provider_declares_its_capabilities_itself_rather_than_reporting_unknown()
    {
        Orkeon.Domain.SharedKernel.ILlmProvider provider = Provider("""{"match":"x","content":"y"}""");

        Assert.NotSame(LlmProviderCapabilities.Unknown, provider.Capabilities);
        Assert.False(provider.Capabilities.ReplaysPrompt);
        Assert.False(provider.Capabilities.RunsOwnTools);
    }

    [Fact]
    public async Task Streaming_a_prompt_no_line_fits_throws_instead_of_yielding_an_empty_answer()
    {
        var provider = Provider("""{"match":"hello","content":"hi"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("something else", null, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Contains("something else", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_with_a_missing_file_throws_naming_the_path()
    {
        var provider = new JsonlMockLlmProvider(new FakeFileSystemService().AddMount("/llm-mock"), Path);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("hello", null, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Contains(Path, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Successive_tool_calls_are_scripted_one_matching_line_per_round_of_tool_results()
    {
        var provider = Provider(
            """{"match":"plan","toolCalls":[{"name":"first_tool","arguments":{}}]}""",
            """{"match":"plan","toolCalls":[{"name":"second_tool","arguments":{}}]}""",
            """{"match":"plan","content":"All done."}""");

        var round0 = await provider.ChatAsync(AfterToolRounds("plan it", 0), null, TestContext.Current.CancellationToken);
        var round1 = await provider.ChatAsync(AfterToolRounds("plan it", 1), null, TestContext.Current.CancellationToken);
        var round2 = await provider.ChatAsync(AfterToolRounds("plan it", 2), null, TestContext.Current.CancellationToken);

        Assert.Equal("first_tool", FirstToolName(round0));
        Assert.Equal("second_tool", FirstToolName(round1));
        Assert.Equal("All done.", round2.Content);
        Assert.Null(round2.RawResponseBody);
    }

    [Fact]
    public async Task A_script_that_runs_out_of_lines_fails_instead_of_asking_for_the_tool_again()
    {
        var provider = Provider("""{"match":"plan","toolCalls":[{"name":"first_tool","arguments":{}}]}""");

        var response = await provider.ChatAsync(AfterToolRounds("plan it", 3), null, TestContext.Current.CancellationToken);

        Assert.NotNull(response.Error);
        Assert.Null(response.RawResponseBody);
    }

    [Fact]
    public async Task A_last_content_line_keeps_answering_after_more_rounds_than_lines()
    {
        var provider = Provider("""{"match":"plan","content":"Always this."}""");

        var response = await provider.ChatAsync(AfterToolRounds("plan it", 2), null, TestContext.Current.CancellationToken);

        Assert.Equal("Always this.", response.Content);
    }

    [Fact]
    public async Task A_regex_that_times_out_is_a_failed_call_naming_the_line()
    {
        var provider = Provider("""{"match":"(a+)+$","regex":true,"content":"never"}""");

        var response = await provider.GenerateAsync(new string('a', 48) + "!", null, TestContext.Current.CancellationToken);

        Assert.Contains("timed out", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("line 1", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_line_is_a_failed_call_naming_its_line_number()
    {
        var provider = Provider(
            """{"match":"hello","content":"hi"}""",
            "this is not json");

        var response = await provider.GenerateAsync("hello", null, TestContext.Current.CancellationToken);

        Assert.Contains("line 2", response.Error, StringComparison.Ordinal);
    }
}
