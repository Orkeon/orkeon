using System.Collections.Immutable;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.Decision.SystemOne;

namespace Orkeon.Infrastructure.Tests.Decision.SystemOne;

/// <summary>
/// Against the real APIs: a hosted provider is skipped unless its key variable is set in the
/// process; a local Kev is skipped unless something answers at its address.
/// </summary>
[Trait("Category", "Integration")]
public class SystemOneDecisionModelIntegrationTests
{
    private static SystemOneDecisionModel Create(
        IServiceProvider services,
        DecisionProviderDescriptor provider,
        Action<SystemOneProviderOptions>? configure = null)
    {
        var options = new SystemOneProviderOptions();
        provider.ApplyDefaults(options);
        configure?.Invoke(options);
        return new SystemOneDecisionModel(
            provider,
            services.GetRequiredService<IHttpClientFactory>(),
            Options.Create(options),
            NullLogger<SystemOneDecisionModel>.Instance);
    }

    private static DecisionRequest RefundAndTopic() => new()
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

    [Theory]
    [Trait("BR", "UC-03")]
    [InlineData("typesafe", "TYPESAFE_API_KEY")]
    [InlineData("liquid", "LIQUID_API_KEY")]
    public async Task ShouldAnswerTypedNoulAndChoice_WhenTheRealApiIsCalled(string provider, string variable)
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)), $"{variable} is not set.");
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var model = Create(services, DecisionProviderDescriptor.Find(provider)!);

        var response = await model.DecideAsync(RefundAndTopic(), TestContext.Current.CancellationToken);

        Assert.InRange(response.Get<NoulAnswer>("refund").Noul, 0.0, 1.0);
        Assert.True(response.Get<ChoiceAnswer>("topic").Choice is "delivery" or "billing");
        Assert.True(response.Usage.InputTokens > 0);
    }

    [Fact]
    [Trait("BR", "UC-03")]
    public async Task ShouldAnswerTypedChoice_WhenALocalKevIsCalled()
    {
        var address = new Uri(Environment.GetEnvironmentVariable("KEV_BASE_URL") is { Length: > 0 } configured
            ? configured
            : DecisionProviderDefaults.KevBaseUrl);
        Assert.SkipUnless(await Answers(address), $"Nothing answers at {address}.");
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var model = Create(services, DecisionProviderDescriptor.Kev, o => o.BaseUrl = address);

        var response = await model.DecideAsync(RefundAndTopic() with
        {
            Questions = RefundAndTopic().Questions.Remove("refund"),
        }, TestContext.Current.CancellationToken);

        Assert.True(response.Get<ChoiceAnswer>("topic").Choice is "delivery" or "billing");
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
        var model = Create(services, DecisionProviderDescriptor.TypeSafe, o => o.MaxRetries = 0);
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

    private static async Task<bool> Answers(Uri address)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await client.ConnectAsync(address.Host, address.Port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}
