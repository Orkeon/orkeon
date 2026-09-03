using Orkeon.Host;

namespace Orkeon.Host.Tests;

/// <summary>
/// The <c>--working-dir</c> probe: a Windows service starts in System32, and the daemon
/// resolves its settings, its boot configuration and its crew paths against the current
/// directory — the flag is what moves the process where the operator's relative paths are
/// true. Applied unconditionally, which is what makes these tests meaningful off Windows.
/// </summary>
[Collection(nameof(WorkingDirectoryCollection))]
public sealed class WorkingDirectoryProbeTests : IDisposable
{
    private readonly string _scratch;
    private readonly string _originalWorkingDirectory;

    public WorkingDirectoryProbeTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"orkeon-host-wd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
        _originalWorkingDirectory = Directory.GetCurrentDirectory();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalWorkingDirectory);
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public void ApplyingTheWorkingDirectoryMovesTheProcess()
    {
        var error = StartupProbes.TryApplyWorkingDirectory(_scratch);

        Assert.Null(error);
        // Path.GetFullPath normalizes the /tmp vs /private/tmp style aliasing of temp dirs.
        Assert.Equal(
            Path.GetFullPath(_scratch).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ARelativeSettingsPathResolvesAgainstTheAppliedWorkingDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_scratch, "conf"));
        File.WriteAllText(
            Path.Combine(_scratch, "conf", "host.json"),
            """{ "Orkeon": { "Host": { "RunTimeout": "00:13:00" } } }""");

        Assert.Null(StartupProbes.TryApplyWorkingDirectory(_scratch));
        var configuration = StartupProbes.BuildBootConfiguration(Path.Combine("conf", "host.json"));

        Assert.Equal("00:13:00", configuration["Orkeon:Host:RunTimeout"]);
    }

    [Fact]
    public void AMissingWorkingDirectoryIsRefusedWithoutMovingTheProcess()
    {
        var before = Directory.GetCurrentDirectory();
        var missing = Path.Combine(_scratch, "does-not-exist");

        var error = StartupProbes.TryApplyWorkingDirectory(missing);

        Assert.NotNull(error);
        Assert.Contains(missing, error, StringComparison.Ordinal);
        Assert.Equal(before, Directory.GetCurrentDirectory());
    }
}
