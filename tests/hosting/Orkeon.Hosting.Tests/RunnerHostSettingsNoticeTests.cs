using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Plugins;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// A section Orkeon knows and no shipped binary reads — <c>ToolRateLimiting</c>, <c>Orkeon:Dlp</c> —
/// used to be read as absent without a word: the run started without the limit, the budget or the
/// screening its operator had written. The host now says it at its start, once per section, on
/// stderr and on its logger, and starts all the same. What it does not report: a section another
/// shipped binary reads, a root section Orkeon does not know, a section this very composition reads.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostSettingsNoticeTests : IDisposable
{
    private const string NoComponent = "is read by no component of this host";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-settings-notices-" + Guid.NewGuid().ToString("N"));

    public RunnerHostSettingsNoticeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Settings(params (string Key, string Value)[] values)
    {
        var all = new Dictionary<string, string>
        {
            ["RaggableTree:Enabled"] = "false",
            ["Llm:BaseUrl"] = "http://localhost:11434",
            ["Llm:Model"] = "llama3.2",
        };
        foreach (var (key, value) in values)
            all[key] = value;

        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, SettingsJson.Of(all));
        return path;
    }

    /// <summary>
    /// The notices of the host a run builds on <paramref name="settings"/>, asked as <c>orkeon doctor</c>
    /// asks them: nothing is announced, so the tests below that read stderr — each on a section no
    /// other test of this project starts a host with — find their line whatever ran before them.
    /// </summary>
    private static IReadOnlyList<string> Notices(string? settings, Action<HostBuilderContext, IServiceCollection>? configureServices = null)
    {
        var verdict = RunnerHost.InspectSettings(settings, configureServices);

        Assert.Empty(verdict.Refusals);
        return verdict.Notices;
    }

    [Fact]
    public void A_tool_rate_limit_the_run_host_does_not_read_is_said_on_stderr_and_the_host_starts()
    {
        using var logs = new CapturingLoggerProvider();
        IHost? built = null;
        var stderr = CaptureStderr(() =>
        {
            built = RunnerHost.Build(
                Settings(("ToolRateLimiting:GlobalToolRequestsPerMinute", "10")),
                new RunnerMountPlan(),
                configureLogging: (_, logging) =>
                {
                    logging.AddProvider(logs);
                    logging.SetMinimumLevel(LogLevel.Warning);
                });
        });
        using var host = built;

        Assert.NotNull(host);
        var line = Assert.Single(stderr.Split('\n'), text => text.Contains(NoComponent, StringComparison.Ordinal));
        Assert.StartsWith("WARNING: ToolRateLimiting " + NoComponent, line, StringComparison.Ordinal);
        Assert.Contains("AddOrkeonToolRateLimiting()", line, StringComparison.Ordinal);
        // What comes close, and this host does read.
        Assert.Contains(" RateLimiting,", line, StringComparison.Ordinal);
        Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(NoComponent, StringComparison.Ordinal));
    }

    [Fact]
    public void A_dlp_section_is_said_to_screen_nothing_here()
    {
        var stderr = CaptureStderr(() =>
        {
            using var host = RunnerHost.Build(Settings(("Orkeon:Dlp:Enabled", "true")), new RunnerMountPlan());
        });

        var line = Assert.Single(stderr.Split('\n'), text => text.Contains(NoComponent, StringComparison.Ordinal));
        Assert.StartsWith("WARNING: Orkeon:Dlp " + NoComponent, line, StringComparison.Ordinal);
        Assert.Contains("AddOrkeonDlp()", line, StringComparison.Ordinal);
        Assert.Contains("not active", line, StringComparison.Ordinal);
        Assert.Contains("Security:Prompt", line, StringComparison.Ordinal);
        Assert.Contains("Security:ToolResults", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_section_is_said_once_for_the_process_whatever_the_number_of_hosts()
    {
        var settings = Settings(("Orkeon:MultiModal:Enabled", "true"));

        var stderr = CaptureStderr(() =>
        {
            using var first = RunnerHost.Build(settings, new RunnerMountPlan());
            using var second = RunnerHost.Build(settings, new RunnerMountPlan());
        });

        Assert.Single(stderr.Split('\n'), text => text.Contains("Orkeon:MultiModal " + NoComponent, StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>Orkeon:Host</c> is the daemon's: a file two shipped binaries share is the case the start
    /// validation protects, and nothing is said of it.
    /// </summary>
    [Theory]
    [InlineData("Orkeon:Host:Crews:0:Name", "support")]
    [InlineData("Orkeon:Host:Discord:Enabled", "true")]
    [InlineData("Orkeon:Cli:Tui:SpinnerVerbs:0", "thinking")]
    public void A_section_another_shipped_binary_reads_is_not_reported(string key, string value) =>
        Assert.Empty(Notices(Settings((key, value))));

    /// <summary>The root stays open: a name Orkeon does not know is another program's.</summary>
    [Theory]
    [InlineData("MonTruc:Clef", "x")]
    [InlineData("MyApp:ToolRateLimiting:GlobalToolRequestsPerMinute", "10")]
    public void A_root_section_orkeon_does_not_know_is_not_reported(string key, string value) =>
        Assert.Empty(Notices(Settings((key, value))));

    /// <summary>A value at the path of a section is no section: an environment variable of that name, most likely.</summary>
    [Fact]
    public void A_single_value_at_the_path_of_a_section_is_not_reported()
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, """{ "RaggableTree": { "Enabled": false }, "Plugins": "none", "Evaluation": "later" }""");

        Assert.Empty(Notices(path));
    }

    [Fact]
    public void A_host_written_in_csharp_that_reads_the_section_is_told_nothing()
    {
        var settings = Settings(
            ("ToolRateLimiting:GlobalToolRequestsPerMinute", "10"),
            ("TokenBudget:MaxTokensPerCrew", "1000"));

        var notices = Notices(settings, (_, services) => services.AddOrkeonToolRateLimiting());

        Assert.Empty(notices);
    }

    /// <summary>The plugins' section is read at registration, without a declaration: the registry it leaves says it was.</summary>
    [Fact]
    public void A_host_that_loaded_plugins_is_told_nothing_of_their_section()
    {
        var settings = Settings(("Plugins:Directory", "/plugins"));

        Assert.Single(Notices(settings));
        Assert.Empty(Notices(settings, (_, services) => services.AddSingleton<IPluginRegistry>(new StubPluginRegistry())));
    }

    /// <summary>The <c>ORKEON_</c> environment writes a section as the file does.</summary>
    [Fact]
    public void A_section_written_by_the_environment_is_reported_as_the_files()
    {
        const string Variable = "ORKEON_TokenBudget__MaxTokensPerCrew";
        Environment.SetEnvironmentVariable(Variable, "1000");
        try
        {
            var notice = Assert.Single(Notices(Settings()));

            Assert.StartsWith("TokenBudget " + NoComponent, notice, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    public static TheoryData<string, string, string> SectionsNoShippedHostReads()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var section in SettingsCatalog.Complete.Sections.Where(section => section.Hosts.Count == 0))
        {
            var key = SettingsCatalog.Complete.SettingsOf(section.Path).First().Path
                .Replace(SettingsCatalogBuilder.Name, "x", StringComparison.Ordinal)
                .Replace(SettingsCatalogBuilder.Index, "0", StringComparison.Ordinal);
            data.Add(section.Path, key, section.Registration ?? string.Empty);
        }

        return data;
    }

    /// <summary>The list is computed: every section of the catalogue no shipped binary reads, by the registration that reads it.</summary>
    [Theory]
    [MemberData(nameof(SectionsNoShippedHostReads))]
    public void Every_section_no_shipped_binary_reads_is_reported_with_its_registration(string section, string key, string registration)
    {
        var notice = Assert.Single(Notices(Settings((key, "1"))));

        Assert.StartsWith($"{section} {NoComponent}: a C# host reads it through {registration}().", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void The_catalogue_names_the_sections_no_shipped_binary_reads()
    {
        var sections = SettingsCatalog.Complete.Sections.Where(section => section.Hosts.Count == 0).ToList();

        Assert.Contains(sections, section => section.Path == "ToolRateLimiting");
        Assert.Contains(sections, section => section.Path == "Orkeon:Dlp");
        Assert.All(sections, section => Assert.False(string.IsNullOrEmpty(section.Registration), section.Path));
    }

    /// <summary>
    /// The page of the opt-in subsystems says which sections no shipped binary reads: it names each
    /// one the catalogue computes, in both languages, so a section that joins the list joins the page.
    /// </summary>
    [Theory]
    [InlineData("docs/reference/opt-in-subsystems.md")]
    [InlineData("docs/fr/reference/opt-in-subsystems.md")]
    public void The_page_of_the_opt_in_subsystems_names_every_section_no_shipped_binary_reads(string page)
    {
        var text = ProducedFile.Read(page);

        Assert.NotNull(text);
        Assert.All(
            SettingsCatalog.Complete.Sections.Where(section => section.Hosts.Count == 0),
            section => Assert.Contains($"`{section.Path}`", text, StringComparison.Ordinal));
    }

    /// <summary>What a notice points to exists, and a shipped binary reads it: a hint to a section nothing reads would be a second trap.</summary>
    [Fact]
    public void What_a_notice_points_to_is_a_section_every_shipped_binary_reads()
    {
        Assert.NotEmpty(SettingsValidation.NoticeHints);
        foreach (var (section, hint) in SettingsValidation.NoticeHints)
        {
            Assert.True(SettingsCatalog.Complete.Section(section) is { Hosts.Count: 0 }, $"{section} is read by a shipped binary, or unknown");
            Assert.NotEmpty(hint.Sections);
            foreach (var pointed in hint.Sections)
            {
                Assert.True(
                    SettingsCatalog.Complete.Section(pointed) is { } known && SettingsHosts.All.All(host => known.Hosts.Contains(host)),
                    $"{section} points to {pointed}, which is not a section the three shipped binaries read");
                Assert.Contains(pointed, hint.Sentence, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>What <c>orkeon doctor</c> asks: the refusals and the notices of one host, built once.</summary>
    [Fact]
    public void The_inspection_of_a_file_gives_its_refusals_and_its_notices()
    {
        var settings = Settings(("Orkeon:Guardian:Enabled", "oui"), ("Orkeon:Monitoring:MaxTraceHistory", "10"));

        var verdict = RunnerHost.InspectSettings(settings, configureServices: null);

        Assert.Contains(verdict.Refusals, refusal => refusal.Contains("Orkeon:Guardian:Enabled", StringComparison.Ordinal));
        Assert.StartsWith("Orkeon:Monitoring " + NoComponent, Assert.Single(verdict.Notices), StringComparison.Ordinal);
        Assert.Equal(verdict.Refusals, RunnerHost.ValidateSettings(settings, configureServices: null));
    }

    /// <summary>An unknown key's refusal ends on the verb that lists the keys of its section.</summary>
    [Theory]
    [InlineData("Llm:Provider", "`orkeon settings Llm` lists its keys.")]
    [InlineData("Llm:Profiles:a:Modle", "`orkeon settings Llm` lists its keys.")]
    [InlineData("Orkeon:Guardian:Enabeld", "`orkeon settings Orkeon:Guardian` lists its keys.")]
    [InlineData("Orkeon:CodeSandbox:Docker:PullImage", "`orkeon settings Orkeon:CodeSandbox:Docker` lists its keys.")]
    public void An_unknown_key_is_refused_with_the_verb_that_lists_the_keys_of_its_section(string key, string ending)
    {
        var error = Assert.Throws<RunnerSettingsException>(() =>
            RunnerHost.Build(Settings((key, "x"), ("Llm:Profiles:a:BaseUrl", "http://localhost:11434")), new RunnerMountPlan()));

        Assert.EndsWith(ending, error.Message, StringComparison.Ordinal);
    }

    private static string CaptureStderr(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }
}
