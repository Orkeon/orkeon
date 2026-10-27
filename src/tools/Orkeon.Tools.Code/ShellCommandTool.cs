using Orkeon.Domain.Attributes;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Abstractions.Security;
using Orkeon.Tools.Code.Confinement;
using Orkeon.Tools.Code.Constants.Shell;
using Orkeon.Tools.Code.Sessions;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Code;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Strongly-typed request for <see cref="ShellCommandTool"/>.</summary>
public sealed record ShellCommandRequest
{
    /// <summary>Gets the shell command to execute.</summary>
    [FieldSchema(Description = "Shell command to execute; required by the run and start_job actions", IsRequired = true, Example = "echo hello")]
    public string Command { get; init; } = "";

    /// <summary>
    /// Gets the virtual working directory for command execution. Left empty, the command runs in
    /// the working directory every file tool resolves a relative path against: the agent's root
    /// under an agent run, else the file system's working directory (<c>/workspace</c> when it is
    /// mounted, or the one the host designated). A relative value is resolved against that same
    /// directory.
    /// <para>
    /// This used to default to <c>"."</c>, which no VFS registry can resolve: since ADR-008 a
    /// virtual path starts with '/', so the lookup found no mount and every call that omitted
    /// the field — the shape an LLM writes when the schema says the field is optional — was
    /// refused with "No mount found for virtual path '.'".
    /// </para>
    /// </summary>
    [FieldSchema(
        Description = "Virtual working directory for command execution, e.g. /workspace. Defaults to the working directory relative file paths resolve against (the agent's root, else /workspace); outside an agent root, a writing command that directory does not allow runs in the write directory (/output).",
        IsRequired = false,
        Example = "/workspace")]
    public string WorkingDirectory { get; init; } = "";

    /// <summary>Gets the timeout in seconds.</summary>
    [FieldSchema(Description = "Timeout in seconds", IsRequired = false, Default = "30")]
    public int TimeoutSeconds { get; init; } = ShellDefaults.DefaultTimeoutSeconds;

    /// <summary>
    /// Gets the action: <c>run</c> (default) runs the command and waits for it; <c>start_job</c> starts it
    /// detached with its output in a log file; <c>job_status</c> and <c>job_tail</c> read a job started
    /// earlier in the same run.
    /// </summary>
    [FieldSchema(
        Description = "run (default): run the command and wait; start_job: start it detached, output to a log file; job_status: running or exit code of a job; job_tail: last lines of a job's log",
        IsRequired = false,
        Default = "run",
        Example = "start_job")]
    public string Action { get; init; } = ShellCommandTool.RunAction;

    /// <summary>Gets the id of the job to inspect, for <c>job_status</c> and <c>job_tail</c>.</summary>
    [FieldSchema(Description = "Id returned by start_job, for job_status and job_tail", IsRequired = false, Example = "3f9a1c0b27d4")]
    public string JobId { get; init; } = "";

    /// <summary>Gets how many lines <c>job_tail</c> returns.</summary>
    [FieldSchema(Description = "Lines job_tail returns from the end of the log", IsRequired = false, Default = "50")]
    public int TailLines { get; init; } = ShellCommandTool.DefaultTailLines;
}

/// <summary>Strongly-typed response for <see cref="ShellCommandTool"/>.</summary>
public sealed record ShellCommandResponse : Orkeon.Domain.Tools.IBoundedToolResponse
{
    /// <summary>Gets whether the shell cut its own output.</summary>
    public bool AlreadyBounded { get; init; }

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

    /// <summary>Gets the id of the job, for the job actions.</summary>
    [ReturnSchema(Description = "Job id, for the job actions", Example = "3f9a1c0b27d4")]
    public string JobId { get; init; } = "";

    /// <summary>Gets the virtual path of the job's log.</summary>
    [ReturnSchema(Description = "Virtual path of the job's log file", Example = "/output/jobs/3f9a1c0b27d4.log")]
    public string OutputPath { get; init; } = "";

    /// <summary>Gets the job's status: <c>running</c> or <c>exited</c>.</summary>
    [ReturnSchema(Description = "Job status: running or exited", Example = "running")]
    public string Status { get; init; } = "";

    /// <summary>Gets the last lines of the job's log, for <c>job_tail</c>.</summary>
    [ReturnSchema(Description = "Last lines of the job's log", Example = "done")]
    public string Tail { get; init; } = "";
}

// ── Tool implementation ────────────────────────────────────────────────

