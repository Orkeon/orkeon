using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// Starts Firefox as a child process with its own profile and a remote-debugging port the system picks,
/// reads the WebDriver BiDi endpoint the browser announces on its standard error, and connects to it.
/// </summary>
internal sealed partial class FirefoxProcessLauncher : IFirefoxLauncher
{
    private const int ErrorTailLines = 5;

    private readonly FirefoxToolOptions _options;
    private readonly ILogger? _logger;
    private readonly Func<string, string?> _readEnvironment;

    public FirefoxProcessLauncher(
        FirefoxToolOptions options, ILogger? logger = null, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>How long a browser asked to close is given to leave before it is killed.</summary>
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The arguments Firefox is started with.</summary>
    public static IReadOnlyList<string> BuildArguments(string profileDirectory, bool headless)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileDirectory);
        List<string> arguments =
        [
            // Port 0: the system picks a free one, announced on the standard error.
            "--remote-debugging-port", "0",
            "--profile", profileDirectory,
            "--no-remote", "--new-instance",
        ];
        if (headless)
            arguments.Add("--headless");
        arguments.Add("about:blank");
        return arguments;
    }

    /// <summary>
    /// <c>true</c> when the browser must start without a window: asked for, or no display to open one
    /// on (Linux with neither <c>DISPLAY</c> nor <c>WAYLAND_DISPLAY</c>).
    /// </summary>
    public static bool ResolveHeadless(bool configured, bool needsDisplay, Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        return configured
            || (needsDisplay
                && string.IsNullOrEmpty(readEnvironment("DISPLAY"))
                && string.IsNullOrEmpty(readEnvironment("WAYLAND_DISPLAY")));
    }

    /// <summary>
    /// The program to start: <paramref name="configured"/> when it is a path, else its name - or
    /// <c>firefox</c> - looked up on <paramref name="pathVariable"/>, then <paramref name="knownPaths"/>.
    /// </summary>
    public static string? Locate(
        string? configured, string? pathVariable, IEnumerable<string> knownPaths, Func<string, bool> fileExists, bool windows)
    {
        ArgumentNullException.ThrowIfNull(knownPaths);
        ArgumentNullException.ThrowIfNull(fileExists);

        var name = string.IsNullOrWhiteSpace(configured) ? "firefox" : configured.Trim();
        if (name.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || name.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return fileExists(name) ? name : null;
        }

        if (windows && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";

        var onPath = (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathRooted)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(fileExists);
        if (onPath is not null)
            return onPath;

        return string.IsNullOrWhiteSpace(configured) ? knownPaths.FirstOrDefault(fileExists) : null;
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The process and the channel are handed to the browser returned, which ends them; a start that fails ends them in the catch below.")]
    public async Task<IFirefoxBrowser> LaunchAsync(CancellationToken cancellationToken)
    {
        var executable = LocateOnHost()
            ?? throw new FirefoxException(string.IsNullOrWhiteSpace(_options.ExecutablePath)
                ? $"Firefox was not found on this machine: install it, or set {FirefoxToolOptions.SectionName}:{nameof(FirefoxToolOptions.ExecutablePath)}."
                : $"Firefox was not found at '{_options.ExecutablePath}' ({FirefoxToolOptions.SectionName}:{nameof(FirefoxToolOptions.ExecutablePath)}).");

        var headless = ResolveHeadless(_options.Headless, OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD(), _readEnvironment);
        if (headless && !_options.Headless && _logger is not null)
            LogHeadlessFallback(_logger);

        var profile = FirefoxProfile.Create(FirefoxProfile.ResolveRoot(_options.ProfileRoot));
        Process? process = null;
        try
        {
            var startup = TimeSpan.FromSeconds(_options.StartupTimeoutSeconds);
            var announced = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            process = Start(executable, BuildArguments(profile.Directory, headless), announced);

            Uri endpoint;
            try
            {
                endpoint = await announced.Task.WaitAsync(startup, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new FirefoxException(
                    $"Firefox did not open its remote protocol within {startup.TotalSeconds:0.#} s.", ex);
            }

            try
            {
                var transport = await WebSocketBiDiTransport.ConnectAsync(BiDiEndpoint.Session(endpoint), cancellationToken)
                    .ConfigureAwait(false);
                return new Browser(process, profile, transport, headless, ExitGrace);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new FirefoxException($"The connection to Firefox at {endpoint} failed ({ex.Message}).", ex);
            }
        }
        catch
        {
            await StopAsync(process, profile, TimeSpan.Zero).ConfigureAwait(false);
            throw;
        }
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for the Firefox program, as the docker sandbox probes for docker; not a VFS path.")]
    private string? LocateOnHost()
    {
        // OUT-OF-SCOPE: binary discovery on the host, not a VFS mount.
        string[] known = OperatingSystem.IsWindows()
            ?
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe"),
            ]
            : OperatingSystem.IsMacOS()
                ? ["/Applications/Firefox.app/Contents/MacOS/firefox"]
                : ["/usr/bin/firefox", "/snap/bin/firefox", "/usr/local/bin/firefox"];
        return Locate(_options.ExecutablePath, _readEnvironment("PATH"), known, File.Exists, OperatingSystem.IsWindows());
    }

    /// <summary>
    /// Starts the browser. Its standard streams are all redirected - a child that shared the host's
    /// terminal would write over it - and read to the end: the error stream gives the endpoint.
    /// </summary>
    private static Process Start(string executable, IReadOnlyList<string> arguments, TaskCompletionSource<Uri> announced)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["MOZ_CRASHREPORTER_DISABLE"] = "1";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var tail = new Queue<string>();
        var errorsRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorsRead.TrySetResult();
                return;
            }

            if (BiDiEndpoint.TryParse(e.Data, out var endpoint))
            {
                announced.TrySetResult(endpoint);
                return;
            }

            lock (tail)
            {
                if (tail.Count == ErrorTailLines)
                    tail.Dequeue();
                tail.Enqueue(e.Data);
            }
        };
        process.OutputDataReceived += static (_, _) => { };
        process.Exited += (sender, e) =>
        {
            int code;
            try
            {
                code = ((Process)sender!).ExitCode;
            }
            catch (InvalidOperationException)
            {
                // Disposed meanwhile: the start was given up.
                return;
            }

            _ = ReportExitAsync(code, tail, errorsRead.Task, announced);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new FirefoxException($"Firefox could not be started from '{executable}' ({ex.Message}).", ex);
        }

        process.StandardInput.Close();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return process;
    }

    /// <summary>
    /// Fails the start with what the browser said last. Its last lines may still be on their way when
    /// it exits: they are waited for an instant - a child that outlives it can hold the stream open.
    /// </summary>
    private static async Task ReportExitAsync(
        int code, Queue<string> tail, Task errorsRead, TaskCompletionSource<Uri> announced)
    {
        if (announced.Task.IsCompleted)
            return;

        await Task.WhenAny(errorsRead, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        string said;
        lock (tail)
            said = string.Join(" | ", tail);
        announced.TrySetException(new FirefoxException(
            $"Firefox ended (exit code {code}) before opening its remote protocol"
            + (said.Length == 0 ? "." : $": {said}")));
    }

    [SuppressMessage("Design", "CA1031", Justification = "Best-effort end of a child process: one that cannot be waited for or killed has already gone.")]
    private static async Task StopAsync(Process? process, FirefoxProfile profile, TimeSpan grace)
    {
        if (process is not null)
        {
            try
            {
                if (!process.HasExited && grace > TimeSpan.Zero)
                {
                    using var waiting = new CancellationTokenSource(grace);
                    try
                    {
                        await process.WaitForExitAsync(waiting.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Still there: killed below.
                    }
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Already gone.
            }

            process.Dispose();
        }

        await profile.RemoveAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The firefox tool starts its browser headless: this machine has no display (neither DISPLAY nor WAYLAND_DISPLAY is set).")]
    private static partial void LogHeadlessFallback(ILogger logger);

    private sealed class Browser(
        Process process, FirefoxProfile profile, IBiDiTransport transport, bool headless, TimeSpan exitGrace) : IFirefoxBrowser
    {
        private int _disposed;

        public IBiDiTransport Transport { get; } = transport;

        public bool Headless { get; } = headless;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // The channel is closed first when its owner did not: a browser nobody talks to has no use.
            await Transport.DisposeAsync().ConfigureAwait(false);
            await StopAsync(process, profile, exitGrace).ConfigureAwait(false);
        }
    }
}
