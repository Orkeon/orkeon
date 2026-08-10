using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Compliance.Vfs;
using Orkeon.Scripting.Configuration;

namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Strips TypeScript syntax from a <c>.ork.ts</c> source by piping it through esbuild
/// (<c>--loader=ts --format=esm --target=es2022</c>). Source maps are out of scope for V1.
/// </summary>
public sealed partial class EsbuildTranspiler : IScriptTranspiler, IDisposable
{
    private static readonly string[] TranspileArgs = ["--loader=ts", "--format=esm", "--target=es2022"];

    private static readonly string[] BundleArgs =
    [
        "--bundle",
        "--loader:.ts=ts",
        "--loader:.tsx=tsx",
        "--loader:.js=js",
        "--loader:.mjs=js",
        "--format=esm",
        "--platform=neutral",
        "--target=es2022",
    ];

    private readonly ScriptingToolchainOptions _options;
    private readonly ILogger<EsbuildTranspiler> _logger;
    private readonly SemaphoreSlim _resolveLock = new(1, 1);
    private string? _resolvedBinary;

    /// <summary>
    /// Creates a transpiler bound to the supplied toolchain options.
    /// </summary>
    public EsbuildTranspiler(
        IOptions<ScriptingToolchainOptions> options,
        ILogger<EsbuildTranspiler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? NullLogger<EsbuildTranspiler>.Instance;
    }

    /// <summary>
    /// Convenience constructor for tests and DI-less hosts.
    /// </summary>
    public EsbuildTranspiler(ScriptingToolchainOptions? options = null)
        : this(Microsoft.Extensions.Options.Options.Create(options ?? new ScriptingToolchainOptions()), null)
    {
    }

