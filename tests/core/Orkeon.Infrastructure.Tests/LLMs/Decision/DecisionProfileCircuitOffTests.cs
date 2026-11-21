using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Decision;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

/// <summary>
/// #433: with the decision circuit off, a decision profile builds without a decision model and
/// takes no decision - a route acts as an answer under its floor, a verify never escalates, a guard
/// lets the call go on unless the profile is fail-closed - and says so once.
/// </summary>
public sealed class DecisionProfileCircuitOffTests
{
    private const string Target = "cheap";
    private const string Spare = "spare";
    private const string Strong = "strong";

    private static readonly (string, string)[] Route =
    [
        ("p:Target", Target),
        ("p:Stages:0:Kind", "route"),
        ("p:Stages:0:Routes:cheap", "Small talk"),
        ("p:Stages:0:Routes:strong", "Mathematics"),
        ("p:Stages:0:MinConfidence", "0.5"),
    ];

    private static readonly (string, string)[] Guard =
    [
        ("p:Target", Target),
        ("p:Stages:0:Kind", "guard"),
        ("p:Stages:0:Risks:secret", "The text leaks a secret"),
    ];

    private static readonly (string, string)[] Verify =
    [
        ("p:Target", Target),
        ("p:Stages:0:Kind", "verify"),
        ("p:Stages:0:EscalateTo", Strong),
    ];

    private sealed class Host : IAsyncDisposable
    {
        public Host(DecisionCircuit? circuit, bool withModel, IEnumerable<(string Key, string Value)> values)
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton<ILoggerFactory>(Logs);
            if (circuit is not null)
                services.AddSingleton(circuit);
            if (withModel)
                services.AddSingleton<IDecisionModel>(Model);
            services
                .AddOrkeonLlmProfile(Target, _ => Cheap)
                .AddOrkeonLlmProfile(Spare, _ => SpareProvider)
                .AddOrkeonLlmProfile(Strong, _ => StrongProvider)
                .AddOrkeonDecisionProfiles(new ConfigurationBuilder()
                    .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>("Orkeon:Decision:Profiles:" + v.Key, v.Value)))
                    .Build());
            Container = services.BuildServiceProvider();
        }

        public FakeDecisionModel Model { get; } = new() { Failure = new InvalidOperationException("the model must not be asked") };

        public StubLlmProvider Cheap { get; } = new StubLlmProvider { Name = Target }.RespondToChatWith(new LlmResponse { Content = Target });

        public StubLlmProvider SpareProvider { get; } = new StubLlmProvider { Name = Spare }.RespondToChatWith(new LlmResponse { Content = Spare });

        public StubLlmProvider StrongProvider { get; } = new StubLlmProvider { Name = Strong }.RespondToChatWith(new LlmResponse { Content = Strong });

        public RecordingLoggerFactory Logs { get; } = new();

        public ServiceProvider Container { get; }

        public Task<LlmResponse> AskAsync() =>
            Container.GetRequiredService<ILlmProfileRegistry>().Resolve("p").Provider
                .ChatAsync([LlmMessage.User("Integrate x^2")], cancellationToken: TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => Container.DisposeAsync();
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldGoToTheRouteFallback_WhenTheCircuitIsOffAndTheRouteDeclaresOne()
    {
        // Arrange
        await using var host = new Host(DecisionCircuit.Off, withModel: true, Route.Append(("p:Stages:0:Fallback", Spare)));

        // Act
        var response = await host.AskAsync();

        // Assert
        Assert.Equal(Spare, response.Content);
        Assert.Empty(host.Model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldGoToTheTarget_WhenTheCircuitIsOffAndTheRouteDeclaresNoFallback()
    {
        // Arrange
        await using var host = new Host(DecisionCircuit.Off, withModel: true, Route);

        // Act
        var response = await host.AskAsync();

        // Assert
        Assert.Equal(Target, response.Content);
        Assert.Empty(host.Model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldNotEscalate_WhenTheCircuitIsOffAndTheProfileVerifies()
    {
        // Arrange
        await using var host = new Host(DecisionCircuit.OffBy(DecisionCircuitCause.Settings), withModel: true, Verify);

        // Act
        var response = await host.AskAsync();

        // Assert
        Assert.Equal(Target, response.Content);
        Assert.Empty(host.StrongProvider.ChatCalls);
        Assert.Empty(host.Model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldLetTheCallGoOn_WhenTheCircuitIsOffAndTheProfileGuards()
    {
        // Arrange
        await using var host = new Host(DecisionCircuit.Off, withModel: true, Guard);

        // Act
        var response = await host.AskAsync();

        // Assert
        Assert.Equal(Target, response.Content);
        Assert.Empty(host.Model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldBlockTheCall_WhenTheCircuitIsOffAndAFailClosedProfileGuards()
    {
        // Arrange
        await using var host = new Host(DecisionCircuit.Off, withModel: true, Guard.Append(("p:FailClosed", "true")));

        // Act
        var response = await host.AskAsync();

        // Assert
        Assert.Equal(DecisionLlmProvider.BlockedErrorType, response.ErrorType);
        Assert.Empty(host.Cheap.ChatCalls);
        Assert.Empty(host.Model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldBuildAndGoToTheRouteFallback_WhenNoDecisionModelIsRegistered()
    {
        // Arrange
        await using var host = new Host(circuit: null, withModel: false, Route.Append(("p:Stages:0:Fallback", Spare)));

        // Act
        var response = await host.AskAsync();

        // Assert
        Assert.Equal(Spare, response.Content);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldWarnOnceNamingTheCause_WhenAProfileIsCalledTwiceWithTheCircuitOff()
    {
        // Arrange
        await using var host = new Host(DecisionCircuit.Off, withModel: true, Route.Concat(Verify.Skip(1).Select(v => (v.Item1.Replace(":0:", ":1:", StringComparison.Ordinal), v.Item2))));
        await host.AskAsync();

        // Act
        await host.AskAsync();

        // Assert
        var warning = Assert.Single(host.Logs.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("the decision circuit is off: --no-decision", warning.Message, StringComparison.Ordinal);
    }
}
