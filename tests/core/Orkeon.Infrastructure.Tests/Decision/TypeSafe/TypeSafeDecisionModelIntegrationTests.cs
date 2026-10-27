using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Decision.DTOs;
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
}
