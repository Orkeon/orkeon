using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Tests.Confinement;

/// <summary><see cref="ContainerDetector"/>: a marker file or the .NET images' variable tells a container.</summary>
public sealed class ContainerDetectorTests
{
    private static ContainerDetector Detector(string? existingFile = null, string? variable = null)
        => new(path => path == existingFile, name => name == ContainerDetector.RunningInContainerVariable ? variable : null);

    [Theory]
    [InlineData("/.dockerenv")]
    [InlineData("/run/.containerenv")]
    public void ShouldSeeAContainer_WhenAMarkerFileExists(string marker)
        => Assert.True(Detector(existingFile: marker).IsInContainer());

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void ShouldSeeAContainer_WhenTheDotnetVariableIsTrue(string value)
        => Assert.True(Detector(variable: value).IsInContainer());

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("1")]
    public void ShouldSeeAHost_WhenNoMarkerExistsAndTheVariableIsNotTrue(string? value)
        => Assert.False(Detector(existingFile: "/etc/hostname", variable: value).IsInContainer());
}
