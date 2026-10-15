using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Hosting;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// <see cref="RunnerSettings.ReadDeclaredMounts"/> and its two split forms answer "what will the
/// host mount?" before the host exists, so the guards can refuse a collision, a reserved root or a
/// missing folder as a one-line diagnostic instead of letting it surface as a raw "Duplicate
/// virtual paths" out of a DI factory.
/// <para>
/// They read the layers the host reads (GAP-49, decision 2): the environment without a prefix, the
/// settings file, the <c>ORKEON_</c> environment, through <see cref="RunnerSettings.ReadConfiguration"/>.
/// A reader of their own - a JSON reader shadowing the binder - failed OPEN on every layer and
/// dialect it missed: a mount the host registers and the guard does not see, the one direction a
/// guard must not fail in. The equivalence test below compares the readers with the host itself.
/// </para>
/// </summary>
// Serial: a test here sets ORKEON_Orkeon__FileSystem__Mounts__* in the process environment,
// which every reader and every runner host built in parallel would see — or lose mid-test.
[Collection(ConsoleSerialCollection.Name)]
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

    /// <summary>Neither a file nor a variable: no mount, whatever the path names.</summary>
    [Fact]
    public void ItReturnsNoMountsWithoutAFileOrAVariable()
    {
        Assert.Empty(RunnerSettings.ReadDeclaredMounts((string?)null));
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

    /// <summary>
    /// GAP-49, decision 2: a variable without a prefix is a declared entry too - the host composes
    /// that layer under the file (GAP-36). At an index the file declares, the file wins; at another,
    /// the variable's entry is read at its own index.
    /// </summary>
    [Fact]
    public void ItReadsTheUnprefixedVariablesUnderTheFile()
    {
        var path = Write("""{"Orkeon":{"FileSystem":{"Mounts":["/a:/work:ro"]}}}""");
        const string zeroth = "Orkeon__FileSystem__Mounts__0";
        const string seventh = "Orkeon__FileSystem__Mounts__7";
        Environment.SetEnvironmentVariable(zeroth, "/z:/shadowed:ro");
        Environment.SetEnvironmentVariable(seventh, "/b:/data:rw");
        try
        {
            Assert.Equal(
                [new DeclaredMountEntry(0, "/a:/work:ro"), new DeclaredMountEntry(7, "/b:/data:rw")],
                RunnerSettings.ReadDeclaredAgentFacingMounts(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(zeroth, null);
            Environment.SetEnvironmentVariable(seventh, null);
        }
    }

    /// <summary>
    /// The reserved-root guard's reader read the file alone: an Internal mount the environment
    /// declares, prefixed or not, claimed its root unseen.
    /// </summary>
    [Fact]
    public void ItReadsTheInternalMountsTheEnvironmentDeclares()
    {
        var path = Write("""{"Orkeon":{"FileSystem":{"Mounts":["/a:/work:ro"]}}}""");
        const string prefixed = "ORKEON_Orkeon__FileSystem__InternalMounts__3";
        const string bare = "Orkeon__FileSystem__InternalMounts__4";
        Environment.SetEnvironmentVariable(prefixed, "/c:/vault:rw");
        Environment.SetEnvironmentVariable(bare, "/d:/keys:rw");
        try
        {
            Assert.Equal(["/a:/work:ro", "/c:/vault:rw", "/d:/keys:rw"], RunnerSettings.ReadDeclaredMounts(path));
            Assert.Equal(["/c:/vault:rw", "/d:/keys:rw"], RunnerSettings.ReadDeclaredInternalMounts(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefixed, null);
            Environment.SetEnvironmentVariable(bare, null);
        }
    }

    /// <summary>
    /// The equivalence the guards rest on: for a file, an <c>ORKEON_</c> variable and a variable
    /// without a prefix, the reader returns the non-empty entries of <c>Orkeon:FileSystem:Mounts</c>
    /// the host declares, at the same indices.
    /// </summary>
    [Fact]
    public void TheAgentFacingReaderReturnsTheEntriesTheHostDeclares()
    {
        var file0 = $"{Folder("a")}:/work:ro";
        var file1 = $"{Folder("b")}:/data:rw";
        var prefixed1 = $"{Folder("c")}:/data:ro";
        var bare5 = $"{Folder("d")}:/extra:ro";
        var path = Write(
            """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "FileSystem": { "Mounts": [ """
            + JsonSerializer.Serialize(file0) + ", " + JsonSerializer.Serialize(file1) + " ] } } }");
        const string prefixed = "ORKEON_Orkeon__FileSystem__Mounts__1";
        const string bare = "Orkeon__FileSystem__Mounts__5";
        Environment.SetEnvironmentVariable(prefixed, prefixed1);
        Environment.SetEnvironmentVariable(bare, bare5);
        try
        {
            using var host = RunnerHost.Build(path, new RunnerMountPlan());
            var hostEntries = host.Services.GetRequiredService<IConfiguration>()
                .GetSection("Orkeon:FileSystem:Mounts").GetChildren()
                .Where(child => !string.IsNullOrEmpty(child.Value))
                .Select(child => new DeclaredMountEntry(int.Parse(child.Key, CultureInfo.InvariantCulture), child.Value!))
                .OrderBy(entry => entry.Index)
                .ToList();

            Assert.Equal(
                [new DeclaredMountEntry(0, file0), new DeclaredMountEntry(1, prefixed1), new DeclaredMountEntry(5, bare5)],
                hostEntries);
            Assert.Equal(hostEntries, RunnerSettings.ReadDeclaredAgentFacingMounts(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefixed, null);
            Environment.SetEnvironmentVariable(bare, null);
        }
    }

    /// <summary>
    /// GAP-49, the order of the layers: an <c>ORKEON_</c> variable set empty withdraws the file's
    /// entry at its index, as the host reads it — the reader returns nothing there, the entries after
    /// it keep their index, and the host declares the same list.
    /// </summary>
    [Fact]
    public void AnEmptyOrkeonVariableWithdrawsTheFileEntryAtItsIndex()
    {
        var file0 = $"{Folder("a")}:/work:ro";
        var file1 = $"{Folder("b")}:/data:rw";
        var path = Write(
            """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "FileSystem": { "Mounts": [ """
            + JsonSerializer.Serialize(file0) + ", " + JsonSerializer.Serialize(file1) + " ] } } }");
        const string emptied = "ORKEON_Orkeon__FileSystem__Mounts__0";
        Environment.SetEnvironmentVariable(emptied, "");
        try
        {
            Assert.Equal(string.Empty, Environment.GetEnvironmentVariable(emptied));
            Assert.Equal([new DeclaredMountEntry(1, file1)], RunnerSettings.ReadDeclaredAgentFacingMounts(path));
            Assert.Equal([file1], RunnerSettings.ReadDeclaredMounts(path));

            using var host = RunnerHost.Build(path, new RunnerMountPlan());
            var hostEntries = host.Services.GetRequiredService<IConfiguration>()
                .GetSection("Orkeon:FileSystem:Mounts").GetChildren()
                .Where(child => !string.IsNullOrEmpty(child.Value))
                .Select(child => new DeclaredMountEntry(int.Parse(child.Key, CultureInfo.InvariantCulture), child.Value!))
                .ToList();
            Assert.Equal(hostEntries, RunnerSettings.ReadDeclaredAgentFacingMounts(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(emptied, null);
        }
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;

    [Fact]
    public void TheSplitReadersAgreeWithTheUnionReader()
    {
        var path = Write("""{"orkeon":{"filesystem":{"mounts":["01J9Z3K4M5N6P7Q8R9S0T1V2W3|/a:/work:ro"],"internalmounts":["/c:/vault:rw"]}}}""");

        Assert.Equal(
            RunnerSettings.ReadDeclaredMounts(path),
            [.. RunnerSettings.ReadDeclaredAgentFacingMounts(path).Select(e => e.Spec), .. RunnerSettings.ReadDeclaredInternalMounts(path)]);
    }
}
