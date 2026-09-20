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

    /// <summary>
    /// VFS-90: the agent-facing reader keeps the ARRAY POSITION of every entry, because that is
    /// the index the host withdraws an entry under. An element that is not a mount string still
    /// counts one position, or the entries after it would be withdrawn at the wrong index.
    /// </summary>
    [Fact]
    public void ItReadsAgentFacingEntriesAtTheirArrayPosition()
    {
        var path = Write("""{"Orkeon":{"FileSystem":{"Mounts":["/a:/work:ro", null, "", "/b:/data:rw"],"InternalMounts":["/c:/vault:rw"]}}}""");

        var entries = RunnerSettings.ReadDeclaredAgentFacingMounts(path);

        Assert.Equal([new DeclaredMountEntry(0, "/a:/work:ro"), new DeclaredMountEntry(3, "/b:/data:rw")], entries);
        Assert.Equal(["/c:/vault:rw"], RunnerSettings.ReadDeclaredInternalMounts(path));
    }

    /// <summary>
    /// An entry the <c>ORKEON_</c> environment declares is a declared entry too (the host's
    /// snapshot sees it), so the guards read it as well — laid over the file index by index,
    /// the way the configuration lays it. Otherwise a second <c>/output</c> arriving through a
    /// variable slipped past the one-line guard and met the host's exception instead.
    /// </summary>
    [Fact]
    public void ItLaysTheEnvironmentEntriesOverTheFileOnes()
    {
        var path = Write("""{"Orkeon":{"FileSystem":{"Mounts":["/a:/work:ro","/b:/data:rw"]}}}""");
        const string ninth = "ORKEON_Orkeon__FileSystem__Mounts__9";
        const string first = "ORKEON_ORKEON__FILESYSTEM__MOUNTS__1";
        Environment.SetEnvironmentVariable(ninth, "/c:/output:rw");
        Environment.SetEnvironmentVariable(first, "/d:/data:ro");
        try
        {
            Assert.Equal(
                [new DeclaredMountEntry(0, "/a:/work:ro"), new DeclaredMountEntry(1, "/d:/data:ro"), new DeclaredMountEntry(9, "/c:/output:rw")],
                RunnerSettings.ReadDeclaredAgentFacingMounts(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ninth, null);
            Environment.SetEnvironmentVariable(first, null);
        }
    }

    [Fact]
    public void TheSplitReadersAgreeWithTheUnionReader()
    {
        var path = Write("""{"orkeon":{"filesystem":{"mounts":["01J9Z3K4M5N6P7Q8R9S0T1V2W3|/a:/work:ro"],"internalmounts":["/c:/vault:rw"]}}}""");

        Assert.Equal(
            RunnerSettings.ReadDeclaredMounts(path),
            [.. RunnerSettings.ReadDeclaredAgentFacingMounts(path).Select(e => e.Spec), .. RunnerSettings.ReadDeclaredInternalMounts(path)]);
    }
}
