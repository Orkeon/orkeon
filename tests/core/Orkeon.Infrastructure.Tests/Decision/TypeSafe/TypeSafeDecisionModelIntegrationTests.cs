using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.Decision.TypeSafe;

namespace Orkeon.Infrastructure.Tests.Decision.TypeSafe;

/// <summary>
/// Against the real TypeSafe API: skipped unless <c>TYPESAFE_API_KEY</c> is set in the process.
/// </summary>
[Trait("Category", "Integration")]
public class TypeSafeDecisionModelIntegrationTests
{
    [Fact]
    public async Task ShouldAnswerTypedNoulAndChoice_WhenTheRealApiIsCalled()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")),
            "TYPESAFE_API_KEY is not set.");
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var model = new TypeSafeDecisionModel(
            services.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new TypeSafeOptions()),
            NullLogger<TypeSafeDecisionModel>.Instance);
        var request = new DecisionRequest
        {
            State = JsonValue.Create("Customer: my parcel never arrived and I want my money back."),
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty
                .Add("refund", new NoulQuestion())
                .Add("topic", new ChoiceQuestion
                {
                    Criteria = ImmutableDictionary<string, JsonNode?>.Empty
                        .Add("delivery", JsonValue.Create("A delivery problem"))
                        .Add("billing", JsonValue.Create("A billing question")),
                }),
        };

        var response = await model.DecideAsync(request, TestContext.Current.CancellationToken);

        Assert.InRange(response.Get<NoulAnswer>("refund").Noul, 0.0, 1.0);
        Assert.True(response.Get<ChoiceAnswer>("topic").Choice is "delivery" or "billing");
        Assert.True(response.Usage.InputTokens > 0);
    }

    /// <summary>
    /// Pins the wording of the refusal of a state over the 64k-token context: the adapter looks
    /// for <c>max_tokens_exceeded</c> in the 400 body to name the limit, a code TypeSafe's OpenAPI
    /// does not enumerate. A run against the real API that fails here says the wording moved.
    /// </summary>
    [Fact]
    public async Task ShouldNameTheContextLimit_WhenTheRealApiRefusesAnOversizedState()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")),
            "TYPESAFE_API_KEY is not set.");
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var model = new TypeSafeDecisionModel(
            services.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new TypeSafeOptions { MaxRetries = 0 }),
            NullLogger<TypeSafeDecisionModel>.Instance);
        // About 100k tokens of varied words: over the 64k per-request context, under long context.
        var words = new[] { "parcel", "invoice", "refund", "delivery", "account", "support", "ticket", "order" };
        var state = string.Join(' ', Enumerable.Range(0, 100_000).Select(i => words[i % words.Length] + i));
        var request = new DecisionRequest
        {
            State = JsonValue.Create(state),
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add("refund", new NoulQuestion()),
        };

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => model.DecideAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.IsTransient);
        Assert.Contains($"context limit ({DecisionLimits.StandardContextTokens} tokens)", ex.Message, StringComparison.Ordinal);
    }
}
