using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The built-in tools a run registers, read on the host <c>--list-tools</c> builds (human_input
/// included, as on the crew path), plus <c>progress_report</c>, which the scripted-command substrate
/// of <c>orkeon-repl</c> adds (<c>AddScriptCommands</c>).
/// </summary>
internal static class RegisteredTools
{
    /// <summary>Builds the host over <paramref name="root"/>, which holds an <c>appsettings.json</c>, and lists its tools.</summary>
    internal static async Task<IReadOnlyList<IBaseTool>> ReadAsync(string root)
    {
        using var host = await RunnerExecution.BuildToolHostAsync(
            Path.Combine(root, "appsettings.json"),
            root,
            new ToolHostMounts([], [], AllowExternalMounts: true, OfferedMountSelectors.MountIdOption | OfferedMountSelectors.MountOption),
            (_, services) =>
            {
                services.AddOrkeonHumanInput();
                services.AddSingleton<IBaseTool>(new ProgressReportTool(new ProgressBroker()));
            },
            TextWriter.Null,
            TestContext.Current.CancellationToken);
        Assert.NotNull(host);
        return [.. await host.Services.GetRequiredService<IToolRegistry>().GetAllToolsAsync()];
    }

    /// <summary>
    /// A fresh folder with an empty <c>appsettings.json</c>: RaggableTree stays on, as by default, so
    /// the analysis tools are registered; nothing resolves its embedding model until a tool runs.
    /// </summary>
    internal static string CreateRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "appsettings.json"), "{ }");
        return root;
    }
}
