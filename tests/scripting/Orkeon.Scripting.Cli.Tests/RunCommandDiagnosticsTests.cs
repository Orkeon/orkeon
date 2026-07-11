using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Hosting;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for the runner-parity diagnostics added to <c>orkeon run</c>: the <c>--validate</c>
/// and <c>--list-tools</c> flags (delegated to <see cref="Orkeon.Hosting.RunnerExecution"/>), the
/// <c>semantic_search</c> registration the YAML path now shares with the standalone standard runner,
/// and the "path required unless --list-tools" contract. Console-driving tests hold the serial
/// <see cref="CliCollection"/> gate because they redirect the process-global streams.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandDiagnosticsTests
{
    [Fact]
    public void ToRunnerOptions_carries_validate_and_list_tools_flags()
    {
        var mapped = RunCommand.ToRunnerOptions(new RunCommandOptions
        {
            ScriptPath = "crew.yaml",
            Validate = true,
            ListTools = true,
        });

        Assert.True(mapped.Validate);
        Assert.True(mapped.ListTools);
        Assert.Equal("crew.yaml", mapped.ConfigPath);
    }

    [Fact]
    public void AddSemanticSearchTool_registers_the_semantic_search_tool()
    {
        // The host normally supplies IEmbeddingProvider; register the double so the tool graph
        // (SearchTool → EmbeddingServiceAdapter → provider) resolves, mirroring the YAML path.
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingProvider, StubEmbeddingProvider>();
        services.AddSemanticSearchTool();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<IBaseTool>().ToList();

        Assert.Contains(tools, t => t.Name == "semantic_search");
    }

    [Fact]
    public async Task ListTools_prints_the_runtime_registry_including_semantic_search_and_human_input()
    {
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions { ListTools = true });

        Assert.Equal(Program.ExitOk, exit);
        // human_input is injected by the shared runner's TryBuildHost path; semantic_search comes
        // from the configureServices hook orkeon run now passes — both must appear in the manifest.
        Assert.Contains("semantic_search", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("human_input", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTools_needs_no_crew_definition_path()
    {
        using var console = new TestConsole();

        // No ScriptPath supplied — --list-tools must still succeed (parity with the standard runner).
        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions { ListTools = true });

        Assert.Equal(Program.ExitOk, exit);
    }

    [Fact]
    public async Task Missing_path_without_list_tools_reports_a_clear_error()
    {
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions { ScriptPath = "" });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("required", console.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
