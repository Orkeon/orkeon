using System.Diagnostics;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Code.Constants.Shell;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Code;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Strongly-typed request for <see cref="ShellCommandTool"/>.</summary>
public sealed record ShellCommandRequest
{
    /// <summary>Gets the shell command to execute.</summary>
    [FieldSchema(Description = "Shell command to execute", IsRequired = true, Example = "echo hello")]
    public string Command { get; init; } = "";

    /// <summary>Gets the working directory for command execution.</summary>
    [FieldSchema(Description = "Working directory for command execution", IsRequired = false, Default = ".")]
    public string WorkingDirectory { get; init; } = ".";

    /// <summary>Gets the timeout in seconds.</summary>
    [FieldSchema(Description = "Timeout in seconds", IsRequired = false, Default = "30")]
    public int TimeoutSeconds { get; init; } = ShellDefaults.DefaultTimeoutSeconds;
}

/// <summary>Strongly-typed response for <see cref="ShellCommandTool"/>.</summary>
public sealed record ShellCommandResponse
{
    /// <summary>Gets the process exit code.</summary>
    [ReturnSchema(Description = "Process exit code", Example = 0)]
    public int ExitCode { get; init; }

    /// <summary>Gets the standard output.</summary>
    [ReturnSchema(Description = "Standard output", Example = "hello")]
    public string Stdout { get; init; } = "";

    /// <summary>Gets the standard error output.</summary>
    [ReturnSchema(Description = "Standard error output", Example = "")]
    public string Stderr { get; init; } = "";

    /// <summary>Gets whether the command completed within the timeout.</summary>
    [ReturnSchema(Description = "Whether the command completed within the timeout", Example = true)]
    public bool Completed { get; init; }
}

// ── Tool implementation ────────────────────────────────────────────────

/// <summary>
/// Tool for executing shell commands with allowlist security,
/// configurable timeout, and output capture.
/// Commands are executed directly via <see cref="ProcessStartInfo"/> without
/// shell interpretation. On Windows, shell built-in commands (echo, dir, etc.)
/// are routed through <c>cmd.exe /c</c> with arguments passed via
/// <see cref="ProcessStartInfo.ArgumentList"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope of the no-shell-injection guarantee.</b> The forbidden-operator
/// check (<c>;</c>, <c>|</c>, <c>$(</c>, backticks, <c>\n</c>, …) and the use of
/// <see cref="ProcessStartInfo.ArgumentList"/> prevent <i>shell metacharacter
/// injection</i> only. They do <b>not</b> provide OS confinement: there is no
/// sandbox, namespace, seccomp or network isolation. The executed process runs
/// with the privileges of the host process.
/// </para>
/// <para>
/// <b>Interpreters are RCE-equivalent.</b> The default allowlist contains
/// read-only commands only. General-purpose interpreters/build tools
/// (<c>node</c>, <c>dotnet</c>, <c>npm</c>, <c>find</c>) are excluded by default
/// because they execute arbitrary host code without any forbidden operator
/// (e.g. <c>node -e "require('child_process').execSync('id')"</c>). They can be
/// re-enabled with the explicit opt-in (<c>allowInterpreters: true</c>), which
/// emits a security warning. In that mode this tool offers <b>no confinement</b>
/// and must be treated as remote-code-execution-equivalent. If you need to run
/// untrusted interpreter code, route it through an isolating
/// <c>ICodeSandbox</c> (Docker <c>--network=none</c>) instead.
/// </para>
/// </remarks>
[ToolContract("shell_command",
    Name = "shell_command",
    Description = "Execute shell commands with security controls. Supports allowlist/blocklist, timeout, and output capture.",
    Category = "Code Operations")]
public partial class ShellCommandTool : ToolBase<ShellCommandRequest, ShellCommandResponse>
{
    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Execute;

    /// <summary>Maximum number of characters captured from stdout/stderr.</summary>
    public const int MaxOutputLength = 10_000;

    private readonly HashSet<string> _allowedCommands;
    private readonly List<string> _blockedPatterns;
    private readonly IFileSystemService _fileSystem;

    /// <summary>
    /// True when interpreters were re-enabled via opt-in AND no explicit custom
    /// allowlist was supplied. In that case git subcommand restriction is relaxed
    /// (the operator has accepted RCE-equivalent behaviour explicitly).
    /// </summary>
    private readonly bool _interpretersAllowed;

