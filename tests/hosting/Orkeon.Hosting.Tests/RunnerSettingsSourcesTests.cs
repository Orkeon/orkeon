using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-36, decision 5 — the sources a run reads its settings from, and those who diagnose it.
/// The runner host was the default .NET host plus the resolved file and the <c>ORKEON_</c> layer:
/// under the file lay the <c>appsettings.json</c> and <c>appsettings.{Environment}.json</c> of the
/// current directory — a file of another project could add profiles, MCP servers or mounts the
/// resolved one never declared — while <c>orkeon doctor</c> read the file and <c>ORKEON_</c> alone.
/// Every host now composes the same layers: the variables without a prefix (the lowest — the
/// <c>OTEL_*</c> a .NET Aspire AppHost sets reach the exporter through it), the resolved file,
/// then <c>ORKEON_</c>.
/// <para>
/// In the serial console collection: one test moves the process working directory. The variables
/// the tests set carry a unique name.
/// </para>
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerSettingsSourcesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-settings-sources-" + Guid.NewGuid().ToString("N"));
    private readonly string _section = "Gap36Sources" + Guid.NewGuid().ToString("N");

    public RunnerSettingsSourcesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_section + "__Value", null);
        Environment.SetEnvironmentVariable("ORKEON_" + _section + "__Value", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Key => _section + ":Value";

    /// <summary>Writes <paramref name="json"/> as the <c>appsettings.json</c> of a folder of its own under the test's root.</summary>
    private string Write(string folder, string json)
    {
        var path = Path.Combine(_root, folder, "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>A settings file setting the test's key, or not, the embedding model kept out of the host.</summary>
    private string SettingsWith(string? value) => Write("settings-" + Guid.NewGuid().ToString("N"), value is null
        ? """{ "RaggableTree": { "Enabled": false } }"""
        : $$"""{ "RaggableTree": { "Enabled": false }, "{{_section}}": { "Value": "{{value}}" } }""");

    private static IHost BuildHost(string? settingsPath)
    {
        var original = Console.Error;
        using var muted = new StringWriter();
        Console.SetError(muted);
        try
        {
            return RunnerHost.Build(
                settingsPath,
                new RunnerMountPlan(),
                configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.None));
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void ReadConfiguration_reads_an_unprefixed_variable_under_the_file()
    {
        Environment.SetEnvironmentVariable(_section + "__Value", "from-the-bare-variable");

        Assert.Equal("from-the-bare-variable", RunnerSettings.ReadConfiguration(null)[Key]);
        Assert.Equal("from-the-file", RunnerSettings.ReadConfiguration(SettingsWith("from-the-file"))[Key]);
    }

    [Fact]
    public void ReadConfiguration_and_the_runner_host_lay_the_same_layers_in_the_same_order()
    {
        // Without the file and the ORKEON_ variable, the bare variable; with the file, the file; with
        // the ORKEON_ variable, the ORKEON_ variable — for the diagnostic as for the run.
        Environment.SetEnvironmentVariable(_section + "__Value", "from-the-bare-variable");
        var withoutValue = SettingsWith(null);
        var withValue = SettingsWith("from-the-file");

        foreach (var (settingsPath, expected) in new[] { (withoutValue, "from-the-bare-variable"), (withValue, "from-the-file") })
        {
            Assert.Equal(expected, RunnerSettings.ReadConfiguration(settingsPath)[Key]);
            using var host = BuildHost(settingsPath);
            Assert.Equal(expected, host.Services.GetRequiredService<IConfiguration>()[Key]);
        }

        Environment.SetEnvironmentVariable("ORKEON_" + _section + "__Value", "from-the-orkeon-variable");
        Assert.Equal("from-the-orkeon-variable", RunnerSettings.ReadConfiguration(withValue)[Key]);
        using (var host = BuildHost(withValue))
            Assert.Equal("from-the-orkeon-variable", host.Services.GetRequiredService<IConfiguration>()[Key]);
    }

    [Fact]
    public void The_runner_host_reads_no_file_but_the_resolved_one_and_no_user_secrets()
    {
        var settingsPath = SettingsWith("from-the-file");

        using var host = BuildHost(settingsPath);

        // The user secrets are a JSON file too: one file provider, and it is the resolved file.
        var root = Assert.IsType<IConfigurationRoot>(host.Services.GetRequiredService<IConfiguration>(), exactMatch: false);
        var file = Assert.Single(root.Providers.OfType<FileConfigurationProvider>());
        Assert.Equal(settingsPath, file.Source.FileProvider!.GetFileInfo(file.Source.Path!).PhysicalPath);
    }

    [Fact]
    public void The_runner_host_ignores_the_appsettings_json_of_the_current_directory()
    {
        // The owner's recipe (GAP-36 § 6, step 4): a run started from a folder whose appsettings.json
        // declares a profile names none of it.
        var workingDirectory = Path.Combine(_root, "elsewhere");
        Write("elsewhere", """
            { "Llm": { "Profiles": { "intrus": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" } } } }
            """);
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workingDirectory);
        try
        {
            foreach (var settingsPath in new[] { SettingsWith(null), null })
            {
                using var host = BuildHost(settingsPath);

                Assert.Empty(LlmSettings.ProfileNames(host.Services.GetRequiredService<IConfiguration>()));
                Assert.Empty(host.Services.GetRequiredService<ILlmProfileRegistry>().Names);
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }
}
