using System.Reflection;
using Orkeon.Scripting.Telemetry;

namespace Orkeon.Scripting.Tests.Telemetry;

/// <summary>#418: the source of every script span reports the version of the running release, where it reported none.</summary>
public sealed class ScriptingActivitySourceVersionTests
{
    [Fact]
    public void ShouldReportTheRelease_OnTheScriptingSource()
    {
        var assembly = typeof(ScriptingActivitySource).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        var release = plus < 0 ? informational : informational[..plus];

        Assert.Equal(release, ScriptingActivitySource.Instance.Version);
        // Read another way than the code reads it: the assembly version comes from the same VersionPrefix.
        Assert.StartsWith(assembly.GetName().Version!.ToString(3), ScriptingActivitySource.Instance.Version!, StringComparison.Ordinal);
    }
}
