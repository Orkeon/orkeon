using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
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

    private void AssertRefused(HostLaunch launch, string named)
    {
        Assert.Null(launch.Host);
        Assert.Equal(78, launch.ExitCode);
        var reported = Assert.Single(_failures.Reported);
        Assert.StartsWith("orkeon-host: ", reported, StringComparison.Ordinal);
        Assert.Contains(named, reported, StringComparison.Ordinal);
    }
}
