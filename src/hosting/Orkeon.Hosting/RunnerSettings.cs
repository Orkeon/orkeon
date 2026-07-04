using Orkeon.Compliance.Vfs;

namespace Orkeon.Hosting;

/// <summary>
/// Shared settings resolution logic for Orkeon runners.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: probes user-supplied appsettings.json locations on the physical disk before any VFS mount exists.")]
public static class RunnerSettings
{
    /// <summary>
    /// Resolves the appsettings.json path using a fallback chain:
    ///   1. Explicit --settings arg
    ///   2. appsettings.json next to config.yaml
    ///   3. examples/_shared/appsettings.json (walk up from config dir)
    /// </summary>
    public static string? ResolveSettingsPath(string? explicitPath, string configDir)
    {
        // 1. Explicit
        if (!string.IsNullOrEmpty(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            if (File.Exists(full)) return full;
            Console.Error.WriteLine($"WARNING: Explicit settings not found: {full}");
            return null;
        }

        // 2. Per-example override (next to config.yaml)
        var localSettings = Path.Combine(configDir, "appsettings.json");
        if (File.Exists(localSettings)) return localSettings;

        // 3. Walk up to find _shared/appsettings.json
        var dir = new DirectoryInfo(configDir);
        while (dir != null)
        {
            var shared = Path.Combine(dir.FullName, "_shared", "appsettings.json");
            if (File.Exists(shared)) return shared;
            if (File.Exists(Path.Combine(dir.FullName, "Orkeon.Examples.sln"))) break;
            dir = dir.Parent;
        }

        Console.Error.WriteLine("WARNING: No appsettings.json found. Using environment variables only.");
        return null;
    }
}