    /// <summary>
    /// Shell operators that must never appear in a command string.
    /// Even though we execute directly (no shell), we reject these to
    /// prevent confusion and make intent clear.
    /// </summary>
    private static readonly string[] s_forbiddenOperators =
    [
        ";",    // command chaining
        "&&",   // logical AND chaining
        "||",   // logical OR chaining
        "|",    // pipe
        "$(",   // command substitution
        "`",    // backtick command substitution
        "\n",   // newline (hidden command)
        "\r",   // carriage return
        "&",    // background execution
        "<",    // input redirection
        ">>",   // append redirection (checked before >)
        ">"     // output redirection
    ];

    /// <summary>
    /// Commands that are built into cmd.exe on Windows and cannot be executed
    /// directly as standalone processes. When the tool detects one of these on
    /// Windows, it routes execution through <c>cmd.exe /c</c>.
    /// </summary>
    private static readonly HashSet<string> s_windowsBuiltins = new(StringComparer.OrdinalIgnoreCase)
    {
        "echo", "dir", "type", "cd", "copy", "del", "ren", "rename",
        "mkdir", "md", "rmdir", "rd", "cls", "set", "ver", "vol"
    };

    /// <summary>
    /// Default set of allowed executable commands. Read-only / non-interpreter only.
    /// General-purpose interpreters (node, dotnet, npm, find) are intentionally
    /// excluded because they allow arbitrary code execution on the host even
    /// without any forbidden shell operator (e.g.
    /// <c>node -e "require('child_process').execSync('id')"</c>). They can be
    /// re-enabled explicitly via <see cref="s_interpreterCommands"/> opt-in
    /// (<c>allowInterpreters: true</c>), which logs a security warning.
    /// </summary>
    private static readonly string[] s_defaultAllowedCommands =
    [
        // Read-only / non-interpreter POSIX commands
        "ls", "cat", "pwd", "which", "grep", "wc", "echo",
        // git restricted to read-only subcommands (see ValidateTypedRequest)
        "git",
        // Windows equivalents
        "dir", "type", "where"
    ];

    /// <summary>
    /// Interpreter / build commands that grant host code execution. They are
    /// NOT in the default allowlist and are only added when the caller opts in
    /// explicitly (<c>allowInterpreters: true</c>). In that mode the tool is
    /// RCE-equivalent: there is no OS confinement.
    /// </summary>
    private static readonly string[] s_interpreterCommands =
    [
        "dotnet", "npm", "node", "find"
    ];