/// <summary>
/// Tool for executing shell commands with allowlist security,
/// configurable timeout, and output capture.
/// Each agent run keeps one shell session (<see cref="Sessions.ShellSessionManager"/>, held by the
/// run's <see cref="Orkeon.Domain.Execution.AgentRunScope"/>): on Linux and macOS a <c>sh -s</c> the
/// tool feeds one fragment per command, every word of it POSIX-quoted so the shell expands nothing
/// (<see cref="Sessions.ShellScript"/>); on Windows each command is its own process inside the
/// session's Job Object, its arguments quoted one by one, shell built-ins (echo, dir, etc.) routed
/// through <c>cmd.exe /c</c> with every argument escaped for cmd. A timeout
/// or the caller's cancellation kills the session and the next command opens a new one; nothing the
/// session started outlives the run.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope of the no-shell-injection guarantee.</b> The forbidden-operator
/// check (<c>;</c>, <c>|</c>, <c>$(</c>, backticks, <c>\n</c>, …) and the quoting of every
/// argument as one word prevent <i>shell metacharacter
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
/// <b>Rights a path needs.</b> A command of the read-only default allowlist needs Read on the
/// paths it names and on its working directory. Any other command (an interpreter, an extra or
/// custom command, <c>git</c> once interpreters lift its restriction) needs every right
/// (<see cref="Orkeon.Domain.FileSystem.FileAccessRights.ReadWrite"/>): the process may write,
/// create or delete there, and the tool cannot tell which. The check covers the paths the command
/// line names; what the process reaches by itself (a path in a script, a relative path climbing
/// out of the working directory) is outside the VFS.
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
/// <para>
/// <b>Confinement (on by default).</b> <see cref="Confinement"/> defaults to
/// <see cref="ShellConfinementMode.Auto"/>, the backend of the platform, and to no confinement inside a
/// container; <see cref="ShellConfinementMode.None"/> turns it off. Under
/// <see cref="ShellConfinementMode.Bubblewrap"/> (Linux), the process runs under <c>bwrap</c> in a view built from
/// the VFS (the session is <c>bwrap … -- /bin/sh -s</c>, reopened when the mounts change): each agent-facing mount bound at its virtual path (read-only when it lacks Write, a sub path
/// without rights covered), the system directories read-only, nothing else of the host, the network off
/// unless allowed. The process then works on virtual paths, so the arguments are checked but not
/// rewritten, and the kernel enforces a read-only mount on whatever the process reaches by itself. When
/// the backend is unavailable the call fails with the reason and how to turn confinement off; it never
/// runs unconfined.
/// </para>
/// <para>
/// Under <see cref="ShellConfinementMode.Seatbelt"/> (macOS), the session is <c>sandbox-exec -p &lt;profile&gt; /bin/sh -s</c>:
/// the profile denies everything but the system directories read-only, each mount read-only or
/// read-write after its rights (a sub path without rights denied), a scratch directory of the session
/// and the network when allowed. Seatbelt filters without moving anything, so the process works on
/// physical paths and the arguments are rewritten as when unconfined.
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

    /// <summary>
    /// How the process is confined: <see cref="ShellConfinementMode.Auto"/> (default) picks the backend
    /// of the platform, none inside a container; <see cref="ShellConfinementMode.None"/> runs it on the
    /// host; <see cref="ShellConfinementMode.Bubblewrap"/> runs it in a view built from the VFS.
    /// </summary>
    public ShellConfinementOptions Confinement { get; init; } = new();

    /// <summary>
    /// The variable names the operator added to the session's environment
    /// (<c>Orkeon:Tools:Shell:Environment:Propagate</c>), already filtered. Set by the dependency
    /// injection registration and the tests.
    /// </summary>
    internal IReadOnlyList<string> PropagatedVariables { get; init; } = [];

    /// <summary>
    /// Whether a confined session receives the operator's session bus
    /// (<c>Orkeon:Tools:Shell:Environment:SessionBus</c>). Set by the dependency injection
    /// registration and the tests.
    /// </summary>
    internal bool SessionBus { get; init; }

    /// <summary>Whether bubblewrap works here; probed once per process. Replaced by the tests.</summary>
    internal Func<BubblewrapAvailability> Bubblewrap { get; init; } = BubblewrapProbe.Current;

    /// <summary>Whether Seatbelt works here; probed once per process. Replaced by the tests.</summary>
    internal Func<ShellSandboxAvailability> Seatbelt { get; init; } = SeatbeltProbe.Current;

    /// <summary>Keeps the shell session of each agent run. Replaced by the tests.</summary>
    internal ShellSessionManager Sessions { get; init; } = ShellSessionManager.Shared;

    /// <summary>Whether the host runs in a container, read by <see cref="ShellConfinementMode.Auto"/>. Replaced by the tests.</summary>
    internal IContainerDetector ContainerDetector { get; init; } = Orkeon.Tools.Code.Confinement.ContainerDetector.Host;

    /// <summary>The platform the backend is chosen for. Replaced by the tests.</summary>
    internal ShellPlatform Platform { get; init; } = ShellConfinementResolver.CurrentPlatform;

    /// <summary>
    /// How the session runs; null picks it from <see cref="Confinement"/> through
    /// <see cref="ShellSandboxSelector"/> (bubblewrap over <see cref="Bubblewrap"/>, Seatbelt over <see cref="Seatbelt"/>, none, or a backend
    /// that reports why it cannot run). Set by the dependency injection registration and the tests.
    /// </summary>
    internal IShellSandbox? Sandbox { get; init; }

    private IShellSandbox? _defaultSandbox;

    private IShellSandbox ActiveSandbox => Sandbox ?? DefaultSandbox();

    /// <summary>The sandbox chosen from <see cref="Confinement"/>, built at the first call.</summary>
    private IShellSandbox DefaultSandbox()
    {
        _defaultSandbox ??= new ShellSandboxSelector(UnconfinedShellSandbox.Shared, new BubblewrapShellSandbox(Bubblewrap), new SeatbeltShellSandbox(Seatbelt), ContainerDetector, Platform, _logger)
            .Select(Confinement.Mode);
        return _defaultSandbox;
    }

    /// <summary>Maximum number of characters captured from stdout/stderr.</summary>
    public const int MaxOutputLength = 10_000;

    /// <summary>The action that runs a command and waits for it (the default).</summary>
    public const string RunAction = "run";

    /// <summary>The lines <c>job_tail</c> returns when the request names none.</summary>
    public const int DefaultTailLines = 50;

    private const string StartJobAction = "start_job";
    private const string JobStatusAction = "job_status";
    private const string JobTailAction = "job_tail";
    private const int MaxTailLines = 500;
    private const int MaxTailBytes = 64 * 1024;

    private readonly HashSet<string> _allowedCommands;
    private readonly List<string> _blockedPatterns;
    private readonly IFileSystemService _fileSystem;
    private readonly int _outputLimit;

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
    /// The commands of <see cref="s_defaultAllowedCommands"/>, which only read the paths they
    /// are given (see <see cref="RequiredPathRights"/>).
    /// </summary>
    private static readonly HashSet<string> s_readOnlyCommands =
        new(s_defaultAllowedCommands, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Read-only <c>git</c> subcommands permitted by default. Other subcommands
    /// (e.g. those that can spawn processes via <c>-c core.sshCommand=…</c>) are
    /// rejected unless interpreters are explicitly opted in.
    /// </summary>
    private static readonly HashSet<string> s_allowedGitSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "log", "diff", "show"
    };

    /// <summary>
    /// Options refused anywhere in a <c>git</c> command line, whichever read-only
    /// subcommand carries them. Checking the subcommand alone was not enough: the four
    /// permitted ones accept diff options that write a file, run a configured program, or
    /// step outside the validated working directory, so "read-only git" was only a claim
    /// about the second token.
    /// <para>
    /// Their global forms (before the subcommand) are already refused by the leading-option
    /// rule below; listing them here keeps a single gate rather than a case analysis, at the
    /// deliberate cost of the diff-side meanings of <c>-c</c> (combined diff) and <c>-C</c>
    /// (copy detection).
    /// </para>
    /// </summary>
    private static readonly string[] s_forbiddenGitOptions =
    [
        // Write the output to an arbitrary host file, or read an order file from one.
        "--output", "-o", "-O",
        // Run the configured external diff driver or textconv filter.
        "--ext-diff", "--textconv",
        // Override the configuration, which is how git is made to spawn a process
        // (core.pager, diff.external, core.sshCommand), or relocate its own binaries.
        "-c", "--config-env", "--exec-path",
        // Repoint the repository or the work tree away from the validated directory.
        "-C", "--git-dir", "--work-tree",
        // Diff two arbitrary host paths, ignoring the repository altogether.
        "--no-index"
    ];

    /// <summary>
    /// Default set of blocked command patterns. Each one names a destructive command, so it
    /// is matched as a whole token (see <see cref="ContainsBlockedPattern"/>) and not as a
    /// bare substring: <c>format</c> means the disk-formatting command, not the
    /// <c>--format=%H</c> of <c>git log</c>.
    /// </summary>
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
    /// <param name="limits">The configured result cuts; the <c>shell_command</c> entry sets the output limit, the default map's (<see cref="MaxOutputLength"/>) otherwise.</param>
    public ShellCommandTool(
        IFileSystemService fileSystem,
        IEnumerable<string>? allowedCommands = null,
        IEnumerable<string>? blockedPatterns = null,
        ILogger<ShellCommandTool>? logger = null,
        bool allowInterpreters = false,
        IEnumerable<string>? extraAllowedCommands = null,
        Orkeon.Domain.Tools.ToolResultLimitsOptions? limits = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _outputLimit = (limits ?? new Orkeon.Domain.Tools.ToolResultLimitsOptions()).ResolveFor("shell_command");
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

    /// <summary>A job action names a job, not a command: the schema's required command is met for it.</summary>
    protected override ValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.TryGetValue("action", out var raw) && ActionName(raw) is JobStatusAction or JobTailAction && !parameters.ContainsKey("command"))
            parameters = new Dictionary<string, object?>(parameters) { ["command"] = "" };
        return base.ValidateParameters(parameters);
    }

    /// <summary>The action as a string, whether it arrives as a CLR string or as a JSON string element.</summary>
    private static string? ActionName(object? value) => value switch
    {
        string text => text,
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
        _ => null,
    };

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ShellCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = string.IsNullOrWhiteSpace(request.Action) ? RunAction : request.Action;
        if (action is JobStatusAction or JobTailAction)
            return ShellJobRegistry.Current()?.Find(request.JobId) is null ? $"Unknown job '{request.JobId}' in this run" : null;
        if (action is not (RunAction or StartJobAction))
            return $"Unknown action '{request.Action}': run, start_job, job_status or job_tail";

        if (string.IsNullOrWhiteSpace(request.Command))
            return "Command cannot be empty";

        // Check for forbidden shell operators before any parsing
        var forbiddenOp = FindForbiddenOperator(request.Command);
        if (forbiddenOp is not null)
            return $"Command contains forbidden shell operator: '{forbiddenOp}'";

        var executable = ExtractExecutable(request.Command);
        if (string.IsNullOrWhiteSpace(executable))
            return "Command cannot be empty";

        var error = ValidateExecutable(request.Command, executable) ?? ValidatePlacement(request, executable);
        if (error is not null)
            return error;

        if (request.TimeoutSeconds <= 0)
            return "Timeout must be greater than 0";

        return action == StartJobAction ? ValidateStartJob() : null;
    }

    /// <summary>
    /// Null when <paramref name="executable"/> may run <paramref name="command"/>: a bare allowlisted
    /// name, no cmd.exe expansion, a read-only git subcommand, no blocked pattern.
    /// </summary>
    private string? ValidateExecutable(string command, string executable)
    {
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
            && command.Contains('%', StringComparison.Ordinal))
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
            var subcommandError = ValidateGitSubcommand(command);
            if (subcommandError is not null)
                return subcommandError;
        }

        var blockedPattern = _blockedPatterns
            .FirstOrDefault(pattern => ContainsBlockedPattern(command, pattern));
        return blockedPattern is null ? null : $"Command contains blocked pattern: '{blockedPattern}'";
    }

    /// <summary>
    /// Null when the command may run where it would: the confinement can be built, its path
    /// arguments, working directory and agent root are granted.
    /// </summary>
    private string? ValidatePlacement(ShellCommandRequest request, string executable)
    {
        // A confinement that cannot be built fails the call: the command never runs unconfined.
        var confinementError = ValidateConfinement();
        if (confinementError is not null)
            return confinementError;

        // VFS inbound check: an argument naming a mount-prefixed virtual path the file
        // system refuses must fail the call (redacted reason) — never reach the process.
        var rights = RequiredPathRights(executable);
        var (_, argumentDenial) = RewriteInboundArguments(ParseCommand(request.Command).Arguments, rights);
        if (argumentDenial is not null)
            return $"Command argument denied by file system: {argumentDenial}{WritingCommandNote(executable, rights)}";

        // The guard judges the directory the command will run in: a relative one is made absolute
        // first, or '../p2' under the root /workspace/wt/p1 reads as '/p2' and escapes the root.
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)
            && AgentRootGuard.Check(_fileSystem, WorkingDirectoryResolver.Absolutize(request.WorkingDirectory, _fileSystem)) is { } workingDirectoryDenial)
            return $"WorkingDirectory denied: {workingDirectoryDenial.DenialReason}";

        // A root whose rights are None grants nothing to bind: the command does not run (RT-06).
        if (AgentRunScope.Current?.Root is { } agentRoot
            && _fileSystem.ResolveAndValidate(agentRoot, FileAccessRights.None).IsAllowed
            && RightsAt(agentRoot) == FileAccessRights.None)
            return $"WorkingDirectory denied: the agent working root '{agentRoot}' grants no access.";

        return null;
    }

    /// <summary>Null when a job may start: a run holds the registry, it has room, and a mount takes the log.</summary>
    private string? ValidateStartJob()
    {
        if (ShellJobRegistry.Current() is not { } registry)
            return "start_job needs a running agent run";
        if (registry.IsFull)
            return $"{ShellJobRegistry.MaxLiveJobs} jobs are already running: wait for one to exit";
        return JobDirectory() is null ? "start_job needs a writable mount for the job log, and none is available" : null;
    }

    /// <summary>The virtual directory of the job logs: <c>/output/jobs</c> when /output is writable, else under the first writable mount.</summary>
    private string? JobDirectory()
    {
        var mount = _fileSystem.ResolveAndValidate("/output", FileAccessRights.Write).IsAllowed
            ? "/output"
            : _fileSystem.GetAvailableMounts().FirstOrDefault(m => m.DefaultRights.HasFlag(FileAccessRights.Write))?.VirtualPath;
        return mount is null ? null : mount.TrimEnd('/') + "/jobs";
    }

    /// <inheritdoc />
    protected override Task<ShellCommandResponse> ExecuteTypedAsync(
        ShellCommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Action switch
        {
            JobStatusAction => Task.FromResult(JobStatus(request.JobId)),
            JobTailAction => TailAsync(request, cancellationToken),
            _ => ExecuteCoreAsync(request, cancellationToken),
        };
    }

    private static ShellCommandResponse JobStatus(string jobId)
    {
        var job = ShellJobRegistry.Current()?.Find(jobId) ?? throw new InvalidOperationException($"Unknown job '{jobId}' in this run");
        var status = ShellJobRegistry.Status(job);
        return new ShellCommandResponse
        {
            JobId = job.Handle.Id,
            OutputPath = job.OutputPath,
            Status = status.Running ? "running" : "exited",
            ExitCode = status.ExitCode,
        };
    }

    /// <summary>The last lines of a job's log: one read of at most <see cref="MaxTailBytes"/> bytes from the end of the file.</summary>
    private async Task<ShellCommandResponse> TailAsync(ShellCommandRequest request, CancellationToken cancellationToken)
    {
        var job = ShellJobRegistry.Current()?.Find(request.JobId) ?? throw new InvalidOperationException($"Unknown job '{request.JobId}' in this run");
        var lines = Math.Clamp(request.TailLines, 1, MaxTailLines);

        var running = ShellJobRegistry.Status(job).Running;
        string text;

        // A running job whose session keeps its output in memory is read there: the file is held open for writing.
        if (running && job.Handle.LiveTail?.Invoke() is { } live)
        {
            text = LastLines(live.Text, live.Truncated, lines);
        }
        else
        {
            var stream = await _fileSystem.OpenReadStreamAsync(job.OutputPath, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var start = Math.Max(0, stream.Length - MaxTailBytes);
                stream.Seek(start, SeekOrigin.Begin);
                var buffer = new byte[stream.Length - start];
                var filled = 0;
                int read;
                while (filled < buffer.Length
                       && (read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false)) > 0)
                    filled += read;

                text = LastLines(System.Text.Encoding.UTF8.GetString(buffer, 0, filled), start > 0, lines);
            }
        }

        return new ShellCommandResponse
        {
            JobId = job.Handle.Id,
            OutputPath = job.OutputPath,
            Status = running ? "running" : "exited",
            Tail = Truncate(RewriteOutboundPaths(text, BuildOutboundReplacements())),
        };
    }

    /// <summary>The last <paramref name="lines"/> lines of <paramref name="text"/>; the first line is dropped when the text starts mid-line.</summary>
    private static string LastLines(string text, bool startsMidLine, int lines)
    {
        var parts = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var skip = startsMidLine && parts.Length > 1 ? 1 : 0;
        var end = parts.Length > 0 && parts[^1].Length == 0 ? parts.Length - 1 : parts.Length;
        return string.Join('\n', parts[Math.Max(skip, end - lines)..Math.Max(skip, end)]);
    }

    private async Task<ShellCommandResponse> StartJobAsync(
        IShellSandbox sandbox, ShellSessionView view, ShellInvocation invocation, CancellationToken cancellationToken)
    {
        var registry = ShellJobRegistry.Current() ?? throw new InvalidOperationException("start_job needs a running agent run");
        var directory = JobDirectory() ?? throw new InvalidOperationException("start_job needs a writable mount for the job log, and none is available");
        var id = Guid.NewGuid().ToString("N")[..12];
        var outputPath = $"{directory}/{id}.log";

        // Creating the empty log makes the directory and checks the rights; the job then writes into it.
        await _fileSystem.WriteAllTextAsync(outputPath, "", cancellationToken).ConfigureAwait(false);
        var logPath = _fileSystem.ResolveAndValidate(outputPath, FileAccessRights.Write).ResolvedPath
            ?? throw new InvalidOperationException("The job log could not be resolved");

        var job = await registry.StartAsync(
            () => Sessions.StartDetachedAsync(sandbox, view, invocation, logPath, ct => _fileSystem.OpenWriteStreamAsync(outputPath, ct), cancellationToken), outputPath).ConfigureAwait(false);
        return new ShellCommandResponse
        {
            JobId = job.Handle.Id,
            OutputPath = outputPath,
            Status = "running",
        };
    }

    /// <summary>What a result adds when the agent's shell session was killed after the command.</summary>
    internal const string SessionResetNote = "\n[Shell session reset: the next command starts in a new session]";

    private async Task<ShellCommandResponse> ExecuteCoreAsync(
        ShellCommandRequest request, CancellationToken cancellationToken)
    {
        var detached = request.Action == StartJobAction;
        var (executable, arguments) = ParseCommand(request.Command);
        var rights = RequiredPathRights(executable);
        var sandbox = ActiveSandbox;
        var confined = sandbox.UsesVirtualPaths;

        // Virtual→physical: the command only understands physical paths, unless its session is
        // confined and sees the virtual ones. Validation already vetoed denials; this re-run is
        // belt-and-braces against a scope swap, and the check a confined call keeps.
        var (rewrittenArguments, argumentDenial) = RewriteInboundArguments(arguments, rights);
        if (argumentDenial is not null)
            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = $"Argument denied: {argumentDenial}{WritingCommandNote(executable, rights)}",
                Completed = false
            };
        if (!confined)
            arguments = rewrittenArguments;

        var (workingDirectory, workingDirectoryDenial) = ResolveWorkingDirectory(request.WorkingDirectory, rights);
        if (workingDirectoryDenial is not null)
            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = $"WorkingDirectory denied: {workingDirectoryDenial}{WritingCommandNote(executable, rights)}",
                Completed = false
            };

        // Validation proved the confinement works; it is checked again, never assumed (a session on the
        // host is always available, a confined one on virtual or on physical paths may not be).
        var availability = sandbox.Availability;
        if (!availability.IsAvailable)
            return ConfinementUnavailable(availability.Reason);

        // A confined session sees the mounts at their virtual paths; an empty directory keeps the
        // session's own (the root of the confined view, the host's directory otherwise).
        var invocationDirectory = confined && workingDirectory.Length > 0
            ? _fileSystem.ToVirtualPath(workingDirectory) ?? "/"
            : workingDirectory;

        // NOSONAR — the executable is a bare, allowlisted name (validated in ValidateTypedRequest),
        // resolved on the session's PATH by design: the allowed commands (git, dotnet, npm, etc.)
        // may be installed in different locations across environments.
        // On Windows, shell built-in commands (echo, dir, etc.) are not standalone executables:
        // the Windows session runs them through `cmd.exe /d /v:off /s /c`, inside its job (and its
        // AppContainer when confined), every argument escaped so that cmd reads no operator in it
        // (WindowsCommandLine.CmdBuiltin). Forbidden operators and '%' were already rejected in
        // ValidateTypedRequest.
        var windowsBuiltin = !confined && OperatingSystem.IsWindows() && s_windowsBuiltins.Contains(executable);
        var invocation = new ShellInvocation(
            executable, arguments, invocationDirectory, TimeSpan.FromSeconds(request.TimeoutSeconds), windowsBuiltin, _outputLimit);

        // Physical→virtual table for the output and the view of the session, built per call: mounts
        // can be swapped per async flow (IFileSystemScope), so nothing here may be cached in the ctor.
        var outboundReplacements = BuildOutboundReplacements();
        var confinedPaths = await BuildConfinedPathsAsync(cancellationToken).ConfigureAwait(false);
        var view = new ShellSessionView(confinedPaths, [.. Confinement.ReadOnlyPaths], Confinement.AllowNetwork)
        {
            PropagatedVariables = PropagatedVariables,
            SessionBus = SessionBus,
        };

        if (detached)
            return await StartJobAsync(sandbox, view, invocation, cancellationToken).ConfigureAwait(false);

        ShellRunResult result;
        try
        {
            result = await Sessions.RunAsync(sandbox, view, invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // The message names the working directory, a RESOLVED PHYSICAL path — so a command
            // that is simply not installed on the host (npm, dotnet, git) put a disk path in front
            // of the model. Ordinary trigger, ADR-008 violation: it is rewritten like the output.
            throw new InvalidOperationException(
                RewriteOutboundPaths(ex.Message, outboundReplacements), ex);
        }
        catch (ShellSessionException ex)
        {
            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = "",
                Stderr = Truncate(RewriteOutboundPaths(ex.Message, outboundReplacements)) + SessionResetNote,
                Completed = false
            };
        }

        if (result.TimedOut)
        {
            LogCommandTimedOut(request.Command, request.TimeoutSeconds);
            var timedOutStdout = RewriteOutboundPaths(result.Stdout, outboundReplacements);
            var timedOutStderr = RewriteOutboundPaths(result.Stderr, outboundReplacements) + "\n[Command timed out]";
            return new ShellCommandResponse
            {
                ExitCode = -1,
                Stdout = Truncate(timedOutStdout),
                Stderr = Truncate(timedOutStderr) + (result.SessionReset ? SessionResetNote : ""),
                Completed = false,
                AlreadyBounded = IsCut(timedOutStdout) || IsCut(timedOutStderr)
            };
        }

        LogCommandCompleted(request.Command, result.ExitCode);
        var completedStdout = RewriteOutboundPaths(result.Stdout, outboundReplacements);
        var completedStderr = RewriteOutboundPaths(result.Stderr, outboundReplacements);
        return new ShellCommandResponse
        {
            ExitCode = result.ExitCode,
            Stdout = Truncate(completedStdout),
            Stderr = Truncate(completedStderr),
            Completed = true,
            AlreadyBounded = IsCut(completedStdout) || IsCut(completedStderr)
        };
    }

    // ── Confinement ────────────────────────────────────────────────────────

    /// <summary>
    /// Null when the call may run: no confinement, or the confining backend works and the extra
    /// read-only paths are absolute. Otherwise the reason the call fails.
    /// </summary>
    private string? ValidateConfinement()
    {
        var sandbox = ActiveSandbox;
        var relative = sandbox switch
        {
            { UsesVirtualPaths: true } or SeatbeltShellSandbox => Confinement.ReadOnlyPaths.FirstOrDefault(p => !p.StartsWith('/')),
            AppContainerShellSandbox => AppContainerShellSandbox.FirstUnqualifiedPath(Confinement.ReadOnlyPaths),
            _ => null,
        };
        if (relative is not null)
            return $"Confinement misconfigured: {ShellToolOptions.SectionName}:Confinement:ReadOnlyPaths holds '{relative}', "
                + "which is not an absolute path. The command did not run.";

        var availability = sandbox.Availability;
        if (availability.IsAvailable)
            return null;

        LogConfinementUnavailable(availability.Reason ?? "");
        return ConfinementUnavailable(availability.Reason).Stderr;
    }

    /// <summary>The failure of a call whose confinement cannot run, with the way to turn it off.</summary>
    private ShellCommandResponse ConfinementUnavailable(string? reason) => new()
    {
        ExitCode = -1,
        Stdout = "",
        Stderr = $"Confinement unavailable: {ShellToolOptions.SectionName}:Confinement:Mode is {Confinement.Mode}, "
            + $"but {reason}. The command did not run. Install the backend, or set "
            + $"{ShellToolOptions.SectionName}:Confinement:Mode = None to run shell commands unconfined.",
        Completed = false
    };

    /// <summary>
    /// The mounts the process sees under bubblewrap, each at its virtual path: the agent-facing mounts
    /// with their default rights, then their sub-path overrides. A mount that grants neither Read nor
    /// Write is left out; a sub path that grants neither is covered when it exists. Built per call, like
    /// the outbound table: mounts can be swapped per async flow (IFileSystemScope).
    /// </summary>
    private async Task<List<ConfinedPath>> BuildConfinedPathsAsync(CancellationToken cancellationToken)
    {
        var paths = new List<ConfinedPath>();
        var agentRoot = AgentRunScope.Current?.Root?.TrimEnd('/');
        foreach (var mount in _fileSystem.GetAvailableMounts())
            await AddMountPathsAsync(paths, mount, agentRoot, cancellationToken).ConfigureAwait(false);

        return paths;
    }

    /// <summary>
    /// Adds the paths <paramref name="mount"/> binds: its root (or the agent root it holds), then its
    /// sub-path overrides, then the git dirs of a linked worktree at the agent root.
    /// </summary>
    private async Task AddMountPathsAsync(List<ConfinedPath> paths, MountInfo mount, string? agentRoot, CancellationToken cancellationToken)
    {
        var virtualRoot = mount.VirtualPath.TrimEnd('/');
        if (virtualRoot.Length == 0)
            return;

        // Asking for no right resolves the root whatever it grants: the rights are applied below.
        var root = _fileSystem.ResolveAndValidate(virtualRoot, FileAccessRights.None);
        if (!root.IsAllowed || string.IsNullOrEmpty(root.ResolvedPath))
            return;

        // A declared agent root narrows the mount that holds it to the root, at its physical path,
        // never the whole mount (RT-06); the other mounts stay bound as they are.
        var heldRoot = agentRoot is not null && MatchesMountPrefix(agentRoot, virtualRoot) ? agentRoot : null;
        if (heldRoot is null)
        {
            if (Granted(mount.DefaultRights) is { } access)
                paths.Add(new ConfinedPath(root.ResolvedPath, virtualRoot, access));
            AddOverridePaths(paths, mount.Overrides, root.ResolvedPath, virtualRoot, virtualRoot);
            return;
        }

        var rootAccess = AddAgentRootPath(paths, heldRoot);
        AddOverridePaths(paths, mount.Overrides, root.ResolvedPath, virtualRoot, heldRoot);
        if (rootAccess is { } heldAccess)
            await AddGitWorktreePathsAsync(paths, mount.Overrides, heldRoot, virtualRoot, root.ResolvedPath, heldAccess, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Adds, after every other path of the mount, the git dirs a linked worktree at <paramref name="agentRoot"/>
    /// commits through (#188), then the overrides under them, each narrowed to what the git dir that holds
    /// it grants: an override narrows a git dir, never widens one.
    /// </summary>
    private async Task AddGitWorktreePathsAsync(
        List<ConfinedPath> paths, IReadOnlyList<SubPathOverride> mountOverrides, string agentRoot, string virtualRoot,
        string physicalRoot, ConfinedAccess rootAccess, CancellationToken cancellationToken)
    {
        var binds = await GitWorktreeBinds.FindAsync(_fileSystem, agentRoot, new GitWorktreeMount(virtualRoot, physicalRoot), rootAccess,
            virtualPath => Granted(RightsAt(virtualPath)), _logger, cancellationToken).ConfigureAwait(false);
        if (binds.Count == 0)
            return;

        paths.AddRange(binds.Select(b => b.Path));
        var overrides = new List<GitOverride>();
        foreach (var subPath in mountOverrides)
        {
            var relative = subPath.RelativePath.Trim('/');
            var source = Path.Combine(physicalRoot, relative);
            if (relative.Length > 0 && (Granted(subPath.Rights) ?? BubblewrapProbe.HiddenKindOf(source)) is { } access)
                overrides.Add(new GitOverride($"{virtualRoot}/{relative}", source, access));
        }

        paths.AddRange(GitWorktreeBinds.Narrow(binds, overrides));
    }

    /// <summary>Adds the agent root a mount holds, with the rights in force there; how it is bound, or null when it is not.</summary>
    private ConfinedAccess? AddAgentRootPath(List<ConfinedPath> paths, string agentRoot)
    {
        // The rights in force at the root, as the file system resolves them (an override above
        // the root counts), not the mount's default.
        var narrowed = _fileSystem.ResolveAndValidate(agentRoot, FileAccessRights.None);
        if (!narrowed.IsAllowed || string.IsNullOrEmpty(narrowed.ResolvedPath) || Granted(RightsAt(agentRoot)) is not { } rootAccess)
            return null;

        paths.Add(new ConfinedPath(narrowed.ResolvedPath, agentRoot, rootAccess));
        return rootAccess;
    }

    /// <summary>Adds the sub-path overrides of a mount that lie under <paramref name="area"/>, at their virtual path.</summary>
    private static void AddOverridePaths(
        List<ConfinedPath> paths, IReadOnlyList<SubPathOverride> overrides, string physicalRoot, string virtualRoot, string area)
    {
        foreach (var subPath in overrides)
        {
            var relative = subPath.RelativePath.Trim('/');
            if (relative.Length == 0)
                continue;

            var virtualPath = $"{virtualRoot}/{relative}";
            if (!MatchesMountPrefix(virtualPath, area))
                continue;

            var source = Path.Combine(physicalRoot, relative);
            var granted = Granted(subPath.Rights) ?? BubblewrapProbe.HiddenKindOf(source);
            if (granted is { } subAccess)
                paths.Add(new ConfinedPath(source, virtualPath, subAccess));
        }
    }

    /// <summary>The Read and Write rights in force at <paramref name="virtualPath"/>, as the file system resolves them.</summary>
    private FileAccessRights RightsAt(string virtualPath) =>
        (_fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read).IsAllowed ? FileAccessRights.Read : FileAccessRights.None)
        | (_fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Write).IsAllowed ? FileAccessRights.Write : FileAccessRights.None);

    /// <summary>How a path with <paramref name="rights"/> is bound, or null when it grants neither Read nor Write.</summary>
    private static ConfinedAccess? Granted(FileAccessRights rights)
    {
        if (rights.HasFlag(FileAccessRights.Write))
            return ConfinedAccess.ReadWrite;
        return rights.HasFlag(FileAccessRights.Read) ? ConfinedAccess.ReadOnly : null;
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
    /// The physical directory the child process starts in, or the denial to report.
    /// <para>
    /// A named directory is made absolute against the working directory when it is relative,
    /// then resolved and rights-checked like any other path. Named nothing, the command runs in
    /// <see cref="WorkingDirectoryResolver.Current"/>: the agent's root under an agent run, else the
    /// file system's working directory, the same directory the file tools resolve a relative
    /// path against (#199). With no mounts at all the process inherits its parent's directory,
    /// the same "no mounts means exact no-op" stance the argument rewriting takes. With mounts but
    /// no working directory the call is refused with a reason that says how to name one: guessing
    /// one is what the removed fallback did.
    /// </para>
    /// <para>
    /// That fallback took the first agent-facing mount granting the rights asked, in the
    /// registry's order (longest virtual path first): with <c>/session-history</c> registered,
    /// a read-only command ran there instead of in <c>/workspace</c>. The default before it was
    /// the literal <c>"."</c>, which no registry can resolve (ADR-008).
    /// </para>
    /// <para>
    /// A command named no directory, run under no agent root, whose working directory is refused
    /// for its rights alone (it lacks one of the rights <see cref="RequiredPathRights"/> yields:
    /// Read for a read-only command, every right - read, write, create, delete - for any other,
    /// so a writing command in the read-only <c>/workspace</c> of <c>orkeon forge</c>) runs in the
    /// designated write directory instead (<see cref="IFileSystemService.WriteDirectory"/>,
    /// <c>/output</c> by default): a choice the host makes, never the order of the mounts. A
    /// working directory granting every right never falls back. Under an agent root the root
    /// rules, any other refusal (no mount, a traversal, a reserved path) is reported as is, and
    /// with no write directory the refusal names the option.
    /// </para>
    /// </summary>
    private (string WorkingDirectory, string? DenialReason) ResolveWorkingDirectory(
        string requested, FileAccessRights rights)
    {
        var named = !string.IsNullOrWhiteSpace(requested);
        var virtualDirectory = named
            ? WorkingDirectoryResolver.Absolutize(requested, _fileSystem)
            : WorkingDirectoryResolver.Current(_fileSystem);

        if (string.IsNullOrWhiteSpace(virtualDirectory))
        {
            var mounts = _fileSystem.GetAvailableMounts();
            if (mounts.Count == 0)
                return ("", null);

            return ("", "no working directory is set: name one in working_directory, an absolute virtual path "
                + $"that starts with a mount ({string.Join(", ", mounts.Select(m => m.VirtualPath))}).");
        }

        if (AgentRootGuard.Check(_fileSystem, virtualDirectory) is { } outsideRoot)
            return ("", outsideRoot.DenialReason);

        var resolved = _fileSystem.ResolveAndValidate(virtualDirectory, rights);
        if (resolved.IsAllowed)
            return (resolved.ResolvedPath!, null);
        if (named || AgentRunScope.Current?.Root is not null || !IsRightsDenial(virtualDirectory))
            return ("", resolved.DenialReason);

        var writeDirectory = _fileSystem.WriteDirectory;
        if (string.IsNullOrWhiteSpace(writeDirectory)
            || string.Equals(writeDirectory, virtualDirectory, StringComparison.Ordinal))
            return ("", resolved.DenialReason + " " + NoWriteDirectoryHint);

        var fallback = _fileSystem.ResolveAndValidate(writeDirectory, rights);
        return fallback.IsAllowed
            ? (fallback.ResolvedPath!, null)
            : ("", resolved.DenialReason + " " + NoWriteDirectoryHint);
    }

    /// <summary>
    /// Whether the working directory was refused for its rights alone: the same resolution asking
    /// no right succeeds, so the mount exists, the path stays inside it and the path validator
    /// accepts it. Any other refusal (no mount, a traversal, a reserved path) is reported as is:
    /// running elsewhere would hide it.
    /// </summary>
    private bool IsRightsDenial(string virtualDirectory)
        => _fileSystem.ResolveAndValidate(virtualDirectory, FileAccessRights.None).IsAllowed;

    /// <summary>What a refused command without a directory can do: name one, or have the host designate one.</summary>
    private const string NoWriteDirectoryHint =
        "No write directory grants it either: name a writable directory in working_directory, "
        + "or designate one with Orkeon:FileSystem:WriteDirectory.";

    /// <summary>
    /// Rewrites mount-prefixed virtual arguments to their physical paths. Returns the
    /// original array untouched together with the (already redacted) denial reason when
    /// the file system refuses one of them. No mounts ⇒ exact no-op.
    /// </summary>
    private (string[] Arguments, string? DenialReason) RewriteInboundArguments(
        string[] arguments, FileAccessRights rights)
    {
        var mounts = _fileSystem.GetAvailableMounts();
        if (mounts.Count == 0 || arguments.Length == 0)
            return (arguments, null);

        var rewritten = new string[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var (token, denial) = TryRewriteToken(arguments[i], mounts, rights);
            if (denial is not null)
                return (arguments, denial);
            rewritten[i] = token;
        }

        return (rewritten, null);
    }

    /// <summary>
    /// The rights a path named by this command must grant, its working directory included.
    /// <para>
    /// The file system can only check what the tool asks of it, and the process works on the
    /// physical path afterwards. A command of the read-only default allowlist (<c>git</c> while
    /// its read-only restriction holds) reads what it is given, so Read is what it uses. Any
    /// other command (an interpreter, an extra or custom command, <c>git</c> once interpreters
    /// lift its restriction) may write, create or delete there, and nothing tells which, so its
    /// paths need every right: <see cref="FileAccessRights.ReadWrite"/>, that is read, write,
    /// create and delete together, not Write alone. Asking it for Read alone looked like a guarantee the tool did not
    /// give: <c>find /docs -delete</c> passed on a read-only <c>/docs</c>.
    /// </para>
    /// </summary>
    private FileAccessRights RequiredPathRights(string executable)
        => s_readOnlyCommands.Contains(executable)
           && !(_interpretersAllowed && executable.Equals("git", StringComparison.OrdinalIgnoreCase))
            ? FileAccessRights.Read
            : FileAccessRights.ReadWrite;

    /// <summary>
    /// What a denial adds for a command that can write: why it needed more than Read, and where
    /// the content of a read-only mount stays reachable.
    /// </summary>
    private static string WritingCommandNote(string executable, FileAccessRights rights)
        => rights == FileAccessRights.Read
            ? ""
            : $" '{executable}' can write, so a path it is given and its working directory need every right "
              + "(read, write, create, delete); a read-only mount stays readable through the file tools.";

    /// <summary>
    /// Rewrites one token: a whole-token mount match wins; otherwise the embedded
    /// <c>key=/mount/…</c> form splits at the FIRST <c>=</c> and resolves the entire
    /// right side (even if it contains further <c>=</c>). Anything else stays verbatim.
    /// </summary>
    private (string Token, string? DenialReason) TryRewriteToken(
        string token, IReadOnlyList<MountInfo> mounts, FileAccessRights rights)
    {
        if (mounts.Any(m => MatchesMountPrefix(token, m.VirtualPath)))
        {
            var result = AgentRootGuard.Check(_fileSystem, token) ?? _fileSystem.ResolveAndValidate(token, rights);
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
                var result = AgentRootGuard.Check(_fileSystem, right) ?? _fileSystem.ResolveAndValidate(right, rights);
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
    /// Validates that a <c>git</c> command uses a read-only subcommand
    /// (<c>status</c>/<c>log</c>/<c>diff</c>/<c>show</c>) AND that none of its options
    /// breaks that promise. The first non-option token after <c>git</c> is treated as the
    /// subcommand; any leading option (e.g. <c>-c …</c>) is rejected because it can alter
    /// execution behaviour, and every remaining token is scanned against
    /// <see cref="s_forbiddenGitOptions"/>.
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

        // The subcommand alone does not make the call read-only: the options it accepts do.
        var forbiddenOption = FindForbiddenGitOption(tokens);
        if (forbiddenOption is not null)
            return $"git option '{forbiddenOption}' is not allowed; it would write, execute, or leave the working directory";

        return null;
    }

    /// <summary>
    /// Returns the first option of <see cref="s_forbiddenGitOptions"/> present in
    /// <paramref name="tokens"/>, or null when the command line carries none. A long option
    /// is matched on its name, so <c>--output=&lt;file&gt;</c> and <c>--output &lt;file&gt;</c>
    /// are the same option; a short one is matched on its prefix, because git lets it carry
    /// its value attached (<c>-o/tmp/x</c>).
    /// </summary>
    private static string? FindForbiddenGitOption(IReadOnlyList<string> tokens)
    {
        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Length < 2 || token[0] != '-')
                continue;

            var equals = token.IndexOf('=', StringComparison.Ordinal);
            var name = equals > 0 ? token[..equals] : token;

            foreach (var option in s_forbiddenGitOptions)
            {
                var matched = option.StartsWith("--", StringComparison.Ordinal)
                    ? string.Equals(name, option, StringComparison.Ordinal)
                    : token.StartsWith(option, StringComparison.Ordinal);
                if (matched)
                    return option;
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="pattern"/> occurs in <paramref name="command"/> at a token
    /// boundary: it must start the command or follow whitespace, and — when it ends on a
    /// letter or a digit — it must not continue into a longer word. Patterns that end on
    /// punctuation (<c>dd if=</c>, <c>rm -rf /</c>) carry their own right boundary.
    /// <para>
    /// The check used to be a bare <c>Contains</c>, which read the intent of every entry
    /// wrongly: <c>format</c> is the disk-formatting command, and the substring match refused
    /// the legitimate <c>git log --format=%H</c> and every <c>--pretty=format:</c> with it.
    /// </para>
    /// </summary>
    private static bool ContainsBlockedPattern(string command, string pattern)
    {
        if (pattern.Length == 0)
            return false;

        var index = command.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var startsToken = index == 0 || char.IsWhiteSpace(command[index - 1]);
            var end = index + pattern.Length;
            var endsToken = end == command.Length
                || !char.IsLetterOrDigit(pattern[^1])
                || !char.IsLetterOrDigit(command[end]);

            if (startsToken && endsToken)
                return true;

            index = command.IndexOf(pattern, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool IsCut(string value) => value.Length > _outputLimit;

    /// <summary>Truncates a string to the configured output limit, telling how many characters were omitted.</summary>
    private string Truncate(string value) =>
        Orkeon.Domain.Tools.ToolResultLimitsOptions.Cut(value, _outputLimit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shell command completed: {Command} (exit code: {ExitCode})")]
    private partial void LogCommandCompleted(string command, int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shell command timed out: {Command} (timeout: {TimeoutSeconds}s)")]
    private partial void LogCommandTimedOut(string command, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shell command refused: the configured confinement is unavailable ({Reason})")]
    private partial void LogConfinementUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SECURITY: ShellCommandTool interpreter opt-in enabled (node/dotnet/npm/find). This is RCE-equivalent: commands run on the host with NO OS confinement. Do not expose this tool to untrusted (LLM-produced) input in this mode.")]
    private partial void LogInterpretersEnabled();
}
