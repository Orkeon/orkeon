using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// #392: loading a crew warns about a <c>tool_call</c> deliverable no agent that may run its task can
/// write. The factory judges the agents the crew's process may pick, takes a tool it cannot see into (an
/// MCP server's) as one that may write, and stays silent when <c>--validate</c> prints the findings itself.
/// </summary>
public sealed class CrewFactoryDeliverableWarningTests : IAsyncDisposable
{
    private const string SequentialYaml = """
name: notes-crew
goal: Take notes
agents:
  reader: { role: Reader, goal: Read, tools: [file_read] }
tasks:
  notes:
    agent: reader
    description: Read the sources and take notes
    expectedOutput: The notes
    deliverable: { path: /output/notes.md }
""";

    private const string HierarchicalYaml = """
name: report-crew
goal: Write a report
process: hierarchical
managerAgent: lead
agents:
  lead: { role: Lead, goal: Coordinate }
  reader: { role: Reader, goal: Read, tools: [file_read] }
  writer: { role: Writer, goal: Write, tools: [file_write] }
tasks:
  report:
    agent: reader
    description: Write the report
    expectedOutput: The report
    deliverable: { path: /output/report.md }
""";

    private readonly MockMcpTransport _transport = new();
    private readonly McpClient _client;

    public CrewFactoryDeliverableWarningTests() => _client = new McpClient(_transport);

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _transport.DisposeAsync();
    }

    private static (YamlCrewDefinitionLoader Loader, CrewFactory Factory, MockToolRegistry Registry, RecordingLogger Log) Build(
        bool warnOnDeliverableTools = true)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var unitOfWork = new NullUnitOfWork();
        var registry = new MockToolRegistry();
        registry.AddTool("file_read", new MockTool("file_read"));
        registry.AddTool("file_write", new MockTool("file_write"));
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
            Options.Create(new CrewFactoryOptions { StrictTools = true, WarnOnDeliverableTools = warnOnDeliverableTools }));
        return (loader, factory, registry, loggerFactory.Logger);
    }

    private static IReadOnlyList<string> DeliverableWarnings(RecordingLogger log) =>
        [.. log.Entries
            .Where(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("declares the deliverable", StringComparison.Ordinal))
            .Select(static entry => entry.Message)];

    [Fact]
    public async Task ShouldWarn_WhenTheAgentHasNoToolThatWritesTheDeliverable()
    {
        var (loader, factory, _, log) = Build();
        var config = await loader.LoadFromStringAsync(SequentialYaml, TestContext.Current.CancellationToken);

        await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var warning = Assert.Single(DeliverableWarnings(log));
        Assert.StartsWith(
            "Crew 'notes-crew': Task 'notes' declares the deliverable '/output/notes.md' (source tool_call), but its agent 'Reader' has no tool that writes it",
            warning,
            StringComparison.Ordinal);
    }

    // D23 (VF-3): --validate turns the factory's warning off and prints the finding once, itself.
    [Fact]
    public async Task ShouldNotWarn_WhenTheOptionTurnsTheWarningOff()
    {
        var (loader, factory, _, log) = Build(warnOnDeliverableTools: false);
        var config = await loader.LoadFromStringAsync(SequentialYaml, TestContext.Current.CancellationToken);

        await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.Empty(DeliverableWarnings(log));
    }

    // D21 (VF-6): the manager of a hierarchical crew may give the task to the writer, whatever it declares.
    [Fact]
    public async Task ShouldNotWarn_WhenAWorkerOfAHierarchicalCrewCanWriteTheDeliverable()
    {
        var (loader, factory, _, log) = Build();
        var config = await loader.LoadFromStringAsync(HierarchicalYaml, TestContext.Current.CancellationToken);

        await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.Empty(DeliverableWarnings(log));
    }

    [Fact]
    public async Task ShouldNameEveryWorker_WhenNoWorkerOfAHierarchicalCrewCanWrite()
    {
        var (loader, factory, _, log) = Build();
        var yaml = HierarchicalYaml.Replace("tools: [file_write]", "tools: [file_read]", StringComparison.Ordinal);
        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var warning = Assert.Single(DeliverableWarnings(log));
        Assert.Contains("none of the agents that may run it ('Reader', 'Writer')", warning, StringComparison.Ordinal);
    }

    // D20 (VF-4): an MCP server's tool may write the file; the check cannot tell, so it does not warn.
    [Fact]
    public async Task ShouldNotWarn_WhenTheAgentHoldsAnMcpTool()
    {
        var (loader, factory, registry, log) = Build();
        registry.AddTool("render_report", new McpToolAdapter(new McpToolDefinition { Name = "render_report" }, _client));
        var yaml = SequentialYaml.Replace("tools: [file_read]", "tools: [file_read, render_report]", StringComparison.Ordinal);
        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.Empty(DeliverableWarnings(log));
    }
}
