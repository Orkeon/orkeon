namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Transforms a <c>.ork.ts</c> source into JavaScript that can be fed directly to Jint.
/// </summary>
public interface IScriptTranspiler
{
    /// <summary>
    /// Transpiles <paramref name="tsSource"/> to JavaScript. Used when the host already
    /// has the source in memory and does not need import resolution.
    /// </summary>
    Task<string> TranspileAsync(string tsSource, CancellationToken ct);

    /// <summary>
    /// Bundles the script at <paramref name="physicalPath"/> into a single JavaScript blob,
    /// resolving any relative <c>import</c>/<c>export</c> statements against the entry
    /// file's directory. <paramref name="source"/> is the already-VFS-read entry source —
    /// transpilers that don't bundle (e.g. pass-through) hand it back as-is.
    /// </summary>
    Task<string> BundleFromFileAsync(string physicalPath, string source, CancellationToken ct);
}
