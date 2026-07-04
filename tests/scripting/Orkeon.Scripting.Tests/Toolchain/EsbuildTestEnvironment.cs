using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

/// <summary>
/// Resolves the esbuild binary used by the test suite. Looks up
/// <c>ORKEON_ESBUILD_PATH</c>, then walks up from the test bin directory until it finds
/// the locally installed <c>tools/scripting-esbuild/node_modules/.bin/esbuild</c>, then
/// falls back to PATH. Tests should use <see cref="TryCreate"/> and skip when null.
/// </summary>
internal static class EsbuildTestEnvironment
{
    public static EsbuildTranspiler? TryCreate()
    {
        var binary = ResolveBinary();
        if (binary is null) return null;
        return new EsbuildTranspiler(new ScriptingToolchainOptions { EsbuildPath = binary });
    }

    private static string? ResolveBinary()
    {
        var binaryName = OperatingSystem.IsWindows() ? "esbuild.exe" : "esbuild";

        var envPath = Environment.GetEnvironmentVariable("ORKEON_ESBUILD_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        // Walk up from the test bin folder looking for tools/scripting-esbuild/node_modules/.bin/esbuild
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "tools", "scripting-esbuild", "node_modules", ".bin", binaryName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var d in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(d, binaryName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
