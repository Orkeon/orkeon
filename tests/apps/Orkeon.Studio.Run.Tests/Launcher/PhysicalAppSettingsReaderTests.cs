using Orkeon.Studio.Run.Launcher;

namespace Orkeon.Studio.Run.Tests.Launcher;

/// <summary>The real-disk settings reader: a blank path, a missing file and a readable one.</summary>
public sealed class PhysicalAppSettingsReaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("orkeon-run-settings-");

    public void Dispose() => _root.Delete(recursive: true);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldRefuse_WhenNoPathIsGiven(string path)
    {
        var read = PhysicalAppSettingsReader.Instance.TryRead(path, out var json, out var reason);

        Assert.False(read);
        Assert.Null(json);
        Assert.Equal("No settings file was given.", reason);
    }

    [Fact]
    public void ShouldSayTheFileDoesNotExistYet_WhenItIsMissing()
    {
        var path = Path.Combine(_root.FullName, "absent.json");

        var read = PhysicalAppSettingsReader.Instance.TryRead(path, out var json, out var reason);

        Assert.False(read);
        Assert.Null(json);
        Assert.Equal($"'{path}' does not exist yet.", reason);
    }

    [Fact]
    public void ShouldReturnTheContent_WhenTheFileIsReadable()
    {
        var path = Path.Combine(_root.FullName, "appsettings.json");
        File.WriteAllText(path, """{ "Orkeon": {} }""");

        var read = PhysicalAppSettingsReader.Instance.TryRead(path, out var json, out var reason);

        Assert.True(read);
        Assert.Equal("""{ "Orkeon": {} }""", json);
        Assert.Null(reason);
    }
}
