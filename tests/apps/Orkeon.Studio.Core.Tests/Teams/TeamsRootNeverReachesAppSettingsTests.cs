using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Hosting;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// STUDIO-61, against the real runner host: the settings file Studio writes carries no
/// <c>Orkeon:Studio</c> section — the teams root lives in Studio's own preferences file and in
/// the <c>ORKEON_STUDIO_TEAMS_ROOT</c> variable —, because <c>orkeon run</c> reads that file and
/// refuses at start any <c>Orkeon:*</c> section it does not know (GAP-54). The variable, inherited
/// by the child process, lands outside the containers the run judges.
/// </summary>
public sealed class TeamsRootNeverReachesAppSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-studio-teams-root-" + Guid.NewGuid().ToString("N"));

    public TeamsRootNeverReachesAppSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>A settings file as Studio writes it, through the document every settings screen saves.</summary>
    private async Task<string> WriteStudioSettingsAsync()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" },
              "RaggableTree": { "Enabled": false }
            }
            """);
        var path = Path.Combine(_root, AppSettingsDocument.FileName);
        await AppSettingsFile.SaveAsync(document, path, TestContext.Current.CancellationToken);
        return path;
    }

    /// <summary>
    /// Builds the runner host over <paramref name="settingsPath"/>, <paramref name="rootKeys"/> laid
    /// last the way <c>RunnerSettings.ComposeSources</c> lays the process environment: without a
    /// prefix, as root-level keys.
    /// </summary>
    private static IHost Build(string settingsPath, IReadOnlyDictionary<string, string?>? rootKeys = null) =>
        RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan(),
            configureLogging: (_, logging) => logging.SetMinimumLevel(LogLevel.None),
            configureBuilder: builder => builder.ConfigureAppConfiguration(
                (_, configuration) => configuration.AddInMemoryCollection(
                    rootKeys ?? new Dictionary<string, string?>(StringComparer.Ordinal))));

    [Fact]
    public async Task The_file_studio_writes_names_no_studio_section_and_the_host_starts_on_it()
    {
        var settingsPath = await WriteStudioSettingsAsync();
        var text = await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Studio", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TeamsRoot", text, StringComparison.OrdinalIgnoreCase);
        using var host = Build(settingsPath);
        Assert.NotNull(host);
    }

    [Fact]
    public async Task A_studio_section_in_the_file_would_be_refused_which_is_why_the_preference_lives_elsewhere()
    {
        // The reading the design refused (decision 1 b): a key Orkeon:Studio:TeamsRoot in the
        // settings file. The host names the section and stops — every run of the machine would.
        var settingsPath = Path.Combine(_root, AppSettingsDocument.FileName);
        await File.WriteAllTextAsync(
            settingsPath,
            """{ "Orkeon": { "Studio": { "TeamsRoot": "/home/me/workshop/teams" } }, "RaggableTree": { "Enabled": false } }""",
            TestContext.Current.CancellationToken);

        var refusal = Assert.Throws<RunnerSettingsException>(() => Build(settingsPath));

        Assert.Contains("Orkeon:Studio", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("is not a section Orkeon reads", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_variable_inherited_by_the_child_process_is_a_root_key_the_host_neither_reads_nor_refuses()
    {
        var settingsPath = await WriteStudioSettingsAsync();
        var inherited = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [TeamsRootLocator.EnvironmentVariable] = "/home/me/workshop/teams",
        };

        using var host = Build(settingsPath, inherited);

        var configuration = host.Services.GetService(typeof(IConfiguration)) as IConfiguration;
        Assert.NotNull(configuration);
        Assert.Equal("/home/me/workshop/teams", configuration[TeamsRootLocator.EnvironmentVariable]);
        Assert.False(configuration.GetSection("Orkeon:Studio").Exists());
    }
}
