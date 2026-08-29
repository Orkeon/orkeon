using Orkeon.Hosting;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// <see cref="RunnerSettings.ReadDeclaredMounts"/> shadows the configuration binder: it answers
/// "what will the host mount?" before the host exists, so the reserved-root guard can refuse a
/// collision as a one-line diagnostic instead of letting it surface as a raw
/// "Duplicate virtual paths" out of a DI factory.
/// <para>
/// Shadowing is what makes its failure direction matter. Every dialect difference between this
/// reader and the binder is a file whose mounts the host registers and the guard does not see -
/// it fails OPEN, which is the one direction a guard must not fail in. These tests are that
/// equivalence, and the method had none.
/// </para>
/// </summary>
public sealed class ReadDeclaredMountsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ork-mounts-{Guid.NewGuid():N}");

    public ReadDeclaredMountsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ItReadsTheMountsASettingsFileDeclares()
    {
        var path = Write("""{"Orkeon":{"FileSystem":{"Mounts":["/a:/work:ro","/b:/data:rw"]}}}""");

        Assert.Equal(["/a:/work:ro", "/b:/data:rw"], RunnerSettings.ReadDeclaredMounts(path));
    }

    /// <summary>
    /// A null section is valid JSON the configuration binder accepts without complaint.
    /// <c>JsonElement.TryGetProperty</c> THROWS on a non-object rather than returning false, so
    /// this file used to kill the runner with a raw stack trace out of a method whose own
    /// documentation says "never a throw" - from a call site outside any try.
    /// </summary>
    [Theory]
    [InlineData("""{"Orkeon":null}""")]
    [InlineData("""{"Orkeon":"nope"}""")]
    [InlineData("""{"Orkeon":{"FileSystem":null}}""")]
    [InlineData("""{"Orkeon":{"FileSystem":{"Mounts":"not-an-array"}}}""")]
    [InlineData("[]")]
    public void ItReturnsNoMountsRatherThanThrowing(string json)
        => Assert.Empty(RunnerSettings.ReadDeclaredMounts(Write(json)));

    /// <summary>
    /// IConfiguration matches keys case-insensitively, so this file really does mount /work.
    /// Reading it with an ordinal lookup returned nothing and the guard waved the collision
    /// through - the fail-open direction.
    /// </summary>
    [Fact]
    public void ItMatchesSectionNamesTheWayTheBinderDoes()
    {
        var path = Write("""{"orkeon":{"fileSystem":{"mounts":["/a:/work:ro"]}}}""");

        Assert.Equal(["/a:/work:ro"], RunnerSettings.ReadDeclaredMounts(path));
    }

    /// <summary>
    /// JsonConfigurationFileParser accepts comments and trailing commas. A stricter reader threw,
    /// was swallowed as "no mounts", and again failed open on a file the host mounts fine.
    /// </summary>
    [Fact]
    public void ItAcceptsWhatTheConfigurationParserAccepts()
    {
        var path = Write("""
        {
          // the workspace
          "Orkeon": { "FileSystem": { "Mounts": ["/a:/work:ro", ] } }
        }
        """);

        Assert.Equal(["/a:/work:ro"], RunnerSettings.ReadDeclaredMounts(path));
    }

    /// <summary>
    /// Internal mounts count too: FileSystemRegistry's duplicate check spans both sections, so a
    /// root claimed under InternalMounts collides exactly as loudly as one under Mounts.
    /// </summary>
    [Fact]
    public void ItReadsInternalMountsAsWell()
    {
        var path = Write("""{"Orkeon":{"FileSystem":{"Mounts":["/a:/work:ro"],"InternalMounts":["/b:/llm-logs:rw"]}}}""");

        Assert.Equal(["/a:/work:ro", "/b:/llm-logs:rw"], RunnerSettings.ReadDeclaredMounts(path));
    }

    [Fact]
    public void ItReturnsNoMountsForAnAbsentOrUnnamedFile()
    {
        Assert.Empty(RunnerSettings.ReadDeclaredMounts(null));
        Assert.Empty(RunnerSettings.ReadDeclaredMounts(""));
        Assert.Empty(RunnerSettings.ReadDeclaredMounts(Path.Combine(_dir, "nope.json")));
    }
}
