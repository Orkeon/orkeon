using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Host;

namespace Orkeon.Host.Tests;

/// <summary>
/// CH-06: the daemon reads its long flags written <c>--name=value</c>, and refuses what it cannot
/// read instead of ignoring it (exit 78).
/// </summary>
[Collection(nameof(WorkingDirectoryCollection))]
public sealed partial class HostArgumentsTests : IDisposable
{
    private const string Unknown = "orkeon-host: unknown argument '--setings'; see orkeon-host --help";

    private readonly string _scratch;
    private readonly string _settings;
    private readonly string _originalWorkingDirectory;

    public HostArgumentsTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"orkeon-host-args-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
        _settings = Path.Combine(_scratch, "host.json");
        File.WriteAllText(_settings, "{}");
        _originalWorkingDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_scratch);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalWorkingDirectory);
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    [Theory]
    [Trait("BR", "CH-06")]
    [InlineData("systemd")]
    [InlineData("windows-script")]
    [InlineData("msi")]
    [InlineData("dockerfile")]
    [InlineData("aspire")]
    public void ShouldAcceptTheShippedCommandLine_WhenAServiceFileStartsTheDaemon(string launcher)
    {
        var args = ShippedArguments(launcher);
        var reports = new List<string>();

        var launch = HostStartup.Prepare(args, reports.Add);
        launch.Host?.Dispose();

        Assert.DoesNotContain(reports, r => r.Contains("unknown argument", StringComparison.Ordinal)
            || r.Contains("settings file not found", StringComparison.Ordinal)
            || r.Contains("requires a path", StringComparison.Ordinal)
            || r.Contains("twice", StringComparison.Ordinal)
            || r.Contains("empty", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void ShouldReadTheSettingsFile_WhenSettingsIsAttachedWithEquals()
    {
        var command = HostStartup.Read([$"--settings={_settings}"]);

        Assert.Equal(_settings, command.SettingsPath);
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void ShouldApplyTheWorkingDirectory_WhenItIsAttachedWithEquals()
    {
        var command = HostStartup.Read([$"--working-dir={_scratch}"]);

        Assert.Equal(_scratch, command.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void ShouldReadTheMount_WhenItIsAttachedWithEquals()
    {
        var spec = $"{_scratch}:/data:ro";

        var command = HostStartup.Read([$"--mount={spec}"]);

        Assert.Equal([spec], command.Mounts);
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void ShouldRefuseWith78_WhenAnArgumentIsUnknown()
    {
        var (exit, reports) = Launch("--setings", "x");

        Assert.Equal(78, exit);
        Assert.Equal([Unknown], reports);
    }

    [Theory]
    [Trait("BR", "CH-06")]
    [InlineData("--settings")]
    [InlineData("--working-dir")]
    public void ShouldRefuseWith78_WhenSettingsOrWorkingDirIsRepeated(string flag)
    {
        string[] args = flag == "--settings"
            ? ["-s", _settings, "--settings", _settings]
            : ["--working-dir", _scratch, "--working-dir", _scratch];

        var (exit, reports) = Launch(args);

        Assert.Equal(78, exit);
        var report = Assert.Single(reports);
        Assert.Contains(flag, report, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-06")]
    [InlineData("--settings=", "--settings")]
    [InlineData("--allow-external-mounts=1", "--allow-external-mounts")]
    public void ShouldRefuseWith78_WhenAnAttachedValueIsEmptyOrAFlagIsAttached(string argument, string named)
    {
        var (exit, reports) = Launch(argument);

        Assert.Equal(78, exit);
        var report = Assert.Single(reports);
        Assert.Contains(named, report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--mount", "orkeon-host: --mount requires a value.")]
    [InlineData("-m", "orkeon-host: -m requires a value.")]
    [InlineData("-s", "orkeon-host: --settings requires a path.")]
    [InlineData("--working-dir", "orkeon-host: --working-dir requires a path.")]
    [InlineData("--mount=", "orkeon-host: --mount= has an empty value.")]
    public void ShouldRefuseWith78_WhenAValueFlagHasNoValue(string argument, string expected)
    {
        var (exit, reports) = Launch(argument);

        Assert.Equal(78, exit);
        Assert.Equal([expected], reports);
    }

    [Fact]
    public void ShouldRefuseWith78_WhenTheWorkingDirectoryDoesNotExist()
    {
        var missing = Path.Combine(_scratch, "no-such-dir");

        var (exit, reports) = Launch("--working-dir", missing);

        Assert.Equal(78, exit);
        var report = Assert.Single(reports);
        Assert.Contains("working directory not found", report, StringComparison.Ordinal);
        Assert.Contains(missing, report, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseWith78_WhenTheSettingsFileDoesNotExist()
    {
        var missing = Path.Combine(_scratch, "missing.json");

        var (exit, reports) = Launch("--settings", missing);

        Assert.Equal(78, exit);
        Assert.Equal([$"orkeon-host: settings file not found: {missing}"], reports);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void ShouldAnswerTheHelp_WhenAskedForIt(string flag)
    {
        var text = HostStartup.InformationalText(["--settings", "x", flag, "--version"]);

        // The help wins over --version and over the flags that would start the daemon.
        Assert.NotNull(text);
        Assert.Contains("--version", text, StringComparison.Ordinal);
        Assert.Contains("docs/architecture/service-host.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldAnswerTheVersion_WhenAskedForIt()
    {
        var text = HostStartup.InformationalText(["--version"]);

        Assert.NotNull(text);
        Assert.StartsWith("orkeon-host ", text, StringComparison.Ordinal);
        Assert.NotEqual("orkeon-host ", text);
    }

    [Fact]
    public void ShouldAnswerNothing_WhenTheDaemonMustRun()
    {
        Assert.Null(HostStartup.InformationalText(["--settings", _settings]));
        Assert.Null(HostStartup.InformationalText([]));
        Assert.Throws<ArgumentNullException>(() => HostStartup.InformationalText(null!));
    }

    private static (int Exit, List<string> Reports) Launch(params string[] args)
    {
        var reports = new List<string>();
        var launch = HostStartup.Prepare(args, reports.Add);
        launch.Host?.Dispose();
        return (launch.ExitCode, reports);
    }

    private string[] ShippedArguments(string launcher)
    {
        var root = RepositoryRoot();
        string[] tokens;
        switch (launcher)
        {
            case "systemd":
                tokens = Tokenize(LineStarting(root, "deploy/systemd/orkeon-host.service", "ExecStart=")["ExecStart=".Length..])[1..];
                break;
            case "windows-script":
                var script = LineStarting(root, "deploy/windows/install-service.ps1", "$binaryPath =");
                tokens = Tokenize(script.Split('\'')[1])[1..];
                break;
            case "msi":
                var wxs = File.ReadAllText(Path.Combine(root, "installers/msi/PackageService.wxs"));
                tokens = Tokenize(System.Net.WebUtility.HtmlDecode(ArgumentsPattern().Match(wxs).Groups[1].Value));
                break;
            case "dockerfile":
                var entry = LineStarting(root, "deploy/Dockerfile.host", "ENTRYPOINT");
                tokens = JsonSerializer.Deserialize<string[]>(entry["ENTRYPOINT".Length..].Trim())![1..];
                break;
            default:
                var source = File.ReadAllText(Path.Combine(root, "src/hosting/Orkeon.Hosting.Aspire/OrkeonResourceBuilderExtensions.cs"));
                var start = source.IndexOf("AddOrkeonHost(", StringComparison.Ordinal);
                var body = source[start..source.IndexOf(".WithOtlpExporter()", start, StringComparison.Ordinal)];
                tokens = [.. AddPattern().Matches(body).Select(m => m.Groups[1].Value)];
                break;
        }

        var result = new List<string>();
        for (var i = 0; i < tokens.Length; i++)
        {
            result.Add(tokens[i]);
            if (tokens[i] is "--settings" or "-s")
            {
                result.Add(_settings);
                if (launcher != "aspire")
                    i++;
            }
            else if (tokens[i] == "--working-dir")
            {
                result.Add(_scratch);
                i++;
            }
        }

        return [.. result];
    }

    private static string LineStarting(string root, string relative, string prefix) =>
        File.ReadLines(Path.Combine(root, relative)).First(l => l.TrimStart().StartsWith(prefix, StringComparison.Ordinal)).Trim();

    private static string[] Tokenize(string line) =>
        [.. TokenPattern().Matches(line).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Value)];

    [GeneratedRegex("\"([^\"]*)\"|\\S+")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("Arguments=\"([^\"]*)\"")]
    private static partial Regex ArgumentsPattern();

    [GeneratedRegex("Args\\.Add\\(\"(-[^\"]*)\"\\)")]
    private static partial Regex AddPattern();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Orkeon.sln not found above the test binaries.");
    }
}
