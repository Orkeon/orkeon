using Orkeon.Domain.FileSystem;
using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Plugins.Tests.Fixtures;

/// <summary>
/// Creates an isolated temp plugin directory on real disk, exposed through a
/// <see cref="DiskBackedFileSystemService"/> under the virtual root <c>/plugins</c>.
/// The test assembly itself (which contains the <c>Fake*Plugin</c> implementations)
/// doubles as the plugin-assembly fixture: it is copied into the directory and loaded
/// through the real pipeline.
/// </summary>
public sealed class PluginDirectoryFixture : IDisposable
{
    public const string VirtualRoot = "/plugins";

    public PluginDirectoryFixture()
    {
        PhysicalRoot = Path.Combine(
            Path.GetTempPath(), "orkeon-plugins-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PhysicalRoot);
        FileSystem = new DiskBackedFileSystemService(PhysicalRoot, VirtualRoot);
    }

    /// <summary>Physical base directory of the fixture (mounted as <c>/plugins</c>).</summary>
    public string PhysicalRoot { get; }

    /// <summary>Single-mount VFS over <see cref="PhysicalRoot"/>.</summary>
    public DiskBackedFileSystemService FileSystem { get; }

    /// <summary>Physical path of the test assembly (a real IOrkeonPlugin host).</summary>
    public static string FixturePluginAssemblyPath => typeof(FakeAlphaPlugin).Assembly.Location;

    /// <summary>Physical path of a managed assembly that contains no plugin types.</summary>
    public static string NonPluginManagedAssemblyPath =>
        typeof(DiskBackedFileSystemService).Assembly.Location;

    /// <summary>Copies the fixture plugin assembly as a flat plugin dll. Returns its virtual path.</summary>
    public string AddFlatFixturePlugin(string fileName = "FixturePlugin.dll") =>
        CopyAsFlat(FixturePluginAssemblyPath, fileName);

    /// <summary>Copies a plugin-less managed assembly as a flat dll. Returns its virtual path.</summary>
    public string AddFlatNonPluginAssembly(string fileName = "NotAPlugin.dll") =>
        CopyAsFlat(NonPluginManagedAssemblyPath, fileName);

    /// <summary>
    /// Copies the fixture plugin assembly using the folder-per-plugin layout
    /// (<c>/plugins/&lt;name&gt;/&lt;name&gt;.dll</c>). Returns its virtual path.
    /// </summary>
    public string AddConventionalFixturePlugin(string pluginDirName = "FixturePlugin")
    {
        var dir = Path.Combine(PhysicalRoot, pluginDirName);
        Directory.CreateDirectory(dir);
        File.Copy(FixturePluginAssemblyPath, Path.Combine(dir, pluginDirName + ".dll"), overwrite: true);
        return $"{VirtualRoot}/{pluginDirName}/{pluginDirName}.dll";
    }

    /// <summary>
    /// Writes an arbitrary (non-assembly) file at <paramref name="relativePath"/>,
    /// creating intermediate directories. Returns its virtual path.
    /// </summary>
    public string AddOpaqueFile(string relativePath, string content = "this is not a managed assembly")
    {
        var physical = Path.Combine(
            PhysicalRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        File.WriteAllText(physical, content);
        return $"{VirtualRoot}/{relativePath}";
    }

    /// <summary>
    /// Builds a <see cref="PluginAssemblyCandidate"/> for an existing virtual path,
    /// resolving the physical path through the VFS (same mechanism as discovery).
    /// </summary>
    public PluginAssemblyCandidate CandidateFor(string virtualPath)
    {
        var validation = FileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
        Assert.True(validation.IsAllowed, validation.DenialReason ?? "resolution denied");
        return new PluginAssemblyCandidate
        {
            VirtualPath = virtualPath,
            PhysicalPath = validation.ResolvedPath!,
        };
    }

    private string CopyAsFlat(string sourcePhysicalPath, string fileName)
    {
        File.Copy(sourcePhysicalPath, Path.Combine(PhysicalRoot, fileName), overwrite: true);
        return $"{VirtualRoot}/{fileName}";
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(PhysicalRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort — temp cleanup must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

/// <summary>Helpers over the fixture plugin assembly (the test assembly itself).</summary>
internal static class FixtureAssembly
{
    /// <summary>
    /// Number of concrete <see cref="IOrkeonPlugin"/> implementations compiled into the
    /// fixture assembly — keeps count-based assertions in sync if doubles are added.
    /// </summary>
    public static int CountPluginTypes() =>
        typeof(FakeAlphaPlugin).Assembly.GetTypes()
            .Count(static t => t.IsClass && !t.IsAbstract && typeof(IOrkeonPlugin).IsAssignableFrom(t));
}
