using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>
/// CH-09: <c>orkeon-host</c> refuses an address no run serves at the start - reported, exit 78.
/// In the working-directory collection, like the other start sequences.
/// </summary>
[Collection(nameof(WorkingDirectoryCollection))]
public sealed class HostLlmEndpointRefusalTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"orkeon-host-endpoint-{Guid.NewGuid():N}");
    private readonly string _originalWorkingDirectory = Directory.GetCurrentDirectory();
    private readonly RecordingFailureSink _failures = new();

    public HostLlmEndpointRefusalTests()
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

    [Fact]
    [Trait("BR", "CH-09")]
    public void ShouldRefuseWith78_WhenTheDaemonReadsAnAddressNoRunServes()
    {
        var path = Path.Combine(_scratch, "host.json");
        File.WriteAllText(path, """
            {
              "RaggableTree": { "Enabled": false },
              "Llm": { "BaseUrl": "localhost:11434", "Model": "m" },
              "Orkeon": { "Host": { "Crews": [ { "Name": "support", "Path": "crews/support.yaml" } ] } }
            }
            """);

        var launch = HostStartup.Prepare(["--settings", path], _failures.Report);

        using var host = launch.Host;
        Assert.Null(launch.Host);
        Assert.Equal(78, launch.ExitCode);
        var reported = Assert.Single(_failures.Reported);
        Assert.StartsWith("orkeon-host: ", reported, StringComparison.Ordinal);
        Assert.Contains("Llm:BaseUrl", reported, StringComparison.Ordinal);
    }
}
