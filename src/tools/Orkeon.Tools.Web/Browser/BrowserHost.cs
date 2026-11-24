using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Browser;

/// <summary>What an engine starts: the program, its arguments and profile, and how to reach its channel.</summary>
internal sealed record BrowserLaunch
{
    /// <summary>The browser's name, as errors write it.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Physical path of the program.</summary>
    public required string Executable { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Variables added to the environment the browser inherits.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>The endpoint, when the program announces none: the one it was told to listen at.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>The endpoint a line of the standard error announces, or <c>null</c>.</summary>
    public required Func<string, Uri?> ReadEndpoint { get; init; }

    /// <summary>Opens the BiDi channel of the browser listening at the endpoint.</summary>
    public required Func<Uri, CancellationToken, Task<IBiDiTransport>> ConnectAsync { get; init; }

    /// <summary>Removes the profile once the browser has ended, when it is a disposable one.</summary>
    public required Func<Task> RemoveProfileAsync { get; init; }

    public required BrowserProfileChoice Profile { get; init; }

    public required bool Headless { get; init; }

    /// <summary>How long the browser is given to announce its endpoint.</summary>
    public required TimeSpan Startup { get; init; }

    /// <summary>How long a browser asked to close is given to leave before it is killed.</summary>
    public required TimeSpan ExitGrace { get; init; }
}

/// <summary>
/// What every engine shares: finding the program on the host, choosing between a window and none, and a
/// child process that announces its remote protocol on its standard error.
/// </summary>
internal static partial class BrowserHost
{
    private const int ErrorTailLines = 5;

    /// <summary>The error of a program that is not on the host.</summary>
    public static FirefoxException Missing(string displayName, FirefoxToolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new FirefoxException(string.IsNullOrWhiteSpace(options.ExecutablePath)
            ? $"{displayName} was not found on this machine: install it, or set {options.Section}:{nameof(FirefoxToolOptions.ExecutablePath)}."
            : $"{displayName} was not found at '{options.ExecutablePath}' ({options.Section}:{nameof(FirefoxToolOptions.ExecutablePath)}).");
    }

    /// <summary>Whether the browser starts without a window on this host; a fallback for want of a display is logged.</summary>
    public static bool ResolveHeadless(
        FirefoxToolOptions options, string displayName, Func<string, string?> readEnvironment, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var headless = ResolveHeadless(options.Headless, OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD(), readEnvironment);
        if (headless && !options.Headless && logger is not null)
            LogHeadlessFallback(logger, displayName);
        return headless;
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
    /// The program to start: <paramref name="configured"/> when it is a path, else its name - or each of
    /// <paramref name="names"/> in turn - looked up on <paramref name="pathVariable"/>, then <paramref name="knownPaths"/>.
    /// </summary>
    public static string? Locate(
        string? configured, IReadOnlyList<string> names, string? pathVariable, IEnumerable<string> knownPaths,
        Func<string, bool> fileExists, bool windows)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(knownPaths);
        ArgumentNullException.ThrowIfNull(fileExists);

        var wanted = configured?.Trim();
        if (!string.IsNullOrEmpty(wanted)
            && (wanted.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || wanted.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)))
        {
            return fileExists(wanted) ? wanted : null;
        }

        var folders = (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathRooted)
            .ToList();
        var onPath = (string.IsNullOrEmpty(wanted) ? names : [wanted])
            .Select(name => windows && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name)
            .SelectMany(name => folders.Select(directory => Path.Combine(directory, name)))
            .FirstOrDefault(fileExists);
        if (onPath is not null)
            return onPath;

        return string.IsNullOrEmpty(wanted) ? knownPaths.FirstOrDefault(fileExists) : null;
    }

    /// <summary>Starts the browser and opens its channel; a start that fails ends the process and removes a disposable profile.</summary>
    /// <exception cref="FirefoxException">The browser did not start, did not announce its endpoint in time, or refused the connection.</exception>
    [SuppressMessage("Reliability", "CA2000", Justification = "The process and the channel are handed to the browser returned, which ends them; a start that fails ends them in the catch below.")]
    public static async Task<IBrowserProcess> LaunchAsync(BrowserLaunch launch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        Process? process = null;
        try
        {
            var announced = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            process = Start(launch, announced);
            if (launch.Endpoint is not null)
                announced.TrySetResult(launch.Endpoint);

            Uri endpoint;
            try
            {
                endpoint = await announced.Task.WaitAsync(launch.Startup, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new FirefoxException(
                    $"{launch.DisplayName} did not open its remote protocol within {launch.Startup.TotalSeconds:0.#} s.", ex);
            }

            try
            {
                var transport = await launch.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                return new Child(process, launch, transport);
            }
            catch (Exception ex) when (ex is not FirefoxException && !cancellationToken.IsCancellationRequested)
            {
                throw new FirefoxException($"The connection to {launch.DisplayName} at {endpoint} failed ({ex.Message}).", ex);
            }
        }
        catch
        {
            await StopAsync(process, launch.RemoveProfileAsync, TimeSpan.Zero).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Starts the browser. Its standard streams are all redirected - a child that shared the host's
    /// terminal would write over it - and read to the end: the error stream gives the endpoint.
    /// </summary>
    private static Process Start(BrowserLaunch launch, TaskCompletionSource<Uri> announced)
    {
        var startInfo = new ProcessStartInfo(launch.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in launch.Arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var (name, value) in launch.Environment)
            startInfo.Environment[name] = value;

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

            if (launch.ReadEndpoint(e.Data) is { } endpoint)
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

            _ = ReportExitAsync(launch.DisplayName, code, tail, errorsRead.Task, announced);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new FirefoxException($"{launch.DisplayName} could not be started from '{launch.Executable}' ({ex.Message}).", ex);
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
        string displayName, int code, Queue<string> tail, Task errorsRead, TaskCompletionSource<Uri> announced)
    {
        if (announced.Task.IsCompleted)
            return;

        // WaitAsync rather than WhenAny + Delay: a Delay that lost the race keeps its timer (CA2027).
        try
        {
            await errorsRead.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The stream is still open: what arrived is enough.
        }

        string said;
        lock (tail)
            said = string.Join(" | ", tail);
        announced.TrySetException(new FirefoxException(
            $"{displayName} ended (exit code {code}) before opening its remote protocol"
            + (said.Length == 0 ? "." : $": {said}")));
    }

    [SuppressMessage("Design", "CA1031", Justification = "Best-effort end of a child process: one that cannot be waited for or killed has already gone.")]
    private static async Task StopAsync(Process? process, Func<Task> removeProfile, TimeSpan grace)
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

        await removeProfile().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The browser tool starts {Engine} headless: this machine has no display (neither DISPLAY nor WAYLAND_DISPLAY is set).")]
    private static partial void LogHeadlessFallback(ILogger logger, string engine);

    private sealed class Child(Process process, BrowserLaunch launch, IBiDiTransport transport) : IBrowserProcess
    {
        private int _disposed;

        public IBiDiTransport Transport => transport;

        public bool Headless => launch.Headless;

        public BrowserProfileChoice Profile => launch.Profile;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // The channel is closed first when its owner did not: a browser nobody talks to has no use.
            await transport.DisposeAsync().ConfigureAwait(false);
            await StopAsync(process, launch.RemoveProfileAsync, launch.ExitGrace).ConfigureAwait(false);
        }
    }
}
