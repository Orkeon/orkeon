using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Compliance.Vfs;
using Orkeon.Scripting.Configuration;

namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Type-checks a <c>.ork.ts</c> script with the TypeScript compiler against the typings this build
/// embeds (the <c>orkeon.d.ts</c> roll-up, <c>test.d.ts</c> included), the way
/// <c>scripts/check-scripting-typings.sh</c> checks the shipped examples.
/// </summary>
/// <remarks>
/// <para>
/// <c>tsc</c> is an external process: it reads physical paths and writes them in its diagnostics, and
/// it does not know the <c>/// &lt;reference orkeon-script="1.0" /&gt;</c> pragma (TS1084, which
/// also hides every other error). So a scratch folder is written in bootstrap, outside the
/// VFS (<c>EXCEPTION-BOOTSTRAP</c>): the roll-up, a generated <c>tsconfig.json</c> and a copy of the
/// script with its two pragma forms blanked (same line count). The copy sits under a mirror of the
/// script's own path and <c>rootDirs</c> merges the mirror with the real tree, so the script's
/// relative imports still resolve. Every path of the compiler's output is then mapped back to the
/// virtual path of the script (<see cref="TscOutputParser"/>); a physical path never leaves this class.
/// </para>
/// </remarks>
public sealed partial class TscTypeChecker
{
    /// <summary>The virtual folder the roll-up and the scratch files are reported under.</summary>
    public const string VirtualWorkFolder = "/typecheck";

    private const string RollupResource = "Orkeon.Scripting.Typings.orkeon.d.ts";
    private const string RollupFileName = "orkeon.d.ts";
    private const string MirrorFolder = "m";

    [GeneratedRegex(@"^/// <reference (?:orkeon-script|path)=?.*$", RegexOptions.CultureInvariant)]
    private static partial Regex PragmaLine();

    [GeneratedRegex(@"import\(""(?<p>[^""]+)""\)", RegexOptions.CultureInvariant)]
    private static partial Regex ImportedPath();

    private readonly ScriptingToolchainOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates a checker for <paramref name="options"/>.</summary>
    public TscTypeChecker(IOptions<ScriptingToolchainOptions> options, ILogger<TscTypeChecker>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? NullLogger<TscTypeChecker>.Instance;
    }

    /// <summary>Convenience constructor for tests and DI-less hosts.</summary>
    public TscTypeChecker(ScriptingToolchainOptions? options = null)
        : this(Microsoft.Extensions.Options.Options.Create(options ?? new ScriptingToolchainOptions()), null)
    {
    }

    /// <summary>The one way a host builds a checker: binds <c>Orkeon:Scripting:Toolchain</c> (<c>TscPath</c>, <c>TypeCheckTimeout</c>).</summary>
    public static TscTypeChecker Create(IConfiguration? configuration, ILogger<TscTypeChecker>? logger = null)
        => new(Microsoft.Extensions.Options.Options.Create(ScriptingToolchainOptions.FromConfiguration(configuration)), logger);

