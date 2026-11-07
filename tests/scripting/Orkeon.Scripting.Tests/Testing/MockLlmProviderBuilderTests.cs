using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Testing;

namespace Orkeon.Scripting.Tests.Testing;

/// <summary>The public <c>When(string|Regex).Respond(...)</c> builder of the mocks, for .NET tests.</summary>
public sealed partial class MockLlmProviderBuilderTests
{
    [GeneratedRegex(@"^plan \d+$")]
    private static partial Regex PlanPattern();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsPattern();

    [Fact]
    public async Task When_text_responds_to_prompts_containing_it()
    {
        var provider = new MockLlmProvider();
        provider.When("weather").Respond("sunny");

        var hit = await provider.GenerateAsync("What is the WEATHER?", null, TestContext.Current.CancellationToken);
        var miss = await provider.GenerateAsync("hello", null, TestContext.Current.CancellationToken);

        Assert.Equal("sunny", hit.Content);
        Assert.Equal(string.Empty, miss.Content);
    }

    [Fact]
    public async Task When_regex_responds_to_the_last_user_message_of_a_chat()
    {
        var provider = new MockLlmProvider();
        provider.When(PlanPattern()).Respond("planned");

        var response = await provider.ChatAsync(
            [new LlmMessage { Role = "system", Content = "x" }, new LlmMessage { Role = "user", Content = "plan 7" }],
            null, TestContext.Current.CancellationToken);

        Assert.Equal("planned", response.Content);
    }

    [Fact]
    public async Task Respond_returns_the_provider_and_the_first_expectation_wins()
    {
        var provider = new MockLlmProvider()
            .When("a").Respond("first")
            .When("a").Respond("second");

        var response = await provider.GenerateAsync("a", null, TestContext.Current.CancellationToken);

        Assert.Equal("first", response.Content);
    }

    [Fact]
    public async Task Mock_tool_when_text_answers_calls_with_a_matching_parameter()
    {
        var tool = new MockTool("lookup") { Response = "default" };
        tool.When("paris").Respond("sunny");

        var hit = await tool.CallAsync(new ToolCallRequest("lookup", new Dictionary<string, object?> { ["city"] = "Paris" }), TestContext.Current.CancellationToken);
        var miss = await tool.CallAsync(new ToolCallRequest("lookup", new Dictionary<string, object?> { ["city"] = "Oslo" }), TestContext.Current.CancellationToken);

        Assert.Equal("sunny", hit.Result);
        Assert.Equal("default", miss.Result);
    }

    [Fact]
    public async Task Mock_tool_when_regex_answers_calls_with_a_matching_parameter()
    {
        var tool = new MockTool("lookup");
        tool.When(DigitsPattern()).Respond(42);

        var hit = await tool.CallAsync(new ToolCallRequest("lookup", new Dictionary<string, object?> { ["id"] = "17" }), TestContext.Current.CancellationToken);

        Assert.Equal(42, hit.Result);
    }
}
