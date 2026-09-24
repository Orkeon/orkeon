using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// STUDIO-29: the vendor's charge crosses the adapter. The provider leaves it in the response
/// metadata, which a <see cref="ChatResponse"/> does not carry — so OpenRouter's real cost was
/// read, stored, and dropped here, before the agent loop that reports usage ever saw it.
/// </summary>
public sealed class LlmProviderToChatClientAdapterCostTests
{
    private static readonly ChatMessage[] OneUserMessage = [new(ChatRole.User, "hi")];

    private static LlmResponse Answer(string content, Dictionary<string, object>? metadata = null, string? raw = null)
        => new()
        {
            Content = content,
            TokensUsed = 150,
            PromptTokens = 120,
            CompletionTokens = 30,
            Model = "google/gemini-3.7-flash",
            Metadata = metadata ?? [],
            RawResponseBody = raw,
        };

    private static Dictionary<string, object> Billed(double cost) =>
        new() { [LlmUsageMetadataKeys.Cost] = cost, [LlmUsageMetadataKeys.CostCurrency] = "USD" };

    [Fact]
    public async Task The_vendor_charge_rides_on_the_response_with_its_currency()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer("ok", Billed(0.0021)));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var response = await adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0.0021m, Assert.IsType<decimal>(response.AdditionalProperties![LlmUsageMetadataKeys.Cost]));
        Assert.Equal("USD", response.AdditionalProperties[LlmUsageMetadataKeys.CostCurrency]);
        Assert.Equal(120, response.Usage!.InputTokenCount);
    }

    [Fact]
    public async Task A_free_call_crosses_as_zero_not_as_nothing()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer("ok", Billed(0.0)));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var response = await adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0m, Assert.IsType<decimal>(response.AdditionalProperties![LlmUsageMetadataKeys.Cost]));
    }

    [Fact]
    public async Task A_call_the_vendor_did_not_bill_carries_no_cost()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer("ok"));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var response = await adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(response.AdditionalProperties?.ContainsKey(LlmUsageMetadataKeys.Cost) ?? false);
        Assert.False(response.AdditionalProperties?.ContainsKey(LlmUsageMetadataKeys.CostCurrency) ?? false);
    }

    [Fact]
    public async Task A_tool_call_answer_carries_its_charge_too()
    {
        // The turn that calls a tool is billed like any other, and it is most of an agent's turns.
        const string toolCallBody =
            """{"choices":[{"message":{"role":"assistant","content":"","tool_calls":[{"id":"c1","type":"function","function":{"name":"probe","arguments":"{}"}}]}}]}""";
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer("", Billed(0.0008), toolCallBody));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var response = await adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal(0.0008m, Assert.IsType<decimal>(response.AdditionalProperties![LlmUsageMetadataKeys.Cost]));
    }

    [Fact]
    public async Task The_streamed_answer_carries_the_same_charge_as_the_buffered_one()
    {
        // The streaming fallback hands the whole answer over as one update: that update is
        // what a consumer folds into its response, so it carries what the answer cost.
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer("full answer", Billed(0.0021)));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter.GetStreamingResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);

        var folded = updates.ToChatResponse();
        Assert.Equal("full answer", folded.Text);
        Assert.Equal(0.0021m, Assert.IsType<decimal>(folded.AdditionalProperties![LlmUsageMetadataKeys.Cost]));
        Assert.Equal("USD", folded.AdditionalProperties[LlmUsageMetadataKeys.CostCurrency]);
    }
}
