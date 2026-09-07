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

    /// <summary>
    /// Gets the virtual working directory for command execution. Left empty, the command runs
    /// in the first mounted directory the agent can read.
    /// <para>
    /// This used to default to <c>"."</c>, which no VFS registry can resolve: since ADR-008 a
    /// virtual path starts with '/', so the lookup found no mount and every call that omitted
    /// the field — the shape an LLM writes when the schema says the field is optional — was
    /// refused with "No mount found for virtual path '.'".
    /// </para>
    /// </summary>
    [FieldSchema(
        Description = "Virtual working directory for command execution, e.g. /workspace. Defaults to the first mounted directory.",
        IsRequired = false,
        Example = "/workspace")]
    public string WorkingDirectory { get; init; } = "";

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
/// <b>VFS path rewriting.</b> Arguments that name a mount-prefixed virtual path
/// (e.g. <c>/workspace/App.sln</c>, including the embedded <c>--out=/workspace/dist</c>
/// form) are resolved to their physical location before the process starts; a path the
/// file system refuses fails the call with the redacted denial reason. Conversely,
/// stdout/stderr are rewritten physical→virtual so the model only ever sees virtual
/// paths. Both directions cover <see cref="Orkeon.Domain.FileSystem.MountVisibility.AgentFacing"/>
/// mounts only (the contract of <c>IFileSystemService.GetAvailableMounts</c>) and match
/// ordinally — a Windows tool that re-cases paths defeats the outbound rewrite.
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
    /// The only environment variables handed to the child process. Everything else the
    /// runner holds is dropped, because that environment carries the crew's LLM API keys
    /// (<c>ORKEON_Llm__ApiKey</c> and friends) and an allowed read-only command
    /// (<c>cat /proc/self/environ</c>, <c>set</c>) would otherwise read them straight
    /// back into the model's context.
    /// <para>
    /// Twin of <c>ProcessIsolationSandbox.ConfigureRestrictedEnvironment</c>
    /// (<c>src/core/Orkeon.Infrastructure/Sandbox</c>), which does the same for the code
    /// sandbox. The list is duplicated rather than shared: a tool project cannot reference
    /// Infrastructure, and the shared kernel it could go through
    /// (<c>Orkeon.Tools.Abstractions</c>) has no process-execution surface to hang it on.
    /// </para>
    /// </summary>
    private static readonly string[] s_propagatedEnvironmentVariables =
    [
        // PATH is propagated, not rebuilt: the tool starts its child from a BARE executable
        // name by design (see ExecuteCoreAsync), so the host lookup path is what makes git
        // and the opt-in interpreters resolvable wherever an operator installed them.
        "PATH",
        // POSIX: git needs a HOME to find its own configuration, the locale decides how the
        // child encodes what it prints, and TMPDIR gives it a writable scratch directory.
        "HOME", "LANG", "LC_ALL", "TMPDIR",
        // Windows: cmd.exe and the CLR resolve an executable through these.
        "USERPROFILE", "SystemRoot", "SystemDrive", "PATHEXT", "TEMP", "TMP",
        "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)",
        // The .NET and NuGet locations, load-bearing once interpreters are opted in.
        "DOTNET_ROOT", "NUGET_PACKAGES"
    ];

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
    /// <param name="extraAllowedCommands">
    /// Extra executables unioned into the effective allowlist — additive on top of the
    /// default (or of a custom <paramref name="allowedCommands"/>), unlike the
    /// replacement semantics of <paramref name="allowedCommands"/>. Does NOT affect
    /// <paramref name="allowInterpreters"/> nor the git read-only restriction. An empty
    /// enumerable is harmless (union of nothing).
    /// </param>
    public ShellCommandTool(
        IFileSystemService fileSystem,
        IEnumerable<string>? allowedCommands = null,
        IEnumerable<string>? blockedPatterns = null,
        ILogger<ShellCommandTool>? logger = null,
        bool allowInterpreters = false,
        IEnumerable<string>? extraAllowedCommands = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _interpretersAllowed = allowInterpreters && allowedCommands is null;
        _allowedCommands = BuildAllowedCommands(allowedCommands, allowInterpreters);
        if (extraAllowedCommands is not null)
            _allowedCommands.UnionWith(extraAllowedCommands);
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

        // A cmd.exe builtin is executed through `cmd.exe /c` (see ExecuteCoreAsync), and cmd
        // expands `%NAME%` against the environment before the builtin ever sees the token.
        // The refusal is unconditional rather than Windows-only: what a crew may ask of this
        // tool must not depend on which host happens to run it, and the model that writes the
        // command never knows which one that is.
        if (s_windowsBuiltins.Contains(executable)
            && request.Command.Contains('%', StringComparison.Ordinal))
        {
            return $"Command contains '%': '{executable}' runs through the Windows shell, "
                + "which would expand it into the host environment";
        }

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

        // VFS inbound check: an argument naming a mount-prefixed virtual path the file
        // system refuses must fail the call (redacted reason) — never reach the process.
        var (_, argumentDenial) = RewriteInboundArguments(ParseCommand(request.Command).Arguments);
        if (argumentDenial is not null)
            return $"Command argument denied by file system: {argumentDenial}";

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

        // Virtual→physical: the child process only understands physical paths. Validation
        // already vetoed denials; this re-run is belt-and-braces against a scope swap.
        var (rewrittenArguments, argumentDenial) = RewriteInboundArguments(arguments);
        if (argumentDenial is not null)
            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = $"Argument denied: {argumentDenial}",
                Completed = false
            };
        arguments = rewrittenArguments;

        var (workingDirectory, workingDirectoryDenial) = ResolveWorkingDirectory(request.WorkingDirectory);
        if (workingDirectoryDenial is not null)
            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = $"WorkingDirectory denied: {workingDirectoryDenial}",
                Completed = false
            };

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ConfigureRestrictedEnvironment(startInfo);

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

        // Physical→virtual table for the output, built per call: mounts can be swapped
        // per async flow (IFileSystemScope), so nothing here may be cached in the ctor.
        var outboundReplacements = BuildOutboundReplacements();

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // The message names the working directory, and the working directory is now a
            // RESOLVED PHYSICAL path — so a command that is simply not installed on the host
            // (npm, dotnet, git) put a disk path in front of the model, through the one field
            // the outbound rewrite below never touched. Ordinary trigger, ADR-008 violation.
            throw new InvalidOperationException(
                RewriteOutboundPaths(ex.Message, outboundReplacements), ex);
        }

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
                Stdout = Truncate(RewriteOutboundPaths(stdout, outboundReplacements)),
                Stderr = Truncate(RewriteOutboundPaths(stderr, outboundReplacements)),
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
                Stdout = Truncate(RewriteOutboundPaths(stdout, outboundReplacements)),
                Stderr = Truncate(RewriteOutboundPaths(stderr, outboundReplacements) + "\n[Command timed out]"),
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

    // ── VFS path rewriting ─────────────────────────────────────────────────

    /// <summary>
    /// Rewrites mount-prefixed virtual arguments to their physical paths. Returns the
    /// original array untouched together with the (already redacted) denial reason when
    /// the file system refuses one of them. No mounts ⇒ exact no-op.
    /// </summary>
    /// <summary>
    /// The physical directory the child process starts in, or the denial to report.
    /// <para>
    /// A named directory is resolved and rights-checked like any other path. Named nothing,
    /// the command runs in the first agent-facing mount that grants Read — deterministic
    /// (the registry orders its mounts) and a place the caller was already told about, since
    /// it comes from the list the tool schema and every denial message are built from. With
    /// no mounts at all the process inherits its parent's directory, the same "no mounts ⇒
    /// exact no-op" stance the argument rewriting takes.
    /// </para>
    /// <para>
    /// The default used to be the literal <c>"."</c>, which no registry can resolve: a mount's
    /// virtual path starts with '/' (ADR-008), so the lookup found nothing and every call that
    /// omitted the field — the shape a model writes for an optional field — was refused.
    /// </para>
    /// </summary>
    private (string WorkingDirectory, string? DenialReason) ResolveWorkingDirectory(string requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var named = _fileSystem.ResolveAndValidate(requested, FileAccessRights.Read);
            return named.IsAllowed ? (named.ResolvedPath!, null) : ("", named.DenialReason);
        }

        var mounts = _fileSystem.GetAvailableMounts();
        if (mounts.Count == 0)
            return ("", null);

        var readable = mounts.FirstOrDefault(m => m.DefaultRights.HasFlag(FileAccessRights.Read));
        if (readable is null)
            return ("", "no readable mount is available to serve as the working directory.");

        var resolved = _fileSystem.ResolveAndValidate(readable.VirtualPath, FileAccessRights.Read);
        return resolved.IsAllowed ? (resolved.ResolvedPath!, null) : ("", resolved.DenialReason);
    }

    private (string[] Arguments, string? DenialReason) RewriteInboundArguments(string[] arguments)
    {
        var mounts = _fileSystem.GetAvailableMounts();
        if (mounts.Count == 0 || arguments.Length == 0)
            return (arguments, null);

        var rewritten = new string[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var (token, denial) = TryRewriteToken(arguments[i], mounts);
            if (denial is not null)
                return (arguments, denial);
            rewritten[i] = token;
        }

        return (rewritten, null);
    }

    /// <summary>
    /// Rewrites one token: a whole-token mount match wins; otherwise the embedded
    /// <c>key=/mount/…</c> form splits at the FIRST <c>=</c> and resolves the entire
    /// right side (even if it contains further <c>=</c>). Anything else stays verbatim.
    /// </summary>
    private (string Token, string? DenialReason) TryRewriteToken(string token, IReadOnlyList<MountInfo> mounts)
    {
        if (mounts.Any(m => MatchesMountPrefix(token, m.VirtualPath)))
        {
            var result = _fileSystem.ResolveAndValidate(token, FileAccessRights.Read);
            return result.IsAllowed
                ? (result.ResolvedPath!, null)
                : (token, result.DenialReason ?? "access denied");
        }

        var eq = token.IndexOf('=', StringComparison.Ordinal);
        if (eq > 0 && eq < token.Length - 1)
        {
            var right = token[(eq + 1)..];
            if (mounts.Any(m => MatchesMountPrefix(right, m.VirtualPath)))
            {
                var result = _fileSystem.ResolveAndValidate(right, FileAccessRights.Read);
                return result.IsAllowed
                    ? (string.Concat(token.AsSpan(0, eq + 1), result.ResolvedPath), null)
                    : (token, result.DenialReason ?? "access denied");
            }
        }

        return (token, null);
    }

    /// <summary>
    /// Path-boundary prefix check: <c>/workspace</c> matches <c>/workspace</c> and
    /// <c>/workspace/x</c> but never <c>/workspaces</c>. Ordinal, like the registry.
    /// </summary>
    public static bool MatchesMountPrefix(string path, string mountVirtualPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mountVirtualPath);
        var prefix = mountVirtualPath.TrimEnd('/');
        if (prefix.Length == 0)
            return false;

        return string.Equals(path, prefix, StringComparison.Ordinal)
            || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the physical→virtual replacement table by resolving each mount ROOT
    /// (a pure mapping — no existence requirement). Mounts whose root resolution fails
    /// and identity mounts (physical == virtual, e.g. in-memory fakes) are skipped.
    /// Longest physical base first, so nested bases resolve to the deepest mount.
    /// </summary>
    private List<(string PhysicalBase, string VirtualPrefix)> BuildOutboundReplacements()
    {
        var mounts = _fileSystem.GetAvailableMounts();
        if (mounts.Count == 0)
            return [];

        var replacements = new List<(string PhysicalBase, string VirtualPrefix)>(mounts.Count);
        foreach (var virtualPath in mounts.Select(mount => mount.VirtualPath))
        {
            var root = _fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
            if (!root.IsAllowed || string.IsNullOrEmpty(root.ResolvedPath))
                continue;

            var virtualPrefix = virtualPath.TrimEnd('/');
            if (virtualPrefix.Length == 0)
                continue;

            if (string.Equals(root.ResolvedPath, virtualPrefix, StringComparison.Ordinal)
                || string.Equals(root.ResolvedPath, virtualPath, StringComparison.Ordinal))
                continue;

            replacements.Add((root.ResolvedPath, virtualPrefix));
        }

        replacements.Sort((a, b) => b.PhysicalBase.Length.CompareTo(a.PhysicalBase.Length));
        return replacements;
    }

    /// <summary>
    /// Rewrites physical mount bases back to their virtual prefixes in process output.
    /// A base only matches on a path boundary (next char is a separator, a delimiter,
    /// or end of text — <c>/data</c> never rewrites <c>/data2/x</c>); after a
    /// replacement, backslashes in the remainder of the path token are normalized to
    /// <c>/</c> up to the next delimiter, so <c>C:\ws\Foo.cs(12,3): error</c> becomes
    /// <c>/workspace/Foo.cs(12,3): error</c>. Pure string scan — no <c>Path</c> APIs.
    /// </summary>
    public static string RewriteOutboundPaths(
        string output,
        IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> replacements)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(replacements);
        if (output.Length == 0 || replacements.Count == 0)
            return output;

        var sb = new System.Text.StringBuilder(output.Length);
        var i = 0;
        while (i < output.Length)
        {
            if (TryRewriteTokenAt(sb, output, replacements, ref i))
                continue;

            sb.Append(output[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Rewrites the path token starting at <paramref name="index"/> when one of the
    /// physical bases matches there on a path boundary, appending the virtual prefix and
    /// the separator-normalized remainder of the token, and advancing
    /// <paramref name="index"/> past what it consumed. Returns false without touching the
    /// builder when no base matches, leaving the caller to copy the character as it is.
    /// </summary>
    private static bool TryRewriteTokenAt(
        System.Text.StringBuilder sb,
        string output,
        IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> replacements,
        ref int index)
    {
        foreach (var (physicalBase, virtualPrefix) in replacements)
        {
            if (!MatchesAt(output, index, physicalBase))
                continue;

            // A base only matches when what follows closes it, so /data leaves /data2/x alone.
            var end = index + physicalBase.Length;
            if (end < output.Length && !ClosesPathBase(output[end]))
                continue;

            sb.Append(virtualPrefix);
            index = end;
            while (index < output.Length && !IsPathDelimiter(output[index]))
            {
                sb.Append(output[index] == '\\' ? '/' : output[index]);
                index++;
            }

            return true;
        }

        return false;
    }

    /// <summary>True when the character ends a matched physical base: a separator or a token delimiter.</summary>
    private static bool ClosesPathBase(char c) => c is '/' or '\\' || IsPathDelimiter(c);

    private static bool MatchesAt(string text, int index, string value)
        => index + value.Length <= text.Length
           && string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    /// <summary>
    /// Characters that end a path token in process output. Includes the list
    /// separators <c>:</c> <c>;</c> <c>,</c> so every entry of a PATH-style dump
    /// (<c>/a:/b</c>, <c>C:\a;C:\b</c>) is matched — without them the scan would
    /// swallow the second base and leak it unrewritten. A drive-letter colon is
    /// unaffected: it sits INSIDE the matched physical base, never after it.
    /// </summary>
    private static bool IsPathDelimiter(char c)
        => char.IsWhiteSpace(c) || c is '"' or '\'' or '(' or ')' or ':' or ';' or ',';

    /// <summary>
    /// Replaces the inherited environment of the child process with
    /// <see cref="s_propagatedEnvironmentVariables"/>, so a command the allowlist permits
    /// can read its own environment without reading the runner's secrets.
    /// </summary>
    private static void ConfigureRestrictedEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment.Clear();

        foreach (var name in s_propagatedEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                startInfo.Environment[name] = value;
        }

        // Set, not propagated: the child must stay quiet and must not start a telemetry
        // upload of its own on the first `dotnet` invocation of a crew run.
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
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
