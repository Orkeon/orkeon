using System.Collections.Immutable;
using System.Globalization;
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

    [Theory]
    [InlineData("triage:Stages:0:Kind", "summarise", "is not a stage")]
    [InlineData("triage:Target", "other", "is a decision profile")]
    [InlineData("triage:Stages:0:MinConfidence", "1.5", "is not a probability")]
    [InlineData("triage:Stages:1:EscalateTo", "", "a profile name is required")]
    [InlineData("triage:Stages:1:Steps", "some", "is not final or all")]
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

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldFailTheBuildNamingTheKeyToRename_WhenADecisionProfileIsNamedAuto()
    {
        // Arrange
        var values = Triage.Select(v => ("auto" + v.Item1["triage".Length..], v.Item2)).ToArray();

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOrkeonDecisionProfiles(Configuration(values)));

        // Assert
        Assert.StartsWith("Orkeon:Decision:Profiles:auto: 'auto' is a reserved profile name", ex.Message, StringComparison.Ordinal);
    }

    private static string BuildRefusal(params (string Key, string Value)[] values) =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOrkeonDecisionProfiles(Configuration(values))).Message;

    /// <summary>#454 — the binder dropped a stage it could not convert: the guard did not exist at run time.</summary>
    [Fact]
    [Trait("BR", "CH-454")]
    public void ShouldRefuseTheStage_WhenOneOfItsValuesCannotBeConverted()
    {
        var message = BuildRefusal(
            ("p:Target", "cheap"),
            ("p:Stages:0:Kind", "guard"), ("p:Stages:0:Risks:x", "y"), ("p:Stages:0:BlockThreshold", "x"),
            ("p:Stages:1:Kind", "guard"), ("p:Stages:1:Risks:x", "y"));

        Assert.Equal(
            "Failed to convert configuration value 'x' at 'Orkeon:Decision:Profiles:p:Stages:0:BlockThreshold' to type 'System.Double'.",
            message);
    }

    /// <summary>#454 — a FailClosed that is no boolean dropped the profile, or read as false: a fail-closed profile ran fail-open.</summary>
    [Fact]
    [Trait("BR", "CH-454")]
    public void ShouldRefuseTheProfile_WhenItsFailClosedIsNoBoolean()
    {
        var message = BuildRefusal(
            ("p:Target", "cheap"), ("p:FailClosed", "x"),
            ("p:Stages:0:Kind", "guard"), ("p:Stages:0:Risks:x", "y"));

        Assert.Equal(
            "Failed to convert configuration value 'x' at 'Orkeon:Decision:Profiles:p:FailClosed' to type 'System.Boolean'.",
            message);
    }

    /// <summary>#454 — a profile or a stage written as a single value was ignored.</summary>
    [Theory]
    [Trait("BR", "CH-454")]
    [InlineData("p", "Orkeon:Decision:Profiles:p is a single value where a decision profile is expected")]
    [InlineData("p:Stages:0", "Orkeon:Decision:Profiles:p:Stages:0 is a single value where a stage is expected")]
    [InlineData("p:Stages", "Orkeon:Decision:Profiles:p:Stages is a single value where a list of stages is expected")]
    public void ShouldRefuseASingleValue_WhereAProfileOrAStageIsExpected(string key, string expected)
    {
        var values = key == "p" ? [("p", "x")] : new[] { ("p:Target", "cheap"), (key, "x") };

        Assert.StartsWith(expected, BuildRefusal(values), StringComparison.Ordinal);
    }

    /// <summary>#455 — a stage's Phase and Steps are written by name: "1" read as the member that holds it.</summary>
    [Theory]
    [Trait("BR", "CH-455")]
    [InlineData("triage:Stages:1:Steps", "0", "is not final or all")]
    [InlineData("triage:Stages:1:Steps", "1", "is not final or all")]
    [InlineData("triage:Stages:0:Phase", "1", "is not request, response or both")]
    public void ShouldRefuseAPhaseOrStepsWrittenAsANumber(string key, string value, string expected)
    {
        var values = key.Contains("Phase", StringComparison.Ordinal)
            ? [("triage:Target", "cheap"), ("triage:Stages:0:Kind", "guard"), ("triage:Stages:0:Risks:x", "y"), (key, value)]
            : Triage.Append((key, value)).ToArray();

        Assert.Contains($"'{value}' {expected}", BuildRefusal(values), StringComparison.Ordinal);
    }

    /// <summary>#456 — a value echoed back is written as the file wrote it, whatever the culture of the machine.</summary>
    [Fact]
    [Trait("BR", "CH-456")]
    public void ShouldEchoAThresholdInTheInvariantCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var message = BuildRefusal(("p:Target", "cheap"), ("p:Stages:0:Kind", "guard"), ("p:Stages:0:Risks:x", "y"), ("p:Stages:0:BlockThreshold", "1.5"));

            Assert.Equal("Orkeon:Decision:Profiles:p:Stages:0:BlockThreshold: 1.5 is not a probability between 0 and 1.", message);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
