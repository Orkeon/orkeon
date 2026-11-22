using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Decision.SystemOne;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

public class DecisionCircuitRegistrationTests
{
    private static bool Enabled(bool withModel, string? setting)
    {
        var values = new Dictionary<string, string?>();
        if (setting is not null)
            values["Orkeon:Decision:Enabled"] = setting;
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        if (withModel)
            services.AddSingleton<IDecisionModel>(new FakeDecisionModel());
        services.AddOrkeonGuardian();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<DecisionCircuit>().Enabled;
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public void ShouldBuildCircuitOff_WhenNoModelRegisteredOrSettingDisabled()
    {
        Assert.False(Enabled(withModel: false, setting: null));
        Assert.False(Enabled(withModel: true, setting: "false"));
        Assert.True(Enabled(withModel: true, setting: null));
    }

    /// <summary>
    /// The host's circuit over a real decision provider, its key read from <paramref name="environment"/> (the
    /// process scope; the user scope is empty). <paramref name="beforeResolve"/> runs once the container is
    /// built, before the circuit is resolved.
    /// </summary>
    private static DecisionCircuit HostCircuit(
        DecisionProviderDescriptor provider,
        Dictionary<string, string?> settings,
        Dictionary<string, string?>? environment = null,
        Action? beforeResolve = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var variables = environment ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(new LlmKeyEnvironment(name => variables.GetValueOrDefault(name), _ => null));
        services.AddOrkeonDecisionProvider(configuration, provider);
        services.AddOrkeonGuardian();
        using var container = services.BuildServiceProvider();
        beforeResolve?.Invoke();
        return container.GetRequiredService<DecisionCircuit>();
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldTurnTheCircuitOffByNoKeyNamingTheVariableAndTheSetting_WhenTheHostedProviderHasNoKey()
    {
        // Act
        var circuit = HostCircuit(DecisionProviderDescriptor.Liquid, new() { ["Orkeon:Liquid:Model"] = "lfm" });

        // Assert
        Assert.Equal(
            (false, (DecisionCircuitCause?)DecisionCircuitCause.NoKey,
                "the decision circuit is off: no Liquid API key: set the environment variable LIQUID_API_KEY, or Orkeon:Liquid:ApiKey"),
            (circuit.Enabled, circuit.Cause, circuit.OffReason));
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldNameTheVariableTheSettingsChose_WhenTheHostedProviderHasNoKey()
    {
        // Act
        var circuit = HostCircuit(
            DecisionProviderDescriptor.TypeSafe,
            new() { ["Orkeon:TypeSafe:ApiKeyEnvVar"] = "MY_TYPESAFE_KEY" });

        // Assert
        Assert.Equal(
            "the decision circuit is off: no TypeSafe API key: set the environment variable MY_TYPESAFE_KEY, or Orkeon:TypeSafe:ApiKey",
            circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldKeepTheCircuitOn_WhenTheKeyIsOnlyInTheEnvironmentVariable()
    {
        // Act
        var circuit = HostCircuit(
            DecisionProviderDescriptor.Liquid,
            new() { ["Orkeon:Liquid:Model"] = "lfm" },
            new(StringComparer.Ordinal) { ["LIQUID_API_KEY"] = "lk-test" });

        // Assert
        Assert.True(circuit.Enabled, circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldKeepTheCircuitOn_WhenTheKeyIsInTheSetting()
    {
        // Act
        var circuit = HostCircuit(DecisionProviderDescriptor.Liquid, new() { ["Orkeon:Liquid:ApiKey"] = "lk-test" });

        // Assert
        Assert.True(circuit.Enabled, circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldKeepTheCircuitOn_WhenTheProviderIsLocalAndHasNoKey()
    {
        // Act
        var circuit = HostCircuit(DecisionProviderDescriptor.Kev, new() { ["Orkeon:Kev:Model"] = "kev" });

        // Assert
        Assert.True(circuit.Enabled, circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldReadTheKeyWhenTheCircuitIsResolved_NotWhenItIsRegistered()
    {
        // Arrange
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        // Act
        var circuit = HostCircuit(
            DecisionProviderDescriptor.Liquid,
            new() { ["Orkeon:Liquid:Model"] = "lfm" },
            environment,
            beforeResolve: () => environment["LIQUID_API_KEY"] = "lk-late");

        // Assert
        Assert.True(circuit.Enabled, circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldNameTheSettingsAsTheCause_WhenTheCircuitIsDisabledAndTheKeyIsMissing()
    {
        // Act
        var circuit = HostCircuit(
            DecisionProviderDescriptor.Liquid,
            new() { ["Orkeon:Liquid:Model"] = "lfm", ["Orkeon:Decision:Enabled"] = "false" });

        // Assert
        Assert.Equal(DecisionCircuitCause.Settings, circuit.Cause);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldKeepTheCircuitOn_WhenTheEmbedderRegistersItsOwnModelBesideAKeylessHostedSection()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "ts" })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(new LlmKeyEnvironment(_ => null, _ => null))
            .AddSingleton<IDecisionModel>(new FakeDecisionModel());
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);
        services.AddOrkeonGuardian();
        using var container = services.BuildServiceProvider();

        // Act
        var circuit = container.GetRequiredService<DecisionCircuit>();

        // Assert
        Assert.True(circuit.Enabled, circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public void ShouldBuildTheModelOverTheContainersKeyEnvironment_SoTheModelAndTheCircuitAgree()
    {
        // Arrange
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:Liquid:Model"] = "lfm" })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(new LlmKeyEnvironment(name => variables.GetValueOrDefault(name), _ => null));
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.Liquid);
        using var container = services.BuildServiceProvider();
        var model = container.GetRequiredService<SystemOneDecisionModel>();

        // Act
        var before = model.MissingKey();
        variables["LIQUID_API_KEY"] = "lk-test";
        var after = model.MissingKey();

        // Assert
        Assert.Equal("no Liquid API key: set the environment variable LIQUID_API_KEY, or Orkeon:Liquid:ApiKey", before);
        Assert.Null(after);
    }

    [Fact]
    [Trait("BR", "CH-467")]
    public void ShouldNameTheVariable_WhenApiKeyEnvVarHoldsAWellFormedName()
    {
        // Act
        var circuit = HostCircuit(
            DecisionProviderDescriptor.Liquid,
            new() { ["Orkeon:Liquid:Model"] = "lfm", ["Orkeon:Liquid:ApiKeyEnvVar"] = " _My_Liquid_Key2 " });

        // Assert
        Assert.Equal(
            "the decision circuit is off: no Liquid API key: set the environment variable _My_Liquid_Key2, or Orkeon:Liquid:ApiKey",
            circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-467")]
    public void ShouldNameTheSettingAndNeverItsValue_WhenApiKeyEnvVarHoldsAKey()
    {
        // Act
        var circuit = HostCircuit(
            DecisionProviderDescriptor.Liquid,
            new() { ["Orkeon:Liquid:Model"] = "lfm", ["Orkeon:Liquid:ApiKeyEnvVar"] = FakeKeys.Pasted });

        // Assert
        Assert.Equal(
            (DecisionCircuitCause.NoKey, FakeKeys.LiquidSentence),
            (circuit.Cause, circuit.Detail));
        Assert.DoesNotContain(FakeKeys.Pasted, circuit.OffReason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-467")]
    public async Task ShouldNeverRepeatTheValueInTheCallsRefusal_WhenApiKeyEnvVarHoldsAKey()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Liquid:Model"] = "lfm",
                ["Orkeon:Liquid:ApiKeyEnvVar"] = FakeKeys.Pasted,
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(new LlmKeyEnvironment(_ => null, _ => null));
        services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.Liquid);
        using var container = services.BuildServiceProvider();
        var model = container.GetRequiredService<SystemOneDecisionModel>();
        var request = new DecisionRequest
        {
            State = JsonValue.Create("a ticket"),
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["refund"] = new NoulQuestion { Instructions = JsonValue.Create("Is it a refund?") },
            }.ToImmutableDictionary(),
        };

        // Act
        var error = await Assert.ThrowsAsync<DecisionModelException>(
            () => model.DecideAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "No liquid API key: Orkeon:Liquid:ApiKeyEnvVar does not hold an environment variable name; set it to one, or set Orkeon:Liquid:ApiKey.",
            error.Message);
    }

    /// <summary>A key pasted into <c>ApiKeyEnvVar</c> by mistake: the shape of a real key, built at run time.</summary>
    private static class FakeKeys
    {
        public static readonly string Pasted = string.Concat("sk", "-ant-test-", "0123456789abcdef");

        public const string LiquidSentence =
            "no Liquid API key: Orkeon:Liquid:ApiKeyEnvVar does not hold an environment variable name; set it to one, or set Orkeon:Liquid:ApiKey";
    }
}
