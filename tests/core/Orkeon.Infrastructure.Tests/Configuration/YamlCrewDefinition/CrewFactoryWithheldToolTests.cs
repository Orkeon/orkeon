using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Crew.Execution;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// #433: with the decision circuit off, <c>system_one_decide</c> is withheld - a crew that lists it
/// fails to load with a message naming the tool and the cause, and <c>--validate</c> (through
/// <see cref="CrewFactoryOptions.OnWithheldTool"/>) hears of it and goes on.
/// </summary>
public sealed class CrewFactoryWithheldToolTests
{
    private const string Yaml = """
name: triage-crew
goal: Triage
agents:
  reader: { role: Reader, goal: Read, tools: [file_read, system_one_decide] }
tasks:
  triage:
    agent: reader
    description: Triage the ticket
    expectedOutput: A category
""";

    public static TheoryData<string, string> Causes => new()
    {
        { nameof(DecisionCircuitCause.Switch), "--no-decision" },
        { nameof(DecisionCircuitCause.Settings), "Orkeon:Decision:Enabled=false" },
        { nameof(DecisionCircuitCause.NoProvider), "no decision provider resolved" },
    };

    private static (YamlCrewDefinitionLoader Loader, CrewFactory Factory, RecordingLogger Log) Build(
        DecisionCircuit circuit, bool strict, Action<string>? onWithheld = null)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var unitOfWork = new NullUnitOfWork();
        var registry = new MockToolRegistry();
        registry.AddTool("file_read", new MockTool("file_read"));
#pragma warning disable CA2000 // RecordingLoggerFactory holds nothing to dispose; its logger outlives this method.
        var loggerFactory = new RecordingLoggerFactory();
#pragma warning restore CA2000
        var factory = new CrewFactory(
            loader,
            registry,
            new Logger<CrewFactory>(loggerFactory),
            new InMemoryCrewRepository(unitOfWork),
            new InMemoryAgentRepository(unitOfWork),
            new InMemoryTaskRepository(unitOfWork),
            Options.Create(new CrewFactoryOptions { StrictTools = strict, OnWithheldTool = onWithheld }),
            decisionCircuit: circuit);
        return (loader, factory, loggerFactory.Logger);
    }

    [Theory]
    [MemberData(nameof(Causes))]
    [Trait("BR", "CH-433")]
    public async Task ShouldRefuseTheCrewNamingTheToolAndTheCause_WhenTheCircuitIsOff(string cause, string named)
    {
        // Arrange
        var (loader, factory, _) = Build(DecisionCircuit.OffBy(Enum.Parse<DecisionCircuitCause>(cause)), strict: true);
        var config = await loader.LoadFromStringAsync(Yaml, TestContext.Current.CancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("tool 'system_one_decide' withheld: the decision circuit is off: " + named, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldHandTheWithheldToolToTheCallbackAndLoad_WhenValidationListens()
    {
        // Arrange
        var heard = new List<string>();
        var (loader, factory, _) = Build(DecisionCircuit.Off, strict: true, heard.Add);
        var config = await loader.LoadFromStringAsync(Yaml, TestContext.Current.CancellationToken);

        // Act
        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(crew);
        Assert.Equal(["tool 'system_one_decide' withheld: the decision circuit is off: --no-decision"], heard);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldWarnAndLoad_WhenTheCircuitIsOffAndToolsAreNotStrict()
    {
        // Arrange
        var (loader, factory, log) = Build(DecisionCircuit.Off, strict: false);
        var config = await loader.LoadFromStringAsync(Yaml, TestContext.Current.CancellationToken);

        // Act
        await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        // Assert
        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("system_one_decide", StringComparison.Ordinal));
        Assert.Contains("--no-decision", warning.Message, StringComparison.Ordinal);
    }
}
