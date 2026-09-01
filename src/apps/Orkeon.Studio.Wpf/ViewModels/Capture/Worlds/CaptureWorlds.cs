using System.IO;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// The two machines the campaign walks, and the throwaway tree they live in.
/// <para>
/// Both are built up front and never mutated into each other. That is what turns empty versus
/// populated into something a stop declares rather than something it has to undo — and a teardown
/// that forgets to un-populate a list is a whole class of wrong screenshot this design cannot
/// produce.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "UI-layer capture harness owning its own throwaway tree under the operator's temp directory — " +
    "Studio's own state, not framework I/O; same exception class as CaptureWorldWriter.")]
internal sealed class CaptureWorlds : IDisposable
{
    private CaptureWorlds(string root, CaptureWorld seeded, CaptureWorld pristine)
    {
        Root = root;
        Seeded = seeded;
        Pristine = pristine;
    }

    /// <summary>The directory both worlds live under; printed so a failed run can be inspected.</summary>
    public string Root { get; }

    /// <summary>The populated machine.</summary>
    public CaptureWorld Seeded { get; }

    /// <summary>The first-run machine, with no CLI installed.</summary>
    public CaptureWorld Pristine { get; }

    /// <summary>Writes both worlds under a fresh directory.</summary>
    public static async Task<CaptureWorlds> CreateAsync(string? root = null)
    {
        var home = root ?? Path.Combine(
            Path.GetTempPath(), "orkeon-capture", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(home);

        var seeded = await CaptureWorldWriter.WriteAsync(StudioFixture.Seeded, Path.Combine(home, "seeded"));
        var pristine = await CaptureWorldWriter.WriteAsync(StudioFixture.Pristine, Path.Combine(home, "pristine"));

        return new CaptureWorlds(home, seeded, pristine);
    }

    /// <summary>The world a stop asked for.</summary>
    public CaptureWorld For(CaptureWorldKind kind) =>
        kind == CaptureWorldKind.Pristine ? Pristine : Seeded;

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked handle must never turn a finished campaign into a failed one; the root is
            // named and printed, so an orphan is identifiable and removable by hand.
        }
    }
}