    /// <summary>
    /// Read-only <c>git</c> subcommands permitted by default. Other subcommands
    /// (e.g. those that can spawn processes via <c>-c core.sshCommand=…</c>) are
    /// rejected unless interpreters are explicitly opted in.
    /// </summary>
    private static readonly HashSet<string> s_allowedGitSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "log", "diff", "show"
    };

    /// <summary>Default set of blocked command patterns.</summary>
    private static readonly string[] s_defaultBlockedPatterns =
    [
        "rm -rf /", "sudo", "mkfs", "dd if=", "shutdown",
        "reboot", "format"
    ];

    /// <summary>
    /// Initializes a new instance of <see cref="ShellCommandTool"/> with VFS working-directory validation.
    /// </summary>
    /// <param name="fileSystem">Virtual file system service used to resolve and validate the working directory.</param>
    /// <param name="allowedCommands">Custom set of allowed executables. Uses the read-only default if null.</param>
    /// <param name="blockedPatterns">Custom set of blocked patterns. Uses default if null.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="allowInterpreters">
    /// When <c>true</c>, re-enables general-purpose interpreters
    /// (<c>node</c>/<c>dotnet</c>/<c>npm</c>/<c>find</c>) on top of the default
    /// allowlist. This is RCE-equivalent (no OS confinement) and emits a security
    /// warning. Ignored when a custom <paramref name="allowedCommands"/> is supplied.
    /// </param>
    public ShellCommandTool(
        IFileSystemService fileSystem,
        IEnumerable<string>? allowedCommands = null,
        IEnumerable<string>? blockedPatterns = null,
        ILogger<ShellCommandTool>? logger = null,
        bool allowInterpreters = false)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _interpretersAllowed = allowInterpreters && allowedCommands is null;
        _allowedCommands = BuildAllowedCommands(allowedCommands, allowInterpreters);
        _blockedPatterns = [.. blockedPatterns ?? s_defaultBlockedPatterns];

        if (_interpretersAllowed)
            LogInterpretersEnabled();
    }

    /// <summary>
    /// Builds the effective allowlist. A custom <paramref name="allowedCommands"/>
    /// is used verbatim (the caller takes full responsibility). Otherwise the
    /// read-only default is used, with interpreters added only on explicit opt-in.
    /// </summary>
    private static HashSet<string> BuildAllowedCommands(
        IEnumerable<string>? allowedCommands, bool allowInterpreters)
    {
        if (allowedCommands is not null)
        {
            return new HashSet<string>(allowedCommands, StringComparer.OrdinalIgnoreCase);
        }

        var set = new HashSet<string>(s_defaultAllowedCommands, StringComparer.OrdinalIgnoreCase);
        if (allowInterpreters)
        {
            foreach (var cmd in s_interpreterCommands)
                set.Add(cmd);
        }

        return set;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ShellCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Command))
            return "Command cannot be empty";

        // Check for forbidden shell operators before any parsing
        var forbiddenOp = FindForbiddenOperator(request.Command);
        if (forbiddenOp is not null)
            return $"Command contains forbidden shell operator: '{forbiddenOp}'";

        var executable = ExtractExecutable(request.Command);
        if (string.IsNullOrWhiteSpace(executable))
            return "Command cannot be empty";

        // Reject executables that contain path separators after normalization
        // (ExtractExecutable already strips paths, so if we still see separators
        //  something is wrong)
        if (executable.Contains('/', StringComparison.Ordinal) || executable.Contains('\\', StringComparison.Ordinal))
            return $"Invalid executable name: '{executable}'";

        if (!_allowedCommands.Contains(executable))
            return $"Command '{executable}' is not in the allowlist. Allowed: {string.Join(", ", _allowedCommands)}";

        // git is allowed by default only for read-only subcommands. Mutating or
        // process-spawning forms (e.g. `git -c core.sshCommand=…`) are rejected
        // unless interpreters were explicitly opted in.
        if (!_interpretersAllowed
            && executable.Equals("git", StringComparison.OrdinalIgnoreCase))
        {
            var subcommandError = ValidateGitSubcommand(request.Command);
            if (subcommandError is not null)
                return subcommandError;
        }

        var blockedPattern = _blockedPatterns
            .FirstOrDefault(pattern => request.Command.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        if (blockedPattern is not null)
            return $"Command contains blocked pattern: '{blockedPattern}'";

        if (request.TimeoutSeconds <= 0)
            return "Timeout must be greater than 0";

        return null;
    }

    /// <inheritdoc />
    protected override Task<ShellCommandResponse> ExecuteTypedAsync(
        ShellCommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync(request, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup after a timeout kill: failures while draining the killed process's stdout/stderr must not mask the timeout result returned to the caller; external cancellation is rethrown by the OperationCanceledException filter.")]
    private async Task<ShellCommandResponse> ExecuteCoreAsync(
        ShellCommandRequest request, CancellationToken cancellationToken)
    {
        var (executable, arguments) = ParseCommand(request.Command);

        var wdValidation = _fileSystem.ResolveAndValidate(request.WorkingDirectory, FileAccessRights.Read);
        if (!wdValidation.IsAllowed)
            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = $"WorkingDirectory refusé: {wdValidation.DenialReason}",
                Completed = false
            };
        var workingDirectory = wdValidation.ResolvedPath!;

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // On Windows, shell built-in commands (echo, dir, etc.) are not standalone
        // executables. Route them through cmd.exe /c to make them functional.
        // Security is preserved: forbidden operators were already rejected in
        // ValidateTypedRequest, and arguments are passed individually through
        // ArgumentList (no shell interpretation of special characters).
        if (OperatingSystem.IsWindows() && s_windowsBuiltins.Contains(executable))
        {
            startInfo.FileName = ShellDefaults.WindowsShell; // NOSONAR — cmd.exe is a Windows system binary resolved via SystemRoot
            startInfo.ArgumentList.Add(ShellDefaults.WindowsCommandSwitch);
            startInfo.ArgumentList.Add(executable);
        }
        else
        {
            // NOSONAR — FileName is intentionally set to a user-supplied executable name
            // (validated against the allowlist in ValidateTypedRequest). Using a bare name
            // rather than an absolute path is by design: the allowed commands (git, dotnet,
            // npm, etc.) may be installed in different locations across environments.
            startInfo.FileName = executable;
        }

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            LogCommandCompleted(request.Command, process.ExitCode);

            return new ShellCommandResponse
            {
                ExitCode = process.ExitCode,
                Stdout = Truncate(stdout),
                Stderr = Truncate(stderr),
                Completed = true
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred (not external cancellation)
            KillProcess(process);

            var stdout = "";
            var stderr = "";

            try
            {
                // Try to read what we can from the streams
                stdout = await ReadWithFallbackAsync(process.StandardOutput).ConfigureAwait(false);
                stderr = await ReadWithFallbackAsync(process.StandardError).ConfigureAwait(false);
            }
            catch
            {
                // Ignore read errors after kill
            }

            LogCommandTimedOut(request.Command, request.TimeoutSeconds);

            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = Truncate(stdout),
                Stderr = Truncate(stderr + "\n[Command timed out]"),
                Completed = false
            };
        }
    }

    /// <summary>
    /// Extracts the executable name from a command string.
    /// Handles leading whitespace and returns the first token.
    /// Path-qualified executables are reduced to the filename only.
    /// </summary>
    public static string ExtractExecutable(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var trimmed = command.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        var executable = spaceIndex >= 0 ? trimmed[..spaceIndex] : trimmed;

        // Handle path-qualified executables (e.g., /usr/bin/git -> git)
        var lastSlash = executable.LastIndexOfAny(['/', '\\']);
        if (lastSlash >= 0 && lastSlash < executable.Length - 1)
            executable = executable[(lastSlash + 1)..];

        return executable;
    }

    /// <summary>
    /// Parses a command string into an executable and its arguments.
    /// The executable is resolved to a bare name (no path).
    /// Arguments are split by whitespace, respecting simple quoting.
    /// </summary>
    public static (string Executable, string[] Arguments) ParseCommand(string command)
    {
        var tokens = TokenizeCommand(command);
        if (tokens.Count == 0)
            return ("", []);

        var executable = tokens[0];

        // Normalize path-qualified executable to bare name
        var lastSlash = executable.LastIndexOfAny(['/', '\\']);
        if (lastSlash >= 0 && lastSlash < executable.Length - 1)
            executable = executable[(lastSlash + 1)..];

        var arguments = tokens.Count > 1 ? tokens.Skip(1).ToArray() : [];
        return (executable, arguments);
    }

    /// <summary>
    /// Tokenizes a command string into individual arguments, respecting
    /// single and double quotes. Does not perform shell expansion.
    /// </summary>
    public static IReadOnlyList<string> TokenizeCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    /// <summary>
    /// Checks whether the command contains any forbidden shell operator.
    /// Returns the first forbidden operator found, or null if the command is clean.
    /// </summary>
    public static string? FindForbiddenOperator(string command)
    {
        return s_forbiddenOperators.FirstOrDefault(op => command.Contains(op, StringComparison.Ordinal));
    }

    /// <summary>
    /// Validates that a <c>git</c> command uses a read-only subcommand
    /// (<c>status</c>/<c>log</c>/<c>diff</c>/<c>show</c>). The first non-option
    /// token after <c>git</c> is treated as the subcommand; any leading option
    /// (e.g. <c>-c …</c>) is rejected because it can alter execution behaviour.
    /// </summary>
    private static string? ValidateGitSubcommand(string command)
    {
        var tokens = TokenizeCommand(command);
        if (tokens.Count < 2)
            return "git requires a read-only subcommand (status, log, diff, show)";

        var subcommand = tokens[1];

        // Reject leading options/flags such as `-c core.sshCommand=…` which can
        // make git spawn arbitrary processes.
        if (subcommand.StartsWith('-'))
            return $"git option '{subcommand}' is not allowed; only read-only subcommands (status, log, diff, show) are permitted";

        if (!s_allowedGitSubcommands.Contains(subcommand))
            return $"git subcommand '{subcommand}' is not allowed; only read-only subcommands (status, log, diff, show) are permitted";

        return null;
    }

    /// <summary>Truncates a string to <see cref="MaxOutputLength"/> characters.</summary>
    private static string Truncate(string value)
    {
        if (value.Length <= MaxOutputLength)
            return value;

        return value[..MaxOutputLength] + "\n[Output truncated]";
    }

    /// <summary>Attempts to kill a process and its children.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: Process.Kill can throw if the process has already exited or is inaccessible; a failed kill must not propagate over the primary result.")]
    private static void KillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have already exited
        }
    }

    /// <summary>Reads remaining stream content with a short timeout fallback.</summary>
    private static async Task<string> ReadWithFallbackAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        return await reader.ReadToEndAsync(cts.Token).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shell command completed: {Command} (exit code: {ExitCode})")]
    private partial void LogCommandCompleted(string command, int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shell command timed out: {Command} (timeout: {TimeoutSeconds}s)")]
    private partial void LogCommandTimedOut(string command, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SECURITY: ShellCommandTool interpreter opt-in enabled (node/dotnet/npm/find). This is RCE-equivalent: commands run on the host with NO OS confinement. Do not expose this tool to untrusted (LLM-produced) input in this mode.")]
    private partial void LogInterpretersEnabled();
}