    /// <summary>
    /// Transpiles <paramref name="tsSource"/> to JavaScript by piping it through esbuild's
    /// <c>--loader=ts</c> mode (TypeScript syntax stripped, ESM output, ES2022 target).
    /// </summary>
    /// <exception cref="EsbuildNotFoundException">If the esbuild binary cannot be located.</exception>
    /// <exception cref="EsbuildTranspileException">If esbuild rejects the source.</exception>
    public Task<string> TranspileAsync(string tsSource, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tsSource);
        return RunEsbuildAsync(
            stdinSource: tsSource,
            entryPath: null,
            extraArgs: TranspileArgs,
            ct);
    }

    /// <summary>
    /// Bundles the script at <paramref name="physicalPath"/> into a single self-contained
    /// JavaScript blob, resolving any relative <c>import</c>/<c>export</c> statements
    /// against the entry file's directory.
    /// </summary>
    /// <remarks>
    /// Uses <c>--format=esm</c> because IIFE refuses top-level <c>await</c> at bundle time,
    /// and most Orkeon scripts use it. After bundling, the entry's own imports are inlined
    /// so the output is a flat script — Jint script mode rejects the top-level await on
    /// first try and <see cref="ScriptHost"/> re-evaluates inside an async IIFE wrapper.
    /// The <c>globalThis.crew = …</c> handoff still works because nothing in the wrapper
    /// shadows <c>globalThis</c>.
    /// </remarks>
    /// <exception cref="EsbuildNotFoundException">If the esbuild binary cannot be located.</exception>
    /// <exception cref="EsbuildTranspileException">If esbuild rejects the source.</exception>
    public Task<string> BundleFromFileAsync(string physicalPath, string source, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);
        // source is read by ScriptHost via VFS for version-directive validation; esbuild
        // re-reads from disk because it needs the path to resolve relative imports.
        _ = source;
        return RunEsbuildAsync(
            stdinSource: null,
            entryPath: physicalPath,
            extraArgs: BundleArgs,
            ct);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort process cleanup: killing the timed-out esbuild process tree must not mask the EsbuildTranspileException that reports the timeout to the caller.")]
    private async Task<string> RunEsbuildAsync(
        string? stdinSource,
        string? entryPath,
        IReadOnlyList<string> extraArgs,
        CancellationToken ct)
    {
        var binary = await ResolveBinaryAsync(ct).ConfigureAwait(false);

        var psi = new ProcessStartInfo
        {
            FileName = binary,
            RedirectStandardInput = stdinSource is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (entryPath is not null)
            psi.ArgumentList.Add(entryPath);
        foreach (var arg in extraArgs)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start esbuild process at '{binary}'.");

        if (stdinSource is not null)
        {
            await proc.StandardInput.WriteAsync(stdinSource.AsMemory(), ct).ConfigureAwait(false);
            proc.StandardInput.Close();
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = new CancellationTokenSource(_options.EsbuildTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new EsbuildTranspileException(
                $"esbuild timed out after {_options.EsbuildTimeout.TotalSeconds:N0}s.", -1);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            var msg = string.IsNullOrWhiteSpace(stderr)
                ? $"esbuild failed with exit code {proc.ExitCode}."
                : stderr.Trim();
            throw new EsbuildTranspileException(msg, proc.ExitCode);
        }

        return stdout;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code", Justification = "Double-checked locking: the second null check runs after awaiting the lock, by which point another thread may have set _resolvedBinary; the analyzer cannot model the cross-thread mutation.")]
    private async Task<string> ResolveBinaryAsync(CancellationToken ct)
    {
        if (_resolvedBinary is not null) return _resolvedBinary;
        await _resolveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_resolvedBinary is not null) return _resolvedBinary;
            _resolvedBinary = ResolveBinary();
            LogResolvedBinary(_resolvedBinary);
            return _resolvedBinary;
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    /// <summary>
    /// Locates the esbuild binary (config → env → bundled → repo-local → PATH). Exposed
    /// <see langword="internal"/> so <c>orkeon doctor</c> reports the exact same resolution
    /// the transpiler would use instead of duplicating the chain (WIN-03; the CLI assembly
    /// is covered by <c>InternalsVisibleTo("orkeon")</c>).
    /// </summary>
    /// <exception cref="EsbuildNotFoundException">If the esbuild binary cannot be located.</exception>
    [SuppressVfsCompliance("OUT-OF-SCOPE: probes external toolchain (esbuild) binary location, not a VFS mount.")]
    internal string ResolveBinary()
    {
        var binaryName = OperatingSystem.IsWindows() ? "esbuild.exe" : "esbuild";

        // 1) Configuration override.
        if (!string.IsNullOrWhiteSpace(_options.EsbuildPath) && File.Exists(_options.EsbuildPath))
            return _options.EsbuildPath;

        // 2) Environment variable.
        var envPath = Environment.GetEnvironmentVariable("ORKEON_ESBUILD_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        // 3) Binary bundled next to the host process (NuGet pack target / build copy).
        var bundled = Path.Combine(AppContext.BaseDirectory, "esbuild-bin", binaryName);
        if (File.Exists(bundled))
            return bundled;

        // 4) Repo-local checked-in copy: walk up from the runtime base directory looking
        //    for both the legacy `node_modules/esbuild/bin/{binaryName}` layout AND the
        //    modern platform-namespaced `node_modules/@esbuild/{rid}/[bin/]{binaryName}`
        //    layout. The legacy path is what older esbuild installs (and Linux native
        //    installs that copy the binary directly) ship; the platform-namespaced path
        //    is what npm produces on Windows (`@esbuild/win32-x64/esbuild.exe`, no `bin/`
        //    subdir) and on macOS/Linux for newer esbuild releases.
        var repoLocal = FindRepoLocalEsbuild(binaryName);
        if (repoLocal is not null)
            return repoLocal;

        // 5) PATH lookup.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, binaryName);
            if (File.Exists(candidate))
                return candidate;
        }

        var rid = GetEsbuildPlatformRid();
        var ridHint = rid is null ? "unknown-platform" : rid;
        throw new EsbuildNotFoundException(
            "Esbuild binary not found. Tried (in order): " +
            $"config '{ScriptingToolchainOptions.SectionName}:EsbuildPath', " +
            "env 'ORKEON_ESBUILD_PATH', " +
            $"bundled '{bundled}', " +
            $"repo-local 'tools/scripting-esbuild/node_modules/' " +
            $"(probed legacy 'esbuild/bin/{binaryName}' and platform-namespaced '@esbuild/{ridHint}/...'), " +
            "PATH lookup. " +
            "Install esbuild ('npm install -g esbuild') or set the binary path explicitly.");
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: walks parent directories looking for a checked-in toolchain binary, not a VFS mount.")]
    private static string? FindRepoLocalEsbuild(string binaryName)
    {
        var candidates = BuildRepoLocalCandidates(
            binaryName,
            platformRid: GetEsbuildPlatformRid(),
            windowsPackageLayout: OperatingSystem.IsWindows());

        // Probe a few ancestors (cwd + AppContext.BaseDirectory) and walk up to the repo root.
        var seeds = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var seed in seeds)
        {
            var dir = new DirectoryInfo(seed);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                foreach (var relative in candidates)
                {
                    var parts = new string[relative.Length + 1];
                    parts[0] = dir.FullName;
                    Array.Copy(relative, 0, parts, 1, relative.Length);
                    var candidate = Path.Combine(parts);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Builds the repo-relative candidate paths probed under each ancestor directory when
    /// looking for a checked-in esbuild binary. Exposed <see langword="internal"/> so the
    /// Windows layout can be exercised from a Linux test run (and vice-versa) without
    /// relying on the current host's platform.
    /// </summary>
    /// <param name="binaryName">The OS-specific binary name (<c>esbuild</c> or <c>esbuild.exe</c>).</param>
    /// <param name="platformRid">
    /// The esbuild platform package RID (e.g. <c>win32-x64</c>, <c>linux-x64</c>,
    /// <c>darwin-arm64</c>), or <see langword="null"/> if the current platform cannot be
    /// resolved — in which case only the legacy layout is probed.
    /// </param>
    /// <param name="windowsPackageLayout">
    /// <see langword="true"/> when the npm package places the binary at the package root
    /// (Windows: <c>@esbuild/win32-x64/esbuild.exe</c>); <see langword="false"/> when it
    /// lives under a <c>bin/</c> subdirectory (macOS/Linux: <c>@esbuild/linux-x64/bin/esbuild</c>).
    /// </param>
    internal static IReadOnlyList<string[]> BuildRepoLocalCandidates(
        string binaryName,
        string? platformRid,
        bool windowsPackageLayout)
    {
        var candidates = new List<string[]>();

        // PREFERRED layout — platform-namespaced native binary. This is the actual
        // Go-built esbuild executable (no Node dependency). Modern esbuild npm
        // packages place it under @esbuild/{rid}/[bin/]{binaryName}.
        if (platformRid is not null)
        {
            var rooted = new List<string> { "tools", "scripting-esbuild", "node_modules", "@esbuild", platformRid };
            if (!windowsPackageLayout)
                rooted.Add("bin");
            rooted.Add(binaryName);
            candidates.Add(rooted.ToArray());
        }

        // FALLBACK layout — legacy wrapper. NOTE: on POSIX, this file is a
        // Node.js shim ("#!/usr/bin/env node ...") that re-execs the native
        // binary, so it requires Node to be installed. We probe it only after
        // the platform-namespaced binary to avoid breaking hosts without Node.
        candidates.Add(new[] { "tools", "scripting-esbuild", "node_modules", "esbuild", "bin", binaryName });

        return candidates;
    }

    /// <summary>
    /// Resolves the npm package RID for esbuild's platform-namespaced binary
    /// (e.g. <c>@esbuild/win32-x64</c>) on the current host. Returns <see langword="null"/>
    /// when the OS or process architecture is not one esbuild publishes a package for.
    /// </summary>
    internal static string? GetEsbuildPlatformRid()
    {
        string? os = null;
        if (OperatingSystem.IsWindows()) os = "win32";
        else if (OperatingSystem.IsMacOS()) os = "darwin";
        else if (OperatingSystem.IsLinux()) os = "linux";
        if (os is null) return null;

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "ia32",
            _ => null,
        };
        if (arch is null) return null;

        return $"{os}-{arch}";
    }

    /// <summary>Releases the semaphore guarding esbuild binary resolution.</summary>
    public void Dispose() => _resolveLock.Dispose();

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Resolved esbuild binary at {Path}")]
    private partial void LogResolvedBinary(string path);
}
