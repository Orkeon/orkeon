using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Decision;
using Orkeon.Infrastructure.LLMs.Decision.Stages;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

public class DecisionProfileRegistrationTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>("Orkeon:Decision:Profiles:" + v.Key, v.Value)))
            .Build();

    private static readonly (string, string)[] Triage =
    [
        ("triage:Target", "cheap"),
        ("triage:Stages:0:Kind", "Route"),
        ("triage:Stages:0:Routes:cheap", "Small talk"),
        ("triage:Stages:0:Routes:strong", "Mathematics"),
        ("triage:Stages:0:MinConfidence", "0.5"),
        ("triage:Stages:1:Kind", "verify"),
        ("triage:Stages:1:EscalateTo", "strong"),
    ];

    [Fact]
    public async Task ShouldRouteThroughTheRegistry_WhenADecisionProfileIsConfigured()
    {
        var cheap = new StubLlmProvider { Name = "cheap" }.RespondToChatWith(new LlmResponse { Content = "cheap" });
        var strong = new StubLlmProvider { Name = "strong" }.RespondToChatWith(new LlmResponse { Content = "strong" });
        var model = new FakeDecisionModel
        {
            Respond = r => new DecisionResponse
            {
                Model = "jev",
                Answers = r.Questions.ContainsKey(RouteDecisionStage.Question)
                    ? ImmutableDictionary<string, DecisionAnswer>.Empty.Add(RouteDecisionStage.Question, new ChoiceAnswer
                    {
                        Choice = "strong",
                        Confidence = 0.9,
                        Probabilities = ImmutableDictionary<string, double>.Empty,
                    })
                    : ImmutableDictionary<string, DecisionAnswer>.Empty.Add(VerifyDecisionStage.Question, new NoulAnswer { Noul = 0.9 }),
            },
        };
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDecisionModel>(model)
            .AddOrkeonLlmProfile("cheap", _ => cheap)
            .AddOrkeonLlmProfile("strong", _ => strong)
            .AddOrkeonDecisionProfiles(Configuration(Triage));
        await using var container = services.BuildServiceProvider();

        var triage = container.GetRequiredService<ILlmProfileRegistry>().Resolve("triage");
        var response = await triage.Provider.ChatAsync([LlmMessage.User("Integrate x^2")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("strong", response.Content);
        Assert.Empty(cheap.ChatCalls);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task ShouldNameTheMissingProfile_WhenARouteIsNotDefined()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDecisionModel>(new FakeDecisionModel())
            .AddOrkeonLlmProfile("cheap", _ => new StubLlmProvider())
            .AddOrkeonDecisionProfiles(Configuration(Triage));
        await using var container = services.BuildServiceProvider();

        var ex = Assert.ThrowsAny<Exception>(() => container.GetRequiredService<ILlmProfileRegistry>().Resolve("triage").Provider);

        Assert.Contains("'strong' is not defined", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAskForTheDecisionModel_WhenNoneIsRegistered()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddOrkeonLlmProfile("cheap", _ => new StubLlmProvider())
            .AddOrkeonLlmProfile("strong", _ => new StubLlmProvider())
            .AddOrkeonDecisionProfiles(Configuration(Triage));
        await using var container = services.BuildServiceProvider();

        var ex = Assert.ThrowsAny<Exception>(() => container.GetRequiredService<ILlmProfileRegistry>().Resolve("triage").Provider);

        Assert.Contains("AddOrkeonTypeSafe", ex.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("triage:Stages:0:Kind", "summarise", "is not a stage")]
    [InlineData("triage:Target", "other", "is a decision profile")]
    [InlineData("triage:Stages:0:MinConfidence", "1.5", "is not a probability")]
    [InlineData("triage:Stages:1:EscalateTo", "", "a profile name is required")]
    public void ShouldFailTheBuildWithTheKey_WhenTheSectionIsInvalid(string key, string value, string expected)
    {
        var values = Triage.Where(v => v.Item1 != key).Append((key, value))
            .Append(("other:Target", "cheap")).Append(("other:Stages:0:Kind", "verify")).Append(("other:Stages:0:EscalateTo", "strong"))
            .ToArray();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOrkeonDecisionProfiles(Configuration(values)));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.StartsWith("Orkeon:Decision:Profiles:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRegisterNothingMore_WhenAddOrkeonDecisionProfilesIsCalledTwice()
    {
        var configuration = Configuration(Triage);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDecisionModel>(new FakeDecisionModel())
            .AddOrkeonLlmProfile("cheap", _ => new StubLlmProvider { Name = "cheap" })
            .AddOrkeonLlmProfile("strong", _ => new StubLlmProvider { Name = "strong" })
            .AddOrkeonDecisionProfiles(configuration);
        var once = services.Count;

        services.AddOrkeonDecisionProfiles(configuration);

        Assert.Equal(once, services.Count);
        await using var container = services.BuildServiceProvider();
        Assert.NotNull(container.GetRequiredService<ILlmProfileRegistry>().Resolve("triage").Provider);
    }
}