    /// <summary>
    /// Locates <c>tsc</c> (config, else the <c>tools/scripting-typecheck</c> bootstrap above the install directory, else <c>PATH</c>; never the working directory).
    /// Exposed so <c>orkeon doctor</c> reports the resolution the checker would use.
    /// </summary>
    /// <exception cref="TscNotFoundException">If <c>tsc</c> cannot be located.</exception>
    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the location of an external toolchain binary (tsc), not a VFS mount.")]
    public string ResolveBinary()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "tsc.cmd", "tsc.exe" } : new[] { "tsc" };

        // An explicit setting is the only candidate: a path that is wrong is the operator's to fix. It is absolute:
        // a relative one would resolve against the working directory, whose checkout may be untrusted.
        if (!string.IsNullOrWhiteSpace(_options.TscPath))
        {
            if (!Path.IsPathFullyQualified(_options.TscPath))
            {
                throw new TscNotFoundException(
                    $"TypeScript compiler not found: '{ScriptingToolchainOptions.SectionName}:TscPath' is a relative path; it must be absolute, never resolved against the working directory. {InstallHint}");
            }

            return File.Exists(_options.TscPath)
                ? _options.TscPath
                : throw new TscNotFoundException(
                    $"TypeScript compiler not found: '{ScriptingToolchainOptions.SectionName}:TscPath' names a file that does not exist. {InstallHint}");
        }

        // The install directory and its ancestors only (a checkout's build output, a tool's layout): never the
        // working directory, whose checkout may be untrusted and would have `orkeon run --validate` execute its binary.
        foreach (var seed in new[] { AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(seed);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                foreach (var name in names)
                {
                    var candidate = Path.Combine(dir.FullName, "tools", "scripting-typecheck", "node_modules", ".bin", name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new TscNotFoundException(
            "TypeScript compiler (tsc) not found. Tried (in order): " +
            $"config '{ScriptingToolchainOptions.SectionName}:TscPath', " +
            "'tools/scripting-typecheck/node_modules/.bin/' above the install directory, PATH lookup. " + InstallHint);
    }

    private const string InstallHint =
        "Bootstrap it with `npm --prefix tools/scripting-typecheck install --prefer-offline` in a checkout, or install it " +
        "(`npm install -g typescript`), or point Orkeon:Scripting:Toolchain:TscPath at it, or set Orkeon:Scripting:Toolchain:TypeCheck to `off`.";

    /// <summary>
    /// Type-checks the script at <paramref name="physicalScriptPath"/>; every path of the result is under
    /// <paramref name="virtualScriptPath"/>'s folder.
    /// </summary>
    /// <param name="physicalScriptPath">The script on disk (the host resolved it from the mount that holds it).</param>
    /// <param name="virtualScriptPath">The same script as the VFS names it (<c>/workspace/main.ork.ts</c>).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The diagnostics, empty when the script type-checks.</returns>
    /// <exception cref="TscNotFoundException"><c>tsc</c> is missing or cannot be started.</exception>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: writes the scratch folder an external tsc process reads, and reads the script it copies into it; tsc cannot go through the VFS.")]
    public async Task<IReadOnlyList<TypeCheckDiagnostic>> CheckAsync(
        string physicalScriptPath, string virtualScriptPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalScriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualScriptPath);

        var binary = ResolveBinary();
        var script = Path.GetFullPath(physicalScriptPath);
        var scriptDir = Path.GetDirectoryName(script)!;
        var virtualDir = virtualScriptPath[..Math.Max(virtualScriptPath.LastIndexOf('/'), 0)];

        var work = Path.Combine(Path.GetTempPath(), "orkeon-typecheck-" + Guid.NewGuid().ToString("N"));
        var root = Path.GetPathRoot(script)!;
        var mirrorRoot = Path.Combine(work, MirrorFolder);
        // Built before the first write: whatever fails below is worded through it.
        var mapper = new PathMapper(work, mirrorRoot, root, scriptDir, virtualDir);

        IReadOnlyList<TypeCheckDiagnostic> diagnostics;
        int exitCode;
        string output;
        try
        {
            Directory.CreateDirectory(work);
            var mirrored = Path.Combine(mirrorRoot, script[root.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(mirrored)!);

            var source = await File.ReadAllTextAsync(script, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(mirrored, BlankPragmas(source), ct).ConfigureAwait(false);

            var rollup = Path.Combine(work, RollupFileName);
            using (var resource = typeof(TscTypeChecker).Assembly.GetManifestResourceStream(RollupResource)
                ?? throw new InvalidOperationException($"The embedded typings '{RollupResource}' are missing from this build."))
            using (var target = File.Create(rollup))
            {
                await resource.CopyToAsync(target, ct).ConfigureAwait(false);
            }

            var tsconfig = Path.Combine(work, "tsconfig.json");
            await File.WriteAllTextAsync(tsconfig, BuildTsconfig(rollup, mirrored, mirrorRoot, root), ct).ConfigureAwait(false);

            // tsc rejects the pragma of an imported module too (TS1084) and then skips its semantic
            // pass. The modules are only known once tsc resolves them: each file it names is copied,
            // pragmas blanked, under the mirror (rootDirs prefers it to the real one) and tsc runs again.
            (output, exitCode) = await RunAsync(binary, tsconfig, work, ct).ConfigureAwait(false);
            for (var pass = 0; pass < MaxPragmaPasses; pass++)
            {
                if (!await MirrorFilesWithPragmaAsync(output, work, mirrorRoot, root, ct).ConfigureAwait(false))
                    break;
                (output, exitCode) = await RunAsync(binary, tsconfig, work, ct).ConfigureAwait(false);
            }

            diagnostics = TscOutputParser.Parse(output, mapper.MapFile, mapper.MapText);
            // A run that exited 0 found no error: a line with no code is a warning of node itself
            // (a DeprecationWarning), not a finding. On failure such lines stay as context.
            if (exitCode == 0)
                diagnostics = [.. diagnostics.Where(d => d.Code.Length > 0)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TscNotFoundException)
        {
            // The message may carry a physical path (an IO error names its file): it leaves redacted.
            throw new InvalidOperationException("type check failed: " + mapper.MapText(ex.Message));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { /* best-effort scratch cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort scratch cleanup */ }
        }

        // A compiler that died (crash, OOM, signal) says nothing about the script: never "no errors".
        if (exitCode != 0 && !diagnostics.Any(d => d.Code.Length > 0))
        {
            var tail = string.Join('\n', diagnostics.Take(MaxFailureLines).Select(d => d.ToString()));
            throw new InvalidOperationException(
                $"type check failed (exit {exitCode})" + (tail.Length == 0 ? string.Empty : "\n" + tail));
        }

        return diagnostics;
    }

    private const int MaxPragmaPasses = 3;
    private const int MaxFailureLines = 20;

    [GeneratedRegex(@"^(?<p>.+?)\(\d+,\d+\): error TS1084\b", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex PragmaDiagnostic();

    /// <summary>Copies every real file tsc rejected for a pragma under the mirror, blanked; false when there is none.</summary>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: copies the files an external tsc process rejected into its scratch folder; tsc cannot go through the VFS.")]
    private static async Task<bool> MirrorFilesWithPragmaAsync(
        string output, string work, string mirrorRoot, string root, CancellationToken ct)
    {
        var copied = false;
        foreach (Match m in PragmaDiagnostic().Matches(output))
        {
            var file = Path.GetFullPath(m.Groups["p"].Value.Trim(), work);
            if (file.StartsWith(work, StringComparison.OrdinalIgnoreCase) || !file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;

            var copy = Path.Combine(mirrorRoot, file[root.Length..]);
            if (File.Exists(copy) || !File.Exists(file))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            var text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(copy, BlankPragmas(text), ct).ConfigureAwait(false);
            copied = true;
        }

        return copied;
    }

    /// <summary>Blanks the two pragma forms tsc cannot take; the line count is kept so positions stay true.</summary>
    internal static string BlankPragmas(string source)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r');
            if (PragmaLine().IsMatch(trimmed))
                lines[i] = lines[i][trimmed.Length..];
        }

        return string.Join('\n', lines);
    }

    private static string BuildTsconfig(string rollup, string mirroredScript, string mirrorRoot, string root)
    {
        // tsconfig.base.json of tools/scripting-typecheck is the author-facing copy; these are its options.
        static string J(string path) => System.Text.Json.JsonSerializer.Serialize(path.Replace('\\', '/'));
        var sb = new StringBuilder();
        sb.Append("{\"compilerOptions\":{")
          .Append("\"moduleDetection\":\"force\",\"target\":\"ES2022\",\"lib\":[\"ES2022\"],")
          .Append("\"module\":\"ESNext\",\"moduleResolution\":\"bundler\",\"types\":[],")
          .Append("\"strict\":true,\"noEmit\":true,\"allowImportingTsExtensions\":true,")
          .Append("\"forceConsistentCasingInFileNames\":true,")
          .Append("\"rootDirs\":[").Append(J(mirrorRoot)).Append(',').Append(J(root)).Append("]},")
          .Append("\"files\":[").Append(J(rollup)).Append(',').Append(J(mirroredScript)).Append("]}");
        return sb.ToString();
    }

    private async Task<(string Output, int ExitCode)> RunAsync(
        string binary, string tsconfig, string workingDirectory, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = binary,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(tsconfig);
        psi.ArgumentList.Add("--pretty");
        psi.ArgumentList.Add("false");

        Process proc;
        try
        {
            proc = Process.Start(psi)
                ?? throw new TscNotFoundException("The TypeScript compiler could not be started. " + InstallHint);
        }
        catch (Win32Exception ex)
        {
            throw new TscNotFoundException("The TypeScript compiler could not be started (is Node installed?). " + InstallHint, ex);
        }

        using (proc)
        {
            // Not tied to ct: killing the process closes the pipes, which ends the reads.
            var stdout = new Pump(proc.StandardOutput);
            var stderr = new Pump(proc.StandardError);
            using var timeout = new CancellationTokenSource(_options.TypeCheckTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The timeout and the caller's cancellation alike: tsc must not outlive the check,
                // nor the scratch folder it reads.
                try { proc.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already exited */ }
                try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
                catch (TimeoutException) { /* the kill was sent; nothing more to wait for */ }

                if (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                    throw new TimeoutException($"tsc timed out after {_options.TypeCheckTimeout.TotalSeconds:N0}s.");
                throw;
            }

            // A grandchild that inherited the pipes can hold them open after tsc exited: the reads are
            // bounded, and what was read so far is the output.
            try
            {
                await Task.WhenAll(stdout.Done, stderr.Done).WaitAsync(PostExitReadGrace, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException) { /* a grandchild holds the pipes: the output read so far stands */ }
            var text = stdout.Text + stderr.Text;
            LogRan(_logger, binary, proc.ExitCode, null);
            return (text, proc.ExitCode);
        }
    }

    private static readonly TimeSpan PostExitReadGrace = TimeSpan.FromSeconds(2);
    private const int MaxOutputChars = 4 * 1024 * 1024;

    /// <summary>Reads a stream to its end into a bounded buffer that can be read before the end comes.</summary>
    private sealed class Pump
    {
        private readonly StringBuilder _text = new();

        public Pump(StreamReader reader) => Done = ReadAsync(reader);

        public Task Done { get; }

        public string Text
        {
            get { lock (_text) { return _text.ToString(); } }
        }

        private async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[4096];
            try
            {
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
                {
                    lock (_text)
                    {
                        if (_text.Length < MaxOutputChars)
                            _text.Append(buffer, 0, Math.Min(read, MaxOutputChars - _text.Length));
                    }
                }
            }
            catch (ObjectDisposedException) { /* the process was disposed while a grandchild held the pipe */ }
            catch (IOException) { /* the pipe broke: what was read stands */ }
        }
    }

    private static readonly Action<ILogger, string, int, Exception?> LogRan =
        LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(1, "TscRan"), "tsc at {Binary} exited {ExitCode}");

    /// <summary>Maps the physical paths of the scratch folder and of the script's tree to virtual ones.</summary>
    private sealed class PathMapper(string work, string mirrorRoot, string root, string scriptDir, string virtualDir)
    {
        private static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');

        [SuppressVfsCompliance("OUT-OF-SCOPE: normalises a path printed by the external tsc process; nothing is opened.")]
        public string MapFile(string path)
        {
            // tsc prints a path relative to its working directory (the scratch folder) when it can.
            var p = Norm(path);
            if (!Path.IsPathRooted(p))
                p = Norm(Path.GetFullPath(p, work));

            var w = Norm(work);
            var m = Norm(mirrorRoot);
            if (p.StartsWith(m + "/", StringComparison.OrdinalIgnoreCase))
                p = Norm(root) + "/" + p[(m.Length + 1)..];
            else if (p.StartsWith(w + "/", StringComparison.OrdinalIgnoreCase))
                return VirtualWorkFolder + p[w.Length..];

            // Every file is named from the script's folder, the way the VFS would spell it; one above
            // the script keeps its `..`, so no physical segment is ever printed.
            var relative = Norm(Path.GetRelativePath(scriptDir, p));
            return virtualDir + "/" + relative;
        }

        public string MapText(string text)
        {
            var mapped = ImportedPath().Replace(text, match => $"import(\"{MapFile(match.Groups["p"].Value)}\")");
            foreach (var (physical, virtualPrefix) in new[]
            {
                (Norm(mirrorRoot) + "/" + Norm(scriptDir)[Norm(root).Length..].TrimStart('/'), virtualDir),
                (Norm(work), VirtualWorkFolder),
                (Norm(scriptDir), virtualDir),
            })
            {
                mapped = mapped.Replace(physical, virtualPrefix, StringComparison.OrdinalIgnoreCase);
            }

            // Whatever absolute path is left is physical (a node install, a temp folder): it never leaves.
            return AbsolutePath().Replace(mapped, match => IsVirtual(match.Value) ? match.Value : RedactedPath);
        }

        private bool IsVirtual(string path)
        {
            var v = virtualDir.TrimEnd('/');
            return path.StartsWith(VirtualWorkFolder + "/", StringComparison.Ordinal)
                || path == VirtualWorkFolder
                || (v.Length > 0 && (path == v || path.StartsWith(v + "/", StringComparison.Ordinal)))
                || path.StartsWith("/workspace/", StringComparison.Ordinal)
                || path.StartsWith("/output/", StringComparison.Ordinal);
        }
    }

    private const string RedactedPath = "<path>";

    // An absolute path of two segments or more, or a drive path, standing alone in the text.
    [GeneratedRegex(@"(?<![\w./:\\-])(?:/[\w.@+~-]+){2,}|(?<![\w])[A-Za-z]:[\\/][^\s'""():]+", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePath();
}
