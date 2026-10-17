using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Configuration;
using Orkeon.Domain.Tools.Security;
using Orkeon.Hosting;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-35 — everything <c>orkeon-host</c> does before it starts runs under the barrier that turns a
/// refused configuration into one reported line and exit 78: the configuration it boots from, the
/// daemon's own sections, read once at start, and the runner host. The host used to be built outside
/// that barrier: a typo crashed it — and under systemd, restarted it every ten seconds — and a value
/// of <c>Orkeon:Host:Discord</c> the binder could not convert crashed it later still, when the host
/// built its services.
/// <para>
/// In the working-directory collection: the sequence reads the working directory, and the
/// reserved-root guard borrows the process-global stderr.
/// </para>
/// </summary>
[Collection(nameof(WorkingDirectoryCollection))]
public sealed class HostStartupSequenceTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"orkeon-host-startup-{Guid.NewGuid():N}");
    private readonly string _originalWorkingDirectory = Directory.GetCurrentDirectory();
    private readonly RecordingFailureSink _failures = new();

    public HostStartupSequenceTests()
    {
        Directory.CreateDirectory(Path.Combine(_scratch, "crews"));
        File.WriteAllText(Path.Combine(_scratch, "crews", "support.yaml"), "name: support");
        Directory.SetCurrentDirectory(_scratch);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalWorkingDirectory);
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>
    /// A settings file hosting one crew, <paramref name="host"/> added inside <c>Orkeon:Host</c> and
    /// <paramref name="raggableTree"/> inside <c>RaggableTree</c> (off: no embedding model loads).
    /// </summary>
    private string Settings(string host = "", string raggableTree = "")
    {
        var path = Path.Combine(_scratch, "host.json");
        File.WriteAllText(path, $$"""
            {
              "RaggableTree": { "Enabled": false{{raggableTree}} },
              "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml" } ]{{host}} } }
            }
            """);
        return path;
    }

    private HostLaunch Prepare(string settings) => HostStartup.Prepare(["--settings", settings], _failures.Report);

    [Fact]
    public void A_retired_runner_key_is_reported_and_exits_78()
    {
        var launch = Prepare(Settings(raggableTree: """, "Exclude": ["bin"]"""));

        AssertRefused(launch, "RaggableTree:Exclude");
    }

    [Fact]
    public void A_RunTimeout_the_binder_cannot_convert_is_reported_and_exits_78()
    {
        var launch = Prepare(Settings(host: """, "RunTimeout": "abc" """));

        AssertRefused(launch, "Orkeon:Host:RunTimeout");
    }

    [Fact]
    public void A_Discord_ProgressInterval_the_binder_cannot_convert_is_reported_and_exits_78()
    {
        // Read once at start: bound lazily, the section only failed when the host built the
        // channel — a crash, after the start had been reported to the supervisor.
        var launch = Prepare(Settings(host: """, "Discord": { "ProgressInterval": "abc" } """));

        AssertRefused(launch, "Orkeon:Host:Discord:ProgressInterval");
    }

    /// <summary>
    /// GAP-40 — a setting the daemon reads late, a log level it cannot honour, a section name or a key
    /// it does not know: reported and 78, before the daemon declares itself ready. It used to start,
    /// then fail every run that touched the section — or crash on the level, restarted every ten
    /// seconds by systemd.
    /// </summary>
    [Theory]
    [InlineData(", \"Guardian\": { \"Enabled\": \"oui\" }", "", "Orkeon:Guardian:Enabled")]
    [InlineData(", \"Guardain\": { \"Enabled\": true }", "", "Orkeon:Guardain")]
    [InlineData("", ", \"RunTimeoutt\": \"00:10:00\"", "Orkeon:Host:RunTimeoutt")]
    public void A_setting_judged_at_the_start_is_reported_and_exits_78(string orkeon, string host, string named)
    {
        var path = Path.Combine(_scratch, "host.json");
        File.WriteAllText(path, $$"""
            {
              "RaggableTree": { "Enabled": false },
              "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml" } ]{{host}} }{{orkeon}} }
            }
            """);

        AssertRefused(Prepare(path), named);
    }

    [Fact]
    public void A_log_level_that_is_none_is_reported_and_exits_78()
    {
        var path = Path.Combine(_scratch, "host.json");
        File.WriteAllText(path, """
            {
              "RaggableTree": { "Enabled": false },
              "Logging": { "LogLevel": { "Orkeon": "Informations" } },
              "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml" } ] } }
            }
            """);

        AssertRefused(Prepare(path), "Logging:LogLevel:Orkeon");
    }

    [Fact]
    public void A_settings_file_that_is_not_JSON_is_reported_naming_it_and_exits_78()
    {
        var path = Path.Combine(_scratch, "host.json");
        File.WriteAllText(path, """
            {
              "Orkeon": { "Host": { "Crews": [ ,, ] } }
            }
            """);

        var launch = Prepare(path);

        AssertRefused(launch, "host.json");
        Assert.Contains("line 2", _failures.Reported[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_accepted_configuration_yields_the_host_and_reports_nothing()
    {
        var launch = Prepare(Settings());

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Equal(0, launch.ExitCode);
        Assert.Empty(_failures.Reported);
    }

    [Fact]
    public void A_named_settings_file_is_the_only_file_the_host_reads()
    {
        // GAP-36, decision 5: the default .NET host laid ./appsettings.json under the file
        // --settings names — its profiles were offered to crews, unseen.
        File.WriteAllText(Path.Combine(_scratch, "appsettings.json"), """
            { "Llm": { "Profiles": { "intrus": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" } } } }
            """);

        var launch = Prepare(Settings());

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Empty(host.Services.GetRequiredService<ILlmProfileRegistry>().Names);
    }

    [Fact]
    public void Without_settings_the_working_directorys_appsettings_is_the_hosts_settings_file()
    {
        // What --help promises, now by resolution rather than by the default host's content root.
        File.WriteAllText(Path.Combine(_scratch, "appsettings.json"), """
            {
              "RaggableTree": { "Enabled": false },
              "Llm": { "Profiles": { "local": { "BaseUrl": "http://localhost:11434", "Model": "qwen3" } } },
              "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml" } ] } }
            }
            """);

        var launch = HostStartup.Prepare([], _failures.Report);

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Empty(_failures.Reported);
        Assert.Equal(["local"], host.Services.GetRequiredService<ILlmProfileRegistry>().Names);
    }

    /// <summary>
    /// GAP-49, decision 2: the start guard reads the layers the host reads. A <c>/sandbox</c> the
    /// <c>ORKEON_</c> environment declares - an Aspire AppHost's <c>WithOrkeonSetting</c>, say -
    /// was invisible to it, and the daemon started over a duplicate root.
    /// </summary>
    [Fact]
    public void A_reserved_root_an_ORKEON_variable_declares_is_refused_with_the_guards_line_and_exits_78()
    {
        const string variable = "ORKEON_Orkeon__FileSystem__Mounts__0";
        var folder = Directory.CreateDirectory(Path.Combine(_scratch, "bac")).FullName;
        Environment.SetEnvironmentVariable(variable, $"{folder}:/sandbox:rw");
        HostLaunch launch;
        try
        {
            launch = Prepare(Settings());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        using var host = launch.Host;
        Assert.Null(launch.Host);
        Assert.Equal(78, launch.ExitCode);
        var reported = Assert.Single(_failures.Reported);
        Assert.Contains("'/sandbox' is a virtual root reserved by the runner", reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// CH-14 - a variable reference in a name the daemon reads is refused before it reports ready:
    /// exit 78, naming the key and its correction.
    /// </summary>
    [Theory]
    [Trait("BR", "CH-14")]
    [InlineData(", \"Discord\": { \"Enabled\": true, \"TokenEnvironmentVariable\": \"${DISCORD_TOKEN}\" }", "", "Orkeon:Host:Discord:TokenEnvironmentVariable")]
    [InlineData("", ", \"A2A\": { \"Security\": { \"ApiKeySecretNames\": [ \"${PEER_KEY}\" ] } }", "A2A:Security:ApiKeySecretNames:0")]
    public void ShouldRefuseWith78BeforeReady_WhenADaemonVariableNameIsAReference(string host, string top, string key)
    {
        var port = Orkeon.Tests.Shared.Network.LoopbackPorts.Probe();
        var a2a = top.Length == 0 ? "" : $", \"A2A\": {{ \"Enabled\": true, \"Port\": {port}, \"Crews\": [ \"support\" ] }}";
        var path = Path.Combine(_scratch, "host.json");
        File.WriteAllText(path, $$"""
            {
              "RaggableTree": { "Enabled": false }{{top}},
              "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml" } ]{{host}}{{a2a}} } }
            }
            """);

        var launch = Prepare(path);

        AssertRefused(launch, key);
        var reported = _failures.Reported[0];
        Assert.Contains(OperatorMessages.SettingPlaceholderRefused(key, SettingPlaceholders.CorrectionFor(key)!), reported, StringComparison.Ordinal);
    }

    private sealed class WitnessOptions
    {
        public string? FooApiKey { get; set; }
    }

    private static readonly Dictionary<string, string> s_excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Orkeon:TokenCounter:CharsPerToken"] = "A number of characters per token: it holds no credential, address or command.",
        ["Orkeon:Host:A2A:Host"] = "The listen address of the daemon's own A2A server (a bind pattern such as http://+), not a credential, a remote endpoint or a command.",
    };

    private static readonly string[] s_sensitiveSuffixes =
    [
        "ApiKey", "Secret", "SecretName", "SecretNames", "Password", "Token", "ConnectionString", "Url", "Uri",
        "Endpoint", "Host", "Authority", "Command", "Args", "Env", "EnvVar", "EnvironmentVariable",
    ];

    private static void Walk(string path, string name, SettingsShape shape, ISet<string> found)
    {
        if (shape.Named.Count == 0 && s_sensitiveSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            found.Add(path);

        foreach (var (key, child) in shape.Named)
            Walk(path + ":" + key, key, child, found);
        if (shape.Entries is not null)
            Walk(path + ":*", name, shape.Entries, found);
    }

    private static List<string> Uncovered(IEnumerable<(string Path, Type Shape)> declarations)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, type) in declarations)
            Walk(path, path.Split(':')[^1], SettingsShape.Of(type), found);

        return found
            .Where(p => SettingPlaceholders.CorrectionFor(p) is null && SettingPlaceholders.CorrectionFor(p + ":*") is null)
            .Where(p => !s_excluded.ContainsKey(p))
            .ToList();
    }

    [Fact]
    [Trait("BR", "CH-14")]
    public void ShouldListEverySensitiveProperty_WhenTheDeclaredShapesAreWalked()
    {
        Assert.Equal(["Witness:FooApiKey"], Uncovered([("Witness", typeof(WitnessOptions))]));

        var launch = Prepare(Settings());
        using var host = launch.Host;
        Assert.NotNull(host);
        var declarations = host.Services.GetServices<SettingsDeclaration>().Select(d => (d.Path, d.Shape)).ToList();
        Assert.NotEmpty(declarations);

        var uncovered = Uncovered(declarations);

        Assert.True(uncovered.Count == 0, "Sensitive properties neither listed nor excluded: " + string.Join(", ", uncovered));
    }

    /// <summary>
    /// CH-10 - a folder granted to a crew that is missing, or a grant that is malformed, is refused at
    /// the start, exit 78, the line naming the crew.
    /// </summary>
    [Theory]
    [Trait("BR", "CH-10")]
    [InlineData("missing")]
    [InlineData("malformed")]
    public void ShouldRefuseWith78NamingTheCrew_WhenAGrantedFolderIsMalformedOrMissing(string defect)
    {
        var grant = defect == "missing"
            ? Path.Combine(_scratch, "not-there") + ":/data:ro"
            : "\"/a\"b\":/x:ro";
        var path = Path.Combine(_scratch, "host.json");
        File.WriteAllText(path, $$"""
            {
              "RaggableTree": { "Enabled": false },
              "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml", "Mounts": [ {{System.Text.Json.JsonSerializer.Serialize(grant)}} ] } ] } }
            }
            """);

        var launch = Prepare(path);

        AssertRefused(launch, "support");
    }

    [Theory]
    [Trait("BR", "CH-11")]
    [InlineData("none")]
    [InlineData("flag")]
    [InlineData("variable")]
    public void ShouldRefuseWith78_WhenAnOperatorMountIsOutsideWithoutTheFlag(string cover)
    {
        var external = Directory.CreateTempSubdirectory("orkeon-host-external-").FullName;
        var variable = RunnerEnvironment.AllowExternalMountsVariable;
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, cover == "variable" ? "1" : null);
        try
        {
            string[] args = cover == "flag"
                ? ["--settings", Settings(), "--mount", $"{external}:/data:ro", "--allow-external-mounts"]
                : ["--settings", Settings(), "--mount", $"{external}:/data:ro"];

            var launch = HostStartup.Prepare(args, _failures.Report);

            using var host = launch.Host;
            if (cover == "none")
            {
                AssertRefused(launch, "--allow-external-mounts");
                Assert.Contains("/data", _failures.Reported[0], StringComparison.Ordinal);
            }
            else
            {
                Assert.NotNull(host);
                Assert.Empty(_failures.Reported);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            try { Directory.Delete(external, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    [Fact]
    [Trait("BR", "CH-11")]
    public void ShouldReadTheGrantedFolder_WhenItIsOutsideTheWorkingDirectory()
    {
        var granted = Directory.CreateTempSubdirectory("orkeon-host-granted-").FullName;
        try
        {
            var grant = System.Text.Json.JsonSerializer.Serialize($"{granted}:/data:ro");
            var path = Path.Combine(_scratch, "host.json");
            File.WriteAllText(path, $$"""
                {
                  "RaggableTree": { "Enabled": false },
                  "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml", "Mounts": [ {{grant}} ] } ] } }
                }
                """);

            var launch = Prepare(path);

            using var host = launch.Host;
            Assert.NotNull(host);
            var validator = host.Services.GetRequiredService<IPathValidator>();
            Assert.True(validator.ValidatePath(Path.Combine(granted, "a.txt")).IsAllowed);
        }
        finally
        {
            try { Directory.Delete(granted, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    private void AssertRefused(HostLaunch launch, string named)
    {
        Assert.Null(launch.Host);
        Assert.Equal(78, launch.ExitCode);
        var reported = Assert.Single(_failures.Reported);
        Assert.StartsWith("orkeon-host: ", reported, StringComparison.Ordinal);
        Assert.Contains(named, reported, StringComparison.Ordinal);
    }
}
