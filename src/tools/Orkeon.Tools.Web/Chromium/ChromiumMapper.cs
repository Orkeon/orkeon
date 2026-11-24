namespace Orkeon.Tools.Web.Chromium;

/// <summary>
/// The chromium-bidi mapper: the script that, run in a hidden tab, translates WebDriver BiDi into the
/// DevTools protocol. Embedded as published, at one pinned version (<c>Mapper/README.md</c>).
/// </summary>
internal static class ChromiumMapper
{
    /// <summary>The version of the <c>chromium-bidi</c> npm package the script comes from.</summary>
    public const string Version = "160.0.8181-0";

    /// <summary>SHA-256 of the embedded script.</summary>
    public const string Sha256 = "6950a24bb711fefa888f92f0992b0e11a3f8b981f5e698f5646925c1cf9c2292";

    private const string ResourceName = "Orkeon.Tools.Web.Chromium.mapperTab.js";

    private static readonly Lazy<string> s_script = new(Read);

    /// <summary>The script.</summary>
    public static string Script => s_script.Value;

    private static string Read()
    {
        using var stream = typeof(ChromiumMapper).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource '{ResourceName}' is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
