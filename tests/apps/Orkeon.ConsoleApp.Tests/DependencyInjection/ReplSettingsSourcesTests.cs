using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// STUDIO-49, decision 10: the REPL reads the settings the runners read — the global file
/// <c>orkeon init</c> writes, unless <c>--settings</c> names files, then the <c>ORKEON_</c>
/// variables, prefix removed. The default host read the variables without a prefix only:
/// <c>ORKEON_Llm__ApiKey</c> never reached the REPL as <c>Llm:ApiKey</c>, nor did the global file.
/// GAP-36, decision 5: and nothing else — not the <c>appsettings.json</c> and
/// <c>appsettings.{Environment}.json</c> of the current directory the default host laid under its
/// files, nor its user secrets —, in the runners' order: the variables without a prefix, the files,
/// <c>ORKEON_</c>, the command line last.
/// <para>
/// The variables these tests set carry a unique name, and <c>ORKEON_Llm__ApiKey</c> itself belongs
/// to every process of the machine. Serial: one test moves the process working directory.
/// </para>
/// </summary>
[Collection(ReplWorkingDirectoryCollection.Name)]
public sealed class ReplSettingsSourcesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-repl-sources-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = "ReplStudio49" + Guid.NewGuid().ToString("N");

    public ReplSettingsSourcesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ORKEON_" + _key + "__ApiKey", null);
        Environment.SetEnvironmentVariable(_key + "__ApiKey", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>The REPL's own composition — the default host, then <c>Program.ConfigureAppConfiguration</c>.</summary>
    private static IHost Build(string[] args, string? globalSettingsPath)
    {
        var options = ScriptedCommandsCliOptions.Parse(args);
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, builder) => Program.ConfigureAppConfiguration(builder, options, globalSettingsPath))
            .Build();
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_ORKEON_layer_reaches_the_repl_prefix_removed_and_wins_over_the_bare_variable()
    {
        // The bare variable stays read, the lowest layer, as in every runner (GAP-36).
        Environment.SetEnvironmentVariable(_key + "__ApiKey", "sk-bare");
        using (var bare = Build([], globalSettingsPath: null))
            Assert.Equal("sk-bare", bare.Services.GetRequiredService<IConfiguration>()[_key + ":ApiKey"]);

        Environment.SetEnvironmentVariable("ORKEON_" + _key + "__ApiKey", "sk-orkeon-layer");

        using var host = Build([], globalSettingsPath: null);

        Assert.Equal("sk-orkeon-layer", host.Services.GetRequiredService<IConfiguration>()[_key + ":ApiKey"]);
    }

    [Fact]
    public void An_unprefixed_variable_is_read_under_the_settings_files_as_in_the_runners()
    {
        // The REPL laid its files under the bare variables, the runners over them: one order now.
        var named = Write("named.json", $$"""{ "{{_key}}": { "ApiKey": "from-the-file" } }""");
        Environment.SetEnvironmentVariable(_key + "__ApiKey", "from-the-bare-variable");

        using var host = Build(["--settings", named], globalSettingsPath: null);

        Assert.Equal("from-the-file", host.Services.GetRequiredService<IConfiguration>()[_key + ":ApiKey"]);
    }

    [Fact]
    public void The_repl_reads_no_file_but_its_settings_files_and_no_user_secrets()
    {
        var named = Write("named.json", """{ "Llm": { "Model": "named-model" } }""");

        using var host = Build(["--settings", named], globalSettingsPath: null);

        // The user secrets are a JSON file too: one file provider, and it is the named file.
        var root = Assert.IsType<IConfigurationRoot>(host.Services.GetRequiredService<IConfiguration>(), exactMatch: false);
        var file = Assert.Single(root.Providers.OfType<FileConfigurationProvider>());
        Assert.Equal(named, file.Source.FileProvider!.GetFileInfo(file.Source.Path!).PhysicalPath);
    }

    [Fact]
    public void The_repl_ignores_the_appsettings_json_of_the_current_directory()
    {
        // The owner's recipe (GAP-36 § 6, step 4): the REPL started from a folder whose
        // appsettings.json declares a profile offers none of it.
        var workingDirectory = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "appsettings.json"), """
            { "Llm": { "Profiles": { "intrus": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" } } } }
            """);
        var global = Write("global.json", """{ "Llm": { "Model": "model-from-the-global-file" } }""");
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workingDirectory);
        try
        {
            using var host = Build([], global);

            var configuration = host.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("model-from-the-global-file", configuration["Llm:Model"]);
            Assert.False(configuration.GetSection("Llm:Profiles").Exists());
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public void The_command_line_stays_the_last_layer()
    {
        var named = Write("named.json", """{ "Llm": { "Model": "named-model" } }""");

        using var host = Build(["--settings", named, "--Llm:Model=command-line-model"], globalSettingsPath: null);

        Assert.Equal("command-line-model", host.Services.GetRequiredService<IConfiguration>()["Llm:Model"]);
    }

    [Fact]
    public void The_global_file_orkeon_init_writes_is_read_when_no_settings_file_is_named()
    {
        var global = Write("global.json", """{ "Llm": { "Model": "model-from-the-global-file" } }""");

        using var host = Build([], global);

        Assert.Equal("model-from-the-global-file", host.Services.GetRequiredService<IConfiguration>()["Llm:Model"]);
    }

    [Fact]
    public void A_missing_global_file_is_no_error()
    {
        using var host = Build([], Path.Combine(_root, "absent", "appsettings.json"));

        Assert.Null(host.Services.GetRequiredService<IConfiguration>()["Llm:Model"]);
    }

    [Fact]
    public void A_settings_file_named_on_the_command_line_replaces_the_global_file_as_for_the_runners()
    {
        // The global file's reference must not send its key to the other file's endpoint.
        var global = Write("global.json", """{ "Llm": { "Model": "global-model", "ApiKeyEnvVar": "GLOBAL_KEY" } }""");
        var named = Write("named.json", """{ "Llm": { "Model": "named-model", "BaseUrl": "https://api.moonshot.ai/v1" } }""");

        using var host = Build(["--settings", named], global);

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal("named-model", configuration["Llm:Model"]);
        Assert.Null(configuration["Llm:ApiKeyEnvVar"]);
    }

    [Fact]
    public void The_ORKEON_layer_wins_over_the_settings_files()
    {
        var named = Write("named.json", $$"""{ "{{_key}}": { "ApiKey": "from-the-file" } }""");
        Environment.SetEnvironmentVariable("ORKEON_" + _key + "__ApiKey", "from-the-environment");

        using var host = Build(["--settings", named], globalSettingsPath: null);

        Assert.Equal("from-the-environment", host.Services.GetRequiredService<IConfiguration>()[_key + ":ApiKey"]);
    }
}

/// <summary>
/// Serialises the tests that move the process working directory — process-wide state, and xUnit
/// runs collections in parallel by default.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReplWorkingDirectoryCollection
{
    public const string Name = "repl-working-directory";
}
