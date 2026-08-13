using System.Reflection;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// The version reading the two terminal front-ends share. Both used to carry their own copy of
/// it, and the packaging smokes compare a plain version — so the SourceLink suffix has to be
/// stripped in exactly one place.
/// </summary>
public class StudioAssemblyInfoTests
{
    [Fact]
    public void The_version_of_a_studio_assembly_is_the_one_the_build_stamped()
    {
        var assembly = typeof(Orkeon.Studio.Core.Validation.AppSettingsValidator).Assembly;
        var stamped = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        var version = StudioAssemblyInfo.VersionOf(assembly);

        Assert.StartsWith(version, stamped, StringComparison.Ordinal);
        Assert.DoesNotContain('+', version);
        Assert.NotEqual(StudioAssemblyInfo.UnknownVersion, version);
    }

    [Fact]
    public void The_version_line_is_the_tool_name_followed_by_the_version()
    {
        var assembly = typeof(StudioAssemblyInfo).Assembly;

        Assert.Equal(
            $"orkeon-studio-run {StudioAssemblyInfo.VersionOf(assembly)}",
            StudioAssemblyInfo.VersionLine("orkeon-studio-run", assembly));
    }

    [Fact]
    public void A_blank_tool_name_is_refused_rather_than_printed_as_a_bare_version()
    {
        Assert.Throws<ArgumentException>(
            () => StudioAssemblyInfo.VersionLine("  ", typeof(StudioAssemblyInfo).Assembly));
    }
}
