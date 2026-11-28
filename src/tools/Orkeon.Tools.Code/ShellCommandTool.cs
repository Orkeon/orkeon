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
        Example = "start_job",
        Enum = new[] { ShellCommandTool.RunAction, ShellCommandTool.StartJobAction, ShellCommandTool.JobStatusAction, ShellCommandTool.JobTailAction })]
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
    /// <summary>Gets whether the shell cut its own output, so the result is not cut again. Internal to the tool: not in the return schema.</summary>
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

    /// <summary>Gets whether <see cref="Stdout"/> was cut: its head and its tail kept around a marker.</summary>
    [ReturnSchema(Description = "Whether stdout was cut (head and tail kept around a marker, which says how much of the output output_path holds)", Example = false)]
    public bool StdoutTruncated { get; init; }

    /// <summary>Gets whether <see cref="Stderr"/> was cut: its head and its tail kept around a marker.</summary>
    [ReturnSchema(Description = "Whether stderr was cut (head and tail kept around a marker, which says how much of the output output_path holds)", Example = false)]
    public bool StderrTruncated { get; init; }

    /// <summary>
    /// Gets the length of the whole standard output, in characters, cut or not; a middle the session dropped
    /// and no capture copy counted (output_path empty, or holding only the first and last parts) counts in bytes.
    /// </summary>
    [ReturnSchema(Description = "Length of the whole stdout, cut or not: characters, except a middle the session dropped and output_path does not hold, counted in bytes", Example = 5)]
    public long StdoutTotalChars { get; init; }

    /// <summary>
    /// Gets the length of the whole standard error, in characters, cut or not; a middle the session dropped
    /// and no capture copy counted (output_path empty, or holding only the first and last parts) counts in bytes.
    /// </summary>
    [ReturnSchema(Description = "Length of the whole stderr, cut or not: characters, except a middle the session dropped and output_path does not hold, counted in bytes", Example = 0)]
    public long StderrTotalChars { get; init; }

    /// <summary>Gets whether the command completed within the timeout.</summary>
    [ReturnSchema(Description = "Whether the command completed within the timeout", Example = true)]
    public bool Completed { get; init; }

    /// <summary>Gets the id of the job, for the job actions.</summary>
    [ReturnSchema(Description = "Job id, for the job actions", Example = "3f9a1c0b27d4")]
    public string JobId { get; init; } = "";

    /// <summary>Gets the virtual path of the job's log, or of the whole output of a <c>run</c> whose output was cut.</summary>
    [ReturnSchema(Description = "Virtual path of the job's log file, or of the output of a run whose stdout or stderr was cut (stdout, then a [stderr] line and stderr, paths virtualized; at most 8 MiB of each stream, the 32 most recent files kept)", Example = "/output/jobs/3f9a1c0b27d4.log")]
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
/// logs a security line at the first call. In that mode the tool is
/// remote-code-execution-equivalent inside the confinement in force; it is unconfined only in a
/// container or with <c>Mode=None</c>. If you need to run untrusted interpreter code, route it
/// through an isolating <c>ICodeSandbox</c> (Docker <c>--network=none</c>) instead.
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
[ToolContract("shell_command", ActsOnWorkspace = true,
    Name = "shell_command",
    Description = "Runs a shell command in the working directory and returns its exit code, stdout and stderr, each cut at 10 000 characters keeping its head and its tail around a marker that counts what was left out; a cut run says stdout_truncated or stderr_truncated, gives the lengths in stdout_total_chars and stderr_total_chars (characters; a middle the session dropped and output_path does not hold is counted in bytes), and writes the output to output_path (at most 8 MiB of each stream, the marker saying when it is not whole), which file_read reads; a command the allowlist or blocklist refuses is not run. git runs status, log, diff and show only, with the options the tool adds so that the repository's configuration runs no program (pager, hooks, fsmonitor, signature verification, filter and diff drivers, lazy fetch, submodules); a %G format placeholder is refused on the command line and neutralised from the configuration (pretty.<name>, format.pretty), and an option that would undo the tool's own, written in full or abbreviated (status -v included), is refused. action run (default) waits up to timeout seconds; start_job starts it detached and returns a job_id, which job_status and job_tail read later in the same run. To read or search files, file_read, grep and glob cost less than cat and find.",
    Category = "Code Operations")]
public partial class ShellCommandTool : ToolBase<ShellCommandRequest, ShellCommandResponse>
{
    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    public override bool UsesFileSystem => true;

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
    /// The Windows spelling of a mount's base under WSL, by virtual path (<c>/workspace</c> →
    /// <c>C:\proj</c>; <c>Orkeon:Tools:Shell:WindowsPaths</c>, which a launcher fills from <c>wslpath -w</c>).
    /// Under WSL, a mount it does not name and whose base lies on a Windows drive (<c>/mnt/c/proj</c>) gets
    /// the spelling of that drive. Outside WSL it is not read (#302).
    /// </summary>
    public IReadOnlyDictionary<string, string> WindowsPaths { get; init; } = new Dictionary<string, string>();

    /// <summary>What the tool knows of WSL. Replaced by the tests.</summary>
    internal WslInterop Wsl { get; init; } = WslInterop.Host;

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
        _defaultSandbox ??= new ShellSandboxSelector(UnconfinedShellSandbox.Shared, new BubblewrapShellSandbox(Bubblewrap), new SeatbeltShellSandbox(Seatbelt), ContainerDetector, Platform, Logger)
            .Select(Confinement.Mode);
        return _defaultSandbox;
    }

    /// <summary>Tells, for the interpreter opt-in line, the confinement the commands run under.</summary>
    private string DescribeConfinement()
    {
        var mode = Confinement.Mode;
        var network = Confinement.AllowNetwork ? "network on" : "network off";
        var resolution = ShellConfinementResolver.Resolve(
            mode, Platform, mode == ShellConfinementMode.Auto && ContainerDetector.IsInContainer());
        if (resolution.Backend is null)
            return $"unavailable ({resolution.UnavailableReason}): every call will fail rather than run unconfined";
        if (resolution.Backend == ShellConfinementBackend.None)
            return resolution.Notice is not null
                ? $"none (running in a container, network on - set {ShellToolOptions.SectionName}:Confinement:Mode to force it)"
                : $"none (Mode=None): commands run on the host unconfined, network on. Do not expose this tool to untrusted input.";
        var availability = ActiveSandbox.Availability;
        if (!availability.IsAvailable)
            return $"{resolution.Backend} is unavailable ({availability.Reason}): every call will fail rather than run unconfined";
        return resolution.Backend == ShellConfinementBackend.Bubblewrap
            ? $"bubblewrap (the VFS view only, {network}, system directories are read-only)"
            : $"{resolution.Backend} (the VFS view only, {network})";
    }

    /// <summary>Maximum number of characters captured from stdout/stderr.</summary>
    public const int MaxOutputLength = 10_000;

    /// <summary>The action that runs a command and waits for it (the default).</summary>
    public const string RunAction = "run";

    /// <summary>The lines <c>job_tail</c> returns when the request names none.</summary>
    public const int DefaultTailLines = 50;

    internal const string StartJobAction = "start_job";
    internal const string JobStatusAction = "job_status";
    internal const string JobTailAction = "job_tail";
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
    /// RCE-equivalent inside the confinement in force; it is unconfined only in a
    /// container or with <c>Mode=None</c>.
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
    private static readonly HashSet<string> s_allowedGitSubcommands = new(StringComparer.Ordinal)
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
        "--no-index",
        // Verify signatures, which runs the gpg.program the repository configures.
        "--show-signature",
        // Undo the tool's --ignore-submodules=all: a submodule's git reads its own configuration.
        "--ignore-submodules", "--submodule"
    ];

    /// <summary>
    /// Long options whose <c>--no-</c> form is refused too, because it undoes what the tool injects
    /// (<c>--ignore-submodules=all</c>, <c>--no-ext-diff</c>, <c>--no-textconv</c>).
    /// </summary>
    private static readonly string[] s_forbiddenGitNegatedOptions =
        ["--ignore-submodules", "--submodule", "--no-ext-diff", "--no-textconv"];

    /// <summary>
    /// The options refused after <c>status</c> alone: a verbose status shows the staged diff
    /// (<c>-vv</c> the work tree's too), which runs the <c>diff.&lt;driver&gt;.textconv</c> filter the
    /// repository's attributes name, and <c>status</c> takes no <c>--no-textconv</c> to stop it.
    /// </summary>
    private static readonly string[] s_forbiddenGitStatusOptions = ["--verbose"];

    /// <summary>
    /// Existing git options that happen to be prefixes of a forbidden one, matched exactly and let through:
    /// <c>--text</c> (treat every file as text) is not an abbreviation of <c>--textconv</c>.
    /// </summary>
    private static readonly string[] s_gitOptionsBesideForbiddenOnes = ["--text"];

    /// <summary>
    /// The shortest abbreviation, after <c>--</c>, a long option is matched on. Git accepts an unambiguous
    /// prefix of a long option for a command parsed by its parse-options (<c>status --ignore-sub=none</c>):
    /// a name that only begins a forbidden option is refused as that option.
    /// </summary>
    private const int MinimumGitOptionAbbreviation = 3;

    /// <summary>
    /// The options and configuration the tool sets itself, before the subcommand, on every <c>git</c> call its
    /// read-only restriction governs: the repository's own configuration must not make a read run a program
    /// or write. No pager, no optional lock (git may still refresh a stat-dirty entry and rewrite the index
    /// file, so no hook runs either: <c>core.hooksPath=/dev/null</c>, a directory that holds no hook on any
    /// platform, keeps <c>post-index-change</c> from running), no fsmonitor hook, no signature
    /// verification program (<c>gpg.program</c>, <c>gpg.ssh.program</c>, <c>gpg.x509.program</c>: a
    /// <c>%G</c> placeholder or a <c>format.pretty</c> that holds one runs it), no transport, and no
    /// submodule summary. <see cref="s_gitNoLazyFetch"/> and the emptied filter drivers
    /// (<see cref="FilterDriverOverrides"/>) complete the set. The caller's own <c>-c</c> stays refused
    /// (<see cref="s_forbiddenGitOptions"/>).
    /// </summary>
    private static readonly string[] s_gitSafeConfiguration =
    [
        "--no-pager", "--no-optional-locks",
        "-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null", "-c", "core.pager=cat", "-c", "log.showSignature=false",
        "-c", "gpg.program=false", "-c", "gpg.ssh.program=false", "-c", "gpg.x509.program=false",
        "-c", "protocol.allow=never", "-c", "diff.ignoreSubmodules=all", "-c", "status.submoduleSummary=false",
    ];

    /// <summary>
    /// No lazy fetch of a missing object of a partial clone, which runs the transport the repository's
    /// promisor remote names (an <c>ext::</c> URL is a command). <c>protocol.allow=never</c> alone does not
    /// stop it: a repository's <c>protocol.&lt;name&gt;.allow</c> wins over it. Git 2.44 and later.
    /// </summary>
    private const string s_gitNoLazyFetch = "--no-lazy-fetch";

    /// <summary>
    /// The oldest git that takes every option of the tool's safe set (<c>--no-lazy-fetch</c> came with 2.44):
    /// an older or unknown git makes a <c>git</c> call <see cref="ToolAccess.Execute"/>.
    /// </summary>
    internal static readonly Version MinimumSafeGitVersion = new(2, 44, 0);

    /// <summary>
    /// The options the tool adds right after <c>status</c>, <c>diff</c>, <c>show</c> and <c>log</c>: no
    /// submodule is entered (a submodule's git reads its own configuration, whose filter drivers the tool
    /// did not read), and for the three that diff no external diff driver (<c>diff.external</c>,
    /// <c>diff.&lt;driver&gt;.command</c>) and no textconv filter run.
    /// </summary>
    private static readonly string[] s_gitSafeSubcommandOptions = ["--ignore-submodules=all"];

    private static readonly string[] s_gitSafeDiffOptions = ["--no-ext-diff", "--no-textconv"];

    /// <summary>The version of the <c>git</c> on the host's PATH, read once (null when there is none).</summary>
    private static readonly Lazy<Version?> s_hostGitVersion = new(ProbeHostGitVersion);

    /// <summary>The version of the git a call runs; the host's by default, null when unknown.</summary>
    internal Func<Version?> GitVersion { get; init; } = static () => s_hostGitVersion.Value;

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
    /// allowlist. This is RCE-equivalent inside the confinement in force (unconfined in
    /// a container or with Mode=None) and logs a security line at the first call. Ignored when a custom <paramref name="allowedCommands"/> is supplied.
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
        : this(fileSystem, allowedCommands, blockedPatterns, logger, allowInterpreters, extraAllowedCommands, limits, new ShellSecurityNotice())
    {
    }

    internal ShellCommandTool(
        IFileSystemService fileSystem,
        IEnumerable<string>? allowedCommands,
        IEnumerable<string>? blockedPatterns,
        ILogger<ShellCommandTool>? logger,
        bool allowInterpreters,
        IEnumerable<string>? extraAllowedCommands,
        Orkeon.Domain.Tools.ToolResultLimitsOptions? limits,
        ShellSecurityNotice securityNotice)
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
        _securityNotice = securityNotice ?? throw new ArgumentNullException(nameof(securityNotice));
    }

    private readonly ShellSecurityNotice _securityNotice;

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

    /// <summary>
    /// <see cref="ToolAccess.Read"/> for a <c>run</c> (the default action) of a command that only reads:
    /// the executable is one of the default read-only commands (<c>ls</c>, <c>cat</c>, <c>pwd</c>,
    /// <c>which</c>, <c>grep</c>, <c>wc</c>, <c>echo</c>, <c>git</c>, <c>dir</c>, <c>type</c>,
    /// <c>where</c>), the tool's own checks accept it (no forbidden operator, in the allowlist, no
    /// blocked pattern) and, for <c>git</c>, the read-only subcommand and option checks pass and the git is
    /// at least <see cref="MinimumSafeGitVersion"/>, so that the tool neutralises every program the
    /// repository configures. <see cref="ToolAccess.Execute"/> otherwise: <c>start_job</c> and the job
    /// actions, a command added by <c>ExtraAllowedCommands</c> or a custom allowlist, an interpreter,
    /// <c>git</c> once interpreters lift its restriction (they are allowed: <c>git</c> is then unrestricted),
    /// an older or unknown git, and a call the tool would refuse.
    /// </summary>
    /// <param name="arguments">Arguments the model supplied for the call.</param>
    /// <returns>The access class of this call.</returns>
    public override ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var action = arguments.TryGetValue("action", out var rawAction) && rawAction is not null
            ? ActionName(rawAction) ?? StartJobAction
            : RunAction;
        if (!string.Equals(string.IsNullOrWhiteSpace(action) ? RunAction : action, RunAction, StringComparison.Ordinal))
            return ToolAccess.Execute;

        if (!arguments.TryGetValue("command", out var rawCommand) || ActionName(rawCommand) is not { } command
            || string.IsNullOrWhiteSpace(command) || FindForbiddenOperator(command) is not null)
            return ToolAccess.Execute;

        var executable = ParseCommand(command).Executable;
        if (string.IsNullOrWhiteSpace(executable)
            || ExecutableTokenError(command) is not null
            || RequiredPathRights(executable) != FileAccessRights.Read
            || ValidateExecutable(command, executable) is not null)
            return ToolAccess.Execute;

        // A git call reads only when the tool can neutralise every program the repository could make it
        // run: the read-only checks pass, and the git is recent enough for the whole safe set.
        return executable.Equals("git", StringComparison.OrdinalIgnoreCase)
               && (ValidateGitSubcommand(command) is not null || !(GitVersion() >= MinimumSafeGitVersion))
            ? ToolAccess.Execute
            : ToolAccess.Read;
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
        // The first hook of a call, so the line comes before any refusal of the command.
        if (_interpretersAllowed && _securityNotice.TryClaim())
            LogInterpretersEnabled(Logger, DescribeConfinement());
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

        // The executable is read with the parse the execution uses (ParseCommand), never a cheaper one:
        // a reading that differs lets a command be validated as one program and run as another (#494).
        var executable = ParseCommand(request.Command).Executable;
        if (string.IsNullOrWhiteSpace(executable))
            return "Command cannot be empty";
        if (ExecutableTokenError(request.Command) is { } tokenError)
            return tokenError;

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
        // (ParseCommand already strips paths, so if we still see separators
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

        // The pattern is sought in the command as written and as the process receives it, quotes
        // removed: `rm "-rf" /` runs `rm -rf /` and must not slip past the "rm -rf /" entry.
        var unquoted = string.Join(' ', TokenizeCommand(command));
        var blockedPattern = _blockedPatterns
            .FirstOrDefault(pattern => ContainsBlockedPattern(command, pattern) || ContainsBlockedPattern(unquoted, pattern));
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
        return JobDirectory() is null ? NoJobDirectory() : null;
    }

    /// <summary>Why no job log can be written: no writable mount, or, under a file-system scope (#349), no place it lets the agent write.</summary>
    private static string NoJobDirectory() => AgentRunScope.Current?.FileSystem is { } scope
        ? $"start_job needs a place the agent's file-system scope lets it write for the job log, and it grants none. Its scope: {scope.Describe()}."
        : "start_job needs a writable mount for the job log, and none is available";

    /// <summary>
    /// The virtual directory of the job logs: <c>/output/jobs</c> when /output is writable, else <c>jobs</c>
    /// under the first writable mount. Under a file-system scope (#349) the places are the scope's entries,
    /// and a directory the scope does not let the agent write is never chosen.
    /// </summary>
    private string? JobDirectory() => WritableDirectory("jobs");

    /// <summary>
    /// The virtual directory named <paramref name="leaf"/> under <c>/output</c> when writable, else under the
    /// first writable mount, as <see cref="JobDirectory"/> chooses it.
    /// </summary>
    private string? WritableDirectory(string leaf)
    {
        var scope = AgentRunScope.Current?.FileSystem;
        var mounts = _fileSystem.GetAvailableMounts();
        var places = scope is null ? mounts : scope.Narrow(mounts);
        return places.Where(m => m.DefaultRights.HasFlag(FileAccessRights.Write)).Select(m => m.VirtualPath)
            .Prepend("/output")
            .Select(place => place.TrimEnd('/') + "/" + leaf)
            .FirstOrDefault(directory => (scope?.Allows(directory, FileAccessRights.Write) ?? true)
                && _fileSystem.ResolveAndValidate(directory, FileAccessRights.Write).IsAllowed);
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
        var directory = JobDirectory() ?? throw new InvalidOperationException(NoJobDirectory());
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
        var restrictedGit = !_interpretersAllowed && executable.Equals("git", StringComparison.OrdinalIgnoreCase);
        if (restrictedGit)
            arguments = WithGitSafeOptions(arguments);
        var rights = RequiredPathRights(executable);
        var sandbox = ActiveSandbox;
        var confined = sandbox.UsesVirtualPaths;

        // Virtual→physical: the command only understands physical paths, unless its session is
        // confined and sees the virtual ones. Validation already vetoed denials; this re-run is
        // belt-and-braces against a scope swap, and the check a confined call keeps.
        // Under WSL a Windows program reads only the Windows spelling of a path (#302): the rights
        // check is the same, the path is spelt for that program once checked.
        var windowsTarget = !confined && Wsl.IsWsl && Wsl.IsWindowsProgram(executable);
        var (rewrittenArguments, argumentDenial) = RewriteInboundArguments(arguments, rights, windowsTarget);
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
        // Where a cut output of a run is kept whole, chosen as a job log is; none when no mount takes it.
        var capture = detached ? null : CaptureTarget(confined);
        var invocation = new ShellInvocation(
            executable, arguments, invocationDirectory, TimeSpan.FromSeconds(request.TimeoutSeconds), windowsBuiltin, _outputLimit,
            capture?.SeenPath, CaptureByteCap);

        // Physical→virtual table for the output and the view of the session, built per call: mounts
        // can be swapped per async flow (IFileSystemScope), so nothing here may be cached in the ctor.
        var outboundReplacements = BuildOutboundReplacements();
        var confinedPaths = await BuildConfinedPathsAsync(cancellationToken).ConfigureAwait(false);
        var view = new ShellSessionView(confinedPaths, [.. Confinement.ReadOnlyPaths], Confinement.AllowNetwork)
        {
            PropagatedVariables = PropagatedVariables,
            SessionBus = SessionBus,
        };

        if (restrictedGit)
        {
            // The filter drivers are named by the configuration only: read them, then empty each one.
            var overrides = await GitFilterDriverOverridesAsync(sandbox, view, invocation, cancellationToken).ConfigureAwait(false);
            if (overrides is null)
            {
                await (capture is null ? Task.CompletedTask : DiscardCaptureAsync(capture)).ConfigureAwait(false);
                return new ShellCommandResponse
                {
                    ExitCode = -1,
                    Stdout = "",
                    Stderr = "git not run: the repository's filter drivers could not be read or named, so the tool cannot keep them from running",
                    Completed = false,
                };
            }

            invocation = invocation with { Arguments = [.. overrides, .. invocation.Arguments] };
        }

        if (detached)
            return await StartJobAsync(sandbox, view, invocation, cancellationToken).ConfigureAwait(false);

        ShellRunResult result;
        try
        {
            try
            {
                result = await Sessions.RunAsync(sandbox, view, invocation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (capture is not null)
            {
                // A failed run leaves no orphan copy behind it.
                await DiscardCaptureAsync(capture).ConfigureAwait(false);
                throw;
            }
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

        return await BuildRunResponseAsync(request, result, outboundReplacements, capture, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The <c>-c</c> pairs that empty the filter drivers the repository's configuration defines, read by a
    /// <c>git config</c> run in the same session and directory as the call; null when that read failed or
    /// a driver cannot be named (the call is then not run).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A failed read refuses the git call; it never throws out of the tool.")]
    private async Task<string[]?> GitFilterDriverOverridesAsync(
        IShellSandbox sandbox, ShellSessionView view, ShellInvocation call, CancellationToken cancellationToken)
    {
        string[] query = [.. GitVersion() >= MinimumSafeGitVersion ? [s_gitNoLazyFetch] : Array.Empty<string>(), .. s_gitFilterDriverQuery];
        var invocation = new ShellInvocation(
            "git", query, call.WorkingDirectory, call.Timeout, OutputLimit: 64 * 1024);
        ShellRunResult result;
        try
        {
            result = await Sessions.RunAsync(sandbox, view, invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        // 1: no key matched, which a directory outside any repository answers too.
        if (result.TimedOut || result.StdoutOmitted > 0 || result.ExitCode is not (0 or 1))
            return null;
        return result.ExitCode == 1 ? [] : FilterDriverOverrides(result.Stdout);
    }

    /// <summary>Where the whole output of a run goes when it is cut: its virtual path, and the path its session writes.</summary>
    private sealed record CaptureFile(string VirtualPath, string SeenPath);

    private const string CaptureExtension = ".log";

    /// <summary>The capture files kept by default under <c>/output/shell</c>: an older one is removed when a new one is kept.</summary>
    internal const int DefaultMaxCaptureFiles = 32;

    /// <summary>The bytes of each stream a POSIX session copies for the capture file (<see cref="ShellScript.CaptureByteCap"/>).</summary>
    internal int CaptureByteCap { get; set; } = ShellScript.CaptureByteCap;

    /// <summary>The capture files kept under the capture directory (<see cref="DefaultMaxCaptureFiles"/>).</summary>
    internal int MaxCaptureFiles { get; set; } = DefaultMaxCaptureFiles;

    /// <summary>
    /// What the tool built from a POSIX session's raw copies: the characters of each stream as the capture
    /// file holds them (paths virtualized), plus the bytes past the cap, and whether the file holds them whole.
    /// </summary>
    private readonly record struct CaptureCopy(long StdoutChars, long StderrChars, bool Whole);

    /// <summary>
    /// A fresh capture file under <c>/output/shell</c> (or the first writable mount, as a job log is placed);
    /// null when no place takes it. The session sees it at its virtual path when confined.
    /// </summary>
    private CaptureFile? CaptureTarget(bool confined)
    {
        if (WritableDirectory("shell") is not { } directory)
            return null;
        // A name that sorts by its creation, so the retention keeps the most recent (see PruneCapturesAsync).
        var virtualPath = string.Create(
            System.Globalization.CultureInfo.InvariantCulture, $"{directory}/{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid().ToString("N")[..8]}{CaptureExtension}");
        var resolution = _fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Write);
        if (!resolution.IsAllowed || resolution.ResolvedPath is not { } physicalPath)
            return null;
        return new CaptureFile(virtualPath, confined ? virtualPath : physicalPath);
    }

    /// <summary>A stream as the result shows it: its head and its tail, the characters between them and its true length.</summary>
    private readonly record struct BoundedStream(string Head, string Tail, long Omitted, long Total)
    {
        public bool Truncated => Omitted > 0;

        /// <summary>The stream as the result shows it, <paramref name="note"/> (what output_path holds) in its marker.</summary>
        public string Text(string note) => !Truncated
            ? Head + Tail
            : $"{Head}\n\n[... truncated, {Omitted} chars omitted{note}. Use more specific parameters to narrow results.]\n\n{Tail}";
    }

    /// <summary>
    /// Bounds one stream the session returned (its head, its tail when the session cut it, the characters
    /// between them) to the output limit, keeping its head and its tail.
    /// </summary>
    private BoundedStream Bound(string head, string tail, long omitted, long? trueTotal = null)
    {
        // A POSIX session counts what it dropped in bytes; the capture copy, when there is one, gives characters.
        var total = trueTotal ?? head.Length + tail.Length + omitted;
        if (total <= _outputLimit)
            return new BoundedStream(head + tail, "", 0, total);

        var keepTail = _outputLimit / 2;
        var keepHead = _outputLimit - keepTail;
        var headPart = head[..Math.Min(keepHead, head.Length)];
        var tailPart = tail.Length == 0
            ? head[Math.Max(headPart.Length, head.Length - keepTail)..]
            : tail[Math.Max(0, tail.Length - keepTail)..];
        return new BoundedStream(headPart, tailPart, total - headPart.Length - tailPart.Length, total);
    }

    private async Task<ShellCommandResponse> BuildRunResponseAsync(
        ShellCommandRequest request, ShellRunResult result, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> outboundReplacements,
        CaptureFile? capture, CancellationToken cancellationToken)
    {
        if (result.TimedOut)
            LogCommandTimedOut(Logger, request.Command, request.TimeoutSeconds);
        else
            LogCommandCompleted(Logger, request.Command, result.ExitCode);

        result = WithoutSplitPaths(result, outboundReplacements);
        var copy = capture is null ? null : await BuildCaptureFileAsync(capture, outboundReplacements, cancellationToken).ConfigureAwait(false);
        var stdout = Bound(
            RewriteOutboundPaths(result.Stdout, outboundReplacements), RewriteOutboundPaths(result.StdoutTail, outboundReplacements), result.StdoutOmitted,
            copy?.StdoutChars);
        var stderr = Bound(
            RewriteOutboundPaths(result.Stderr, outboundReplacements), RewriteOutboundPaths(result.StderrTail, outboundReplacements), result.StderrOmitted,
            copy?.StderrChars);
        var (outputPath, note) = capture is null
            ? ("", "")
            : await KeepWholeOutputAsync(capture, copy, result, outboundReplacements, stdout, stderr, cancellationToken).ConfigureAwait(false);

        return new ShellCommandResponse
        {
            ExitCode = result.TimedOut ? -1 : result.ExitCode,
            Stdout = stdout.Text(note),
            Stderr = stderr.Text(note) + (result.TimedOut ? "\n[Command timed out]" + (result.SessionReset ? SessionResetNote : "") : ""),
            Completed = !result.TimedOut,
            StdoutTruncated = stdout.Truncated,
            StderrTruncated = stderr.Truncated,
            StdoutTotalChars = stdout.Total,
            StderrTotalChars = stderr.Total,
            OutputPath = outputPath,
            AlreadyBounded = stdout.Truncated || stderr.Truncated,
        };
    }

    /// <summary>
    /// The virtual path of the output when a stream was cut, empty otherwise, and what the result says that file
    /// holds. A POSIX session left raw copies the tool built the file from (<see cref="BuildCaptureFileAsync"/>);
    /// when it did not (a Windows session, or a session that could not write), the tool writes what the session
    /// returned. A file left by an output that passed the limit in bytes only is removed; a kept one makes the
    /// oldest go past <see cref="MaxCaptureFiles"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "The whole output is a convenience: a file it cannot keep leaves output_path empty, never fails the command's result.")]
    private async Task<(string Path, string Note)> KeepWholeOutputAsync(
        CaptureFile capture, CaptureCopy? copy, ShellRunResult result, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> outboundReplacements,
        BoundedStream stdout, BoundedStream stderr, CancellationToken cancellationToken)
    {
        try
        {
            if (!stdout.Truncated && !stderr.Truncated)
            {
                if (copy is not null)
                    await _fileSystem.DeleteAsync(capture.VirtualPath, recursive: false, cancellationToken).ConfigureAwait(false);
                return ("", "");
            }

            string note;
            if (copy is { } built)
            {
                note = built.Whole
                    ? "; output_path holds the whole output"
                    : $"; output_path holds the first {CaptureByteCap} bytes of each stream";
            }
            else
            {
                var stderrText = WholeText(result.Stderr, result.StderrTail, result.StderrOmitted);
                var text = WholeText(result.Stdout, result.StdoutTail, result.StdoutOmitted)
                    + (stderrText.Length > 0 ? ShellScript.CaptureStderrHeader + stderrText : "");
                await _fileSystem.WriteAllTextAsync(capture.VirtualPath, RewriteOutboundPaths(text, outboundReplacements), cancellationToken).ConfigureAwait(false);
                note = result.StdoutOmitted > 0 || result.StderrOmitted > 0
                    ? "; output_path holds its first and last parts only, the middle was not captured"
                    : "; output_path holds the whole output";
            }

            await PruneCapturesAsync(capture, cancellationToken).ConfigureAwait(false);
            return (capture.VirtualPath, note);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DiscardCaptureAsync(capture).ConfigureAwait(false);
            return ("", "");
        }
    }

    /// <summary>
    /// Builds the capture file from the raw copies a POSIX session left next to it (<see cref="ShellScript"/>
    /// <c>AppendCapture</c>), through the file system: each stream is streamed, its physical paths virtualized as
    /// the result's are (ADR-008), and its characters counted. The raw copies are removed. Null when the session
    /// left none (no stream passed the limit, a Windows session, a session that could not write).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "The whole output is a convenience: a copy it cannot read leaves output_path empty, never fails the command's result.")]
    private async Task<CaptureCopy?> BuildCaptureFileAsync(
        CaptureFile capture, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> outboundReplacements, CancellationToken cancellationToken)
    {
        var sizesPath = capture.VirtualPath + ShellScript.CaptureSizesSuffix;
        try
        {
            if (await _fileSystem.TryReadAllTextAsync(sizesPath, cancellationToken).ConfigureAwait(false) is not { } sizesText)
                return null;
            var sizes = sizesText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sizes.Length != 2
                || !long.TryParse(sizes[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var stdoutBytes)
                || !long.TryParse(sizes[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var stderrBytes))
                return null;

            long stdoutChars, stderrChars;
            var output = await _fileSystem.OpenWriteStreamAsync(capture.VirtualPath, cancellationToken).ConfigureAwait(false);
            await using (output.ConfigureAwait(false))
            {
                var writer = new StreamWriter(output, s_captureEncoding);
                await using (writer.ConfigureAwait(false))
                {
                    stdoutChars = await CopyRewrittenAsync(capture.VirtualPath + ShellScript.CaptureStdoutSuffix, writer, outboundReplacements, stdoutBytes > CaptureByteCap, cancellationToken).ConfigureAwait(false);
                    stderrChars = 0;
                    if (stderrBytes > 0)
                    {
                        await writer.WriteAsync(ShellScript.CaptureStderrHeader.AsMemory(), cancellationToken).ConfigureAwait(false);
                        stderrChars = await CopyRewrittenAsync(capture.VirtualPath + ShellScript.CaptureStderrSuffix, writer, outboundReplacements, stderrBytes > CaptureByteCap, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Past the cap the file holds nothing more: the bytes left out are counted as they are.
            var stdoutBeyond = Math.Max(0, stdoutBytes - CaptureByteCap);
            var stderrBeyond = Math.Max(0, stderrBytes - CaptureByteCap);
            return new CaptureCopy(stdoutChars + stdoutBeyond, stderrChars + stderrBeyond, stdoutBeyond == 0 && stderrBeyond == 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DiscardCaptureAsync(capture).ConfigureAwait(false);
            return null;
        }
        finally
        {
            await DeleteQuietlyAsync(capture.VirtualPath + ShellScript.CaptureStdoutSuffix).ConfigureAwait(false);
            await DeleteQuietlyAsync(capture.VirtualPath + ShellScript.CaptureStderrSuffix).ConfigureAwait(false);
            await DeleteQuietlyAsync(sizesPath).ConfigureAwait(false);
        }
    }

    private static readonly System.Text.UTF8Encoding s_captureEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The characters a rewrite pass holds back at most while waiting for the end of a line.</summary>
    private const int CaptureLineCarry = 1 << 20;

    /// <summary>
    /// Streams the raw copy at <paramref name="rawPath"/> into <paramref name="writer"/>, a line at a time so no
    /// path is split between two rewrites, and returns the characters written. A line longer than
    /// <see cref="CaptureLineCarry"/> is cut where no physical base straddles the cut (<see cref="SafeCut"/>);
    /// a copy the cap ended (<paramref name="capped"/>) loses the base its last bytes split. A missing copy
    /// writes nothing.
    /// </summary>
    private async Task<long> CopyRewrittenAsync(
        string rawPath, StreamWriter writer, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> outboundReplacements, bool capped,
        CancellationToken cancellationToken)
    {
        if (!await _fileSystem.ExistsAsync(rawPath, cancellationToken).ConfigureAwait(false))
            return 0;
        var input = await _fileSystem.OpenReadStreamAsync(rawPath, cancellationToken).ConfigureAwait(false);
        await using (input.ConfigureAwait(false))
        {
            using var reader = new StreamReader(input, s_captureEncoding, detectEncodingFromByteOrderMarks: false);
            var buffer = new char[64 * 1024];
            var pending = new System.Text.StringBuilder();
            long written = 0;
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                pending.Append(buffer, 0, read);
                var text = pending.ToString();
                var cut = text.LastIndexOf('\n');
                if (cut < 0 && text.Length < CaptureLineCarry)
                    continue;
                var ready = cut >= 0 ? text[..(cut + 1)] : text[..SafeCut(text, outboundReplacements)];
                pending.Clear().Append(text, ready.Length, text.Length - ready.Length);
                written += await WriteRewrittenAsync(writer, ready, outboundReplacements, cancellationToken).ConfigureAwait(false);
            }

            var rest = pending.ToString();
            if (capped)
                rest = rest[..SafeCut(rest, outboundReplacements)];
            if (rest.Length > 0)
                written += await WriteRewrittenAsync(writer, rest, outboundReplacements, cancellationToken).ConfigureAwait(false);
            return written;
        }
    }

    /// <summary>
    /// The longest prefix of <paramref name="text"/> whose end splits no physical base: a base (or the start of
    /// one) that reaches the end of the text, or ends right at it before the character that would close it,
    /// is left after the cut. Never empty unless the text starts with such a base.
    /// </summary>
    internal static int SafeCut(string text, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> replacements)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(replacements);
        var cut = text.Length;
        bool moved;
        do
        {
            moved = false;
            foreach (var (physicalBase, _) in replacements)
            {
                for (var start = Math.Max(0, cut - physicalBase.Length); start < cut; start++)
                {
                    // The text from start to the cut, completed by the rest of the base: a match is a split base.
                    if (!MatchesAt(string.Concat(text.AsSpan(start, cut - start), physicalBase.AsSpan(cut - start)), 0, physicalBase))
                        continue;
                    cut = start;
                    moved = true;
                    break;
                }
            }
        }
        while (moved && cut > 0);

        return cut;
    }

    /// <summary>
    /// <paramref name="result"/> without the pieces of a physical base its session's cut split: the end of
    /// a head and the start of a tail around a dropped middle would each hold half of a path no rewrite
    /// recognises. What is dropped counts with the omitted characters.
    /// </summary>
    private static ShellRunResult WithoutSplitPaths(
        ShellRunResult result, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> replacements)
    {
        if (replacements.Count == 0)
            return result;
        var (stdout, stdoutTail, stdoutOmitted) = WithoutSplitPaths(result.Stdout, result.StdoutTail, result.StdoutOmitted, replacements);
        var (stderr, stderrTail, stderrOmitted) = WithoutSplitPaths(result.Stderr, result.StderrTail, result.StderrOmitted, replacements);
        return result with
        {
            Stdout = stdout, StdoutTail = stdoutTail, StdoutOmitted = stdoutOmitted,
            Stderr = stderr, StderrTail = stderrTail, StderrOmitted = stderrOmitted,
        };
    }

    private static (string Head, string Tail, long Omitted) WithoutSplitPaths(
        string head, string tail, long omitted, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> replacements)
    {
        if (omitted <= 0)
            return (head, tail, omitted);
        var keptHead = head[..SafeCut(head, replacements)];
        // The longest end of a base (two characters or more) the tail starts with.
        var drop = 0;
        foreach (var (physicalBase, _) in replacements)
        {
            for (var from = 1; from <= physicalBase.Length - 2; from++)
            {
                var suffix = physicalBase[from..];
                if (suffix.Length > drop && tail.StartsWith(suffix, StringComparison.Ordinal))
                {
                    drop = suffix.Length;
                    break;
                }
            }
        }

        return (keptHead, tail[drop..], omitted + (head.Length - keptHead.Length) + drop);
    }

    private static async Task<long> WriteRewrittenAsync(
        StreamWriter writer, string text, IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> outboundReplacements, CancellationToken cancellationToken)
    {
        var rewritten = RewriteOutboundPaths(text, outboundReplacements);
        await writer.WriteAsync(rewritten.AsMemory(), cancellationToken).ConfigureAwait(false);
        return rewritten.Length;
    }

    /// <summary>
    /// Removes the oldest capture files of the directory <paramref name="kept"/> lies in, past
    /// <see cref="MaxCaptureFiles"/>: their names sort by creation (<see cref="CaptureTarget"/>). Only the
    /// directory itself is read, and only a file named as the tool names a capture is a candidate, so a
    /// <c>.log</c> of the user's in that directory (the fallback <c>/workspace/shell</c>) or below it stays.
    /// The file just kept is never removed, however old its name: a long run is named before it starts.
    /// </summary>
    private async Task PruneCapturesAsync(CaptureFile kept, CancellationToken cancellationToken)
    {
        var slash = kept.VirtualPath.LastIndexOf('/');
        var directory = slash > 0 ? kept.VirtualPath[..slash] : "/";
        var captures = new List<string>();
        var options = new VirtualEnumerationOptions(Recursive: false);
        await foreach (var entry in _fileSystem.EnumerateFilesAsync(directory, options, cancellationToken).ConfigureAwait(false))
        {
            if (entry.Kind == VirtualEntryKind.File
                && !string.Equals(entry.VirtualPath, kept.VirtualPath, StringComparison.Ordinal)
                && IsCaptureFileName(entry.VirtualPath[(entry.VirtualPath.LastIndexOf('/') + 1)..]))
                captures.Add(entry.VirtualPath);
        }

        captures.Sort(StringComparer.Ordinal);
        foreach (var stale in captures.Take(Math.Max(0, captures.Count - Math.Max(0, MaxCaptureFiles - 1))))
            await DeleteQuietlyAsync(stale).ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="name"/> is a capture file's name as <see cref="CaptureTarget"/> writes it.</summary>
    internal static bool IsCaptureFileName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            name, @"^[0-9]{21}-[0-9a-f]{8}\.log$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Removes the capture file and the raw copies next to it, best effort.</summary>
    private async Task DiscardCaptureAsync(CaptureFile capture)
    {
        foreach (var suffix in s_captureSuffixes)
            await DeleteQuietlyAsync(capture.VirtualPath + suffix).ConfigureAwait(false);
    }

    private static readonly string[] s_captureSuffixes =
        ["", ShellScript.CaptureStdoutSuffix, ShellScript.CaptureStderrSuffix, ShellScript.CaptureSizesSuffix];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Cleanup of a convenience file: what cannot be removed stays, never fails the call.")]
    private async Task DeleteQuietlyAsync(string virtualPath)
    {
        try
        {
            if (await _fileSystem.ExistsAsync(virtualPath, CancellationToken.None).ConfigureAwait(false))
                await _fileSystem.DeleteAsync(virtualPath, recursive: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    // What a session returned of a stream, its dropped middle marked when it dropped one.
    private static string WholeText(string head, string tail, long omitted) =>
        omitted > 0 ? $"{head}\n[... {omitted} chars not captured ...]\n{tail}" : head + tail;

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

        LogConfinementUnavailable(Logger, availability.Reason ?? "");
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

        // A file-system scope (#349) binds its entries alone, each with the rights in force there: a
        // mount no entry selects is not bound at all, so the process cannot reach what the tools refuse.
        if (AgentRunScope.EnforcedFileSystem is { } scope)
        {
            foreach (var area in ScopedAreas(scope, virtualRoot, heldRoot))
            {
                if (paths.Exists(p => string.Equals(p.Destination, area, StringComparison.Ordinal)))
                    continue;
                var areaAccess = AddAgentRootPath(paths, area);
                AddOverridePaths(paths, mount.Overrides, root.ResolvedPath, virtualRoot, area, RightsAt);
                if (areaAccess is { } held && heldRoot is not null && string.Equals(area, heldRoot, StringComparison.Ordinal))
                    await AddGitWorktreePathsAsync(paths, mount.Overrides, heldRoot, virtualRoot, root.ResolvedPath, held, cancellationToken)
                        .ConfigureAwait(false);
            }
            return;
        }

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
            virtualPath => Granted(RightsAt(virtualPath)), Logger, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// The areas of the mount at <paramref name="virtualRoot"/> a file-system scope binds: its entries on
    /// that mount, parents first so a narrower entry is bound over a wider one; clipped to the agent root
    /// the mount holds, when it holds one (an entry inside the root, or the root inside an entry).
    /// </summary>
    private static IEnumerable<string> ScopedAreas(AgentFileSystemScope scope, string virtualRoot, string? heldRoot)
    {
        var areas = scope.Entries.Select(e => e.Path).Where(p => MatchesMountPrefix(p, virtualRoot));
        if (heldRoot is not null)
            areas = areas.Select(p => MatchesMountPrefix(p, heldRoot) ? p : MatchesMountPrefix(heldRoot, p) ? heldRoot : null)
                .OfType<string>();
        return areas.Distinct(StringComparer.Ordinal).OrderBy(p => p.Length);
    }

    /// <summary>Adds the sub-path overrides of a mount that lie under <paramref name="area"/>, at their virtual path.</summary>
    /// <param name="paths">The paths the sandbox binds, appended to.</param>
    /// <param name="overrides">The mount's sub-path overrides.</param>
    /// <param name="physicalRoot">The mount's physical root.</param>
    /// <param name="virtualRoot">The mount's virtual root.</param>
    /// <param name="area">The virtual area bound: the mount root, the agent root or a scope entry.</param>
    /// <param name="rightsAt">
    /// The rights to bind an override with, by virtual path; <see langword="null"/> takes the override's own.
    /// A file-system scope passes the rights the file system resolves there, the scope's narrowing included.
    /// </param>
    private static void AddOverridePaths(
        List<ConfinedPath> paths, IReadOnlyList<SubPathOverride> overrides, string physicalRoot, string virtualRoot, string area,
        Func<string, FileAccessRights>? rightsAt = null)
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
            var granted = Granted(rightsAt is null ? subPath.Rights : rightsAt(virtualPath)) ?? BubblewrapProbe.HiddenKindOf(source);
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
        ArgumentNullException.ThrowIfNull(command);
        // Sought as written and with the quotes removed, the two readings a check could be made on.
        var unquoted = string.Join(' ', TokenizeCommand(command));
        return s_forbiddenOperators.FirstOrDefault(op =>
            command.Contains(op, StringComparison.Ordinal) || unquoted.Contains(op, StringComparison.Ordinal));
    }

    /// <summary>
    /// Null when the first token of <paramref name="command"/>, as <see cref="ParseCommand"/> reads it, is
    /// one plain word: a quoted token holding whitespace (<c>"/x/ls /y/sh" -c id</c>) names a program
    /// whose name no allowlist check can vouch for, so it is refused rather than reduced to a base name.
    /// </summary>
    private static string? ExecutableTokenError(string command)
    {
        var tokens = TokenizeCommand(command);
        return tokens.Count > 0 && tokens[0].Any(char.IsWhiteSpace)
            ? "Invalid executable name: a quoted executable cannot hold whitespace"
            : null;
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
        string[] arguments, FileAccessRights rights, bool windowsTarget = false)
    {
        var mounts = _fileSystem.GetAvailableMounts();
        if (mounts.Count == 0 || arguments.Length == 0)
            return (arguments, null);

        // The Windows spelling of each mount, once per call, not once per argument (#302).
        var windowsBases = windowsTarget ? WindowsBasesOf(mounts) : null;
        var rewritten = new string[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            var (token, denial) = TryRewriteToken(arguments[i], mounts, rights, windowsBases);
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
        string token, IReadOnlyList<MountInfo> mounts, FileAccessRights rights,
        IReadOnlyDictionary<string, (string Root, string WindowsBase)>? windowsBases)
    {
        if (mounts.Any(m => MatchesMountPrefix(token, m.VirtualPath)))
        {
            var result = AgentRootGuard.Check(_fileSystem, token) ?? _fileSystem.ResolveAndValidate(token, rights);
            return result.IsAllowed
                ? (SpellForProcess(token, result.ResolvedPath!, windowsBases), null)
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
                    ? (string.Concat(token.AsSpan(0, eq + 1), SpellForProcess(right, result.ResolvedPath!, windowsBases)), null)
                    : (token, result.DenialReason ?? "access denied");
            }
        }

        return (token, null);
    }

    /// <summary>
    /// The mounts that have a Windows spelling under WSL, by virtual path without its trailing slash:
    /// the mount's physical root and that spelling. Empty outside WSL.
    /// </summary>
    private Dictionary<string, (string Root, string WindowsBase)> WindowsBasesOf(IReadOnlyList<MountInfo> mounts)
    {
        var bases = new Dictionary<string, (string Root, string WindowsBase)>(StringComparer.Ordinal);
        foreach (var virtualPath in mounts.Select(mount => mount.VirtualPath))
        {
            var root = _fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.None).ResolvedPath;
            if (root is not null && WindowsBaseOf(virtualPath, root) is { } windowsBase)
                bases[virtualPath.TrimEnd('/')] = (root, windowsBase);
        }

        return bases;
    }

    /// <summary>
    /// The physical path a checked virtual path is handed to the process as: the resolved path, or, for a
    /// Windows program under WSL, its Windows spelling (<c>/mnt/c/proj/src/a.ts</c> → <c>C:\proj\src\a.ts</c>).
    /// The spelling is built from the resolved path, the one the rights check judged, never from the
    /// virtual text. A mount without a Windows spelling keeps the resolved path.
    /// </summary>
    private static string SpellForProcess(
        string virtualPath, string resolved, IReadOnlyDictionary<string, (string Root, string WindowsBase)>? windowsBases)
    {
        if (windowsBases is null || windowsBases.Count == 0)
            return resolved;

        string? key = null;
        foreach (var candidate in windowsBases.Keys)
        {
            if (MatchesMountPrefix(virtualPath, candidate) && (key is null || candidate.Length > key.Length))
                key = candidate;
        }

        if (key is null || !resolved.StartsWith(windowsBases[key].Root, StringComparison.Ordinal))
            return resolved;

        var (root, windowsBase) = windowsBases[key];
        var rest = resolved[root.Length..].Trim('/');
        return rest.Length == 0
            ? windowsBase
            : windowsBase.TrimEnd('\\', '/') + "\\" + rest.Replace('/', '\\');
    }

    /// <summary>
    /// The Windows spelling of a mount's base under WSL: the one <see cref="WindowsPaths"/> declares for its
    /// virtual path, else that of the Windows drive the base lies on; null outside WSL or off a drive.
    /// </summary>
    private string? WindowsBaseOf(string virtualPath, string physicalBase)
    {
        if (!Wsl.IsWsl)
            return null;

        var key = virtualPath.TrimEnd('/');
        if ((WindowsPaths.TryGetValue(key, out var spelling) || WindowsPaths.TryGetValue(key + "/", out spelling))
            && !string.IsNullOrWhiteSpace(spelling))
            return spelling.Trim();

        return WslInterop.DriveSpelling(physicalBase);
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

            // Under WSL a Windows program prints the Windows spelling of the base (#302). The base is
            // kept without its trailing separator (a drive root C:\ becomes C:), as the match wants
            // the separator after the base, not inside it.
            if (WindowsBaseOf(virtualPath, root.ResolvedPath) is { } windowsBase)
                replacements.Add((windowsBase.TrimEnd('\\', '/') is { Length: >= 2 } trimmed ? trimmed : windowsBase, virtualPrefix));
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
    /// Rewrites the path token starting at <paramref name="index"/> when one of the physical bases
    /// matches there, at the start of a token and on a path boundary, appending the virtual prefix and
    /// the separator-normalized remainder of the token, and advancing <paramref name="index"/> past what
    /// it consumed. Inside a quoted span (<paramref name="quotedUntil"/>, see <see cref="ClosingQuoteOf"/>)
    /// the remainder is scanned for further bases, so <c>"C:\proj\a and C:\proj\b"</c> rewrites both.
    /// Returns false without touching the builder when no base matches.
    /// </summary>
    private static bool TryRewriteTokenAt(
        System.Text.StringBuilder sb,
        string output,
        IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> replacements,
        ref int index,
        int quotedUntil = -1)
    {
        // A base starts a token, never continues a word or a path: PID: is no D: base, and
        // /foo/mnt/c/x holds no /mnt/c mount.
        if (index > 0 && ContinuesPathToken(output[index - 1]))
            return false;

        foreach (var (physicalBase, virtualPrefix) in replacements)
        {
            if (!MatchesAt(output, index, physicalBase))
                continue;

            // A base only matches when what follows closes it, so /data leaves /data2/x alone.
            var end = index + physicalBase.Length;
            if (end < output.Length && !ClosesPathBase(output[end]))
                continue;

            sb.Append(virtualPrefix);
            var limit = quotedUntil >= 0 ? quotedUntil : ClosingQuoteOf(output, index, end);
            index = end;
            CopyTokenRemainder(sb, output, replacements, ref index, limit);
            return true;
        }

        return false;
    }

    /// <summary>Appends the rest of a rewritten token with its separators normalized, rewriting further bases inside a quoted span.</summary>
    private static void CopyTokenRemainder(
        System.Text.StringBuilder sb,
        string output,
        IReadOnlyList<(string PhysicalBase, string VirtualPrefix)> replacements,
        ref int index,
        int limit)
    {
        while (index < output.Length
               && (limit >= 0 ? index < limit && output[index] != ';' : !IsPathDelimiter(output[index])))
        {
            var c = output[index];
            sb.Append(c == '\\' ? '/' : c);
            index++;
            if (limit >= 0 && !ContinuesPathToken(c))
                TryRewriteTokenAt(sb, output, replacements, ref index, limit);
        }
    }

    /// <summary>
    /// Where a path opened by a quote ends (#300): when the base at <paramref name="start"/> follows a
    /// <c>"</c> or a <c>'</c> that starts a token, and the same quote closes it further on the same line,
    /// the index of that quote, so <c>"C:\ici est\un dossier\f.txt"</c> is rewritten whole, spaces
    /// included. -1 otherwise: unquoted, an apostrophe inside a word (<c>isn'C:\…</c>), or a quote never
    /// closed on the line, the token ends at the first delimiter.
    /// </summary>
    private static int ClosingQuoteOf(string output, int start, int end)
    {
        if (start == 0 || output[start - 1] is not ('"' or '\''))
            return -1;
        if (start >= 2 && ContinuesPathToken(output[start - 2]))
            return -1;

        var quote = output[start - 1];
        for (var i = end; i < output.Length; i++)
        {
            if (output[i] == quote)
                return i;
            if (output[i] is '\n' or '\r')
                return -1;
        }

        return -1;
    }

    /// <summary>True when a character continues a word or a path, so that no path token can start right after it.</summary>
    private static bool ContinuesPathToken(char c) => char.IsLetterOrDigit(c) || c is '/' or '\\' or '.' or '_' or '-';

    /// <summary>True when the character ends a matched physical base: a separator or a token delimiter.</summary>
    private static bool ClosesPathBase(char c) => c is '/' or '\\' || IsPathDelimiter(c);

    /// <summary>
    /// Whether <paramref name="value"/>, a physical base, sits at <paramref name="index"/>. A Windows base
    /// (<c>C:\…</c>, <c>\\server\…</c>) matches without case and with <c>/</c> for any <c>\</c> (#301): a tool
    /// re-cases a path (<c>c:\Proj</c>), git for Windows prints <c>C:/proj</c>. A POSIX base matches ordinally.
    /// </summary>
    private static bool MatchesAt(string text, int index, string value)
    {
        if (index + value.Length > text.Length)
            return false;
        if (!IsWindowsSpelling(value))
            return string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

        for (var i = 0; i < value.Length; i++)
        {
            var expected = value[i];
            var actual = text[index + i];
            var same = expected is '\\' or '/'
                ? actual is '\\' or '/'
                : char.ToUpperInvariant(expected) == char.ToUpperInvariant(actual);
            if (!same)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a path is spelt the Windows way: a drive (<c>C:\</c>, <c>C:/</c>, or the bare <c>C:</c> an outbound
    /// base of a drive-root mount is kept as) or a UNC share (<c>\\server</c>). Unlike the absolute-path checks of
    /// <c>WorkingDirectoryResolver</c> and <c>FileSystemMount</c>, which want a separator after the drive, this one
    /// accepts the bare drive.
    /// </summary>
    internal static bool IsWindowsSpelling(string path)
        => (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            && (path.Length == 2 || path[2] is '\\' or '/'))
           || path.StartsWith(@"\\", StringComparison.Ordinal);

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

        // A %G placeholder verifies the signature, which runs a gpg program: neutralised by the tool's
        // configuration, and refused here so the call does not ask for what it cannot show.
        if (tokens.Skip(2).Any(token => token.Contains("%G", StringComparison.Ordinal)))
            return "git format placeholder '%G' is not allowed; it verifies signatures, which runs a program";

        return null;
    }

    /// <summary>
    /// <paramref name="arguments"/> of a validated read-only <c>git</c> call with the tool's own safe
    /// configuration in front and, for a subcommand that diffs, its safe diff options right after it.
    /// </summary>
    private string[] WithGitSafeOptions(string[] arguments)
    {
        if (arguments.Length == 0)
            return arguments;
        var subcommand = arguments[0];
        var diffing = subcommand is "diff" or "show" or "log";
        // An older git refuses the option it does not know; its call is Execute (AccessFor) and runs without it.
        string[] lazyFetch = GitVersion() >= MinimumSafeGitVersion ? [s_gitNoLazyFetch] : [];
        return
        [
            .. lazyFetch, .. s_gitSafeConfiguration, subcommand, .. s_gitSafeSubcommandOptions,
            .. diffing ? s_gitSafeDiffOptions : [], .. arguments[1..],
        ];
    }

    /// <summary>
    /// The arguments that read the names of the filter drivers the configuration defines
    /// (<c>filter.&lt;driver&gt;.clean</c>, <c>.smudge</c>, <c>.process</c>), whichever attributes file
    /// names them: <c>.git/info/attributes</c>, <c>core.attributesFile</c> and <c>.gitattributes</c> alike.
    /// </summary>
    private static readonly string[] s_gitFilterDriverQuery =
        ["--no-pager", "--no-optional-locks", "config", "--null", "--name-only", "--get-regexp", "^filter[.]"];

    /// <summary>
    /// The <c>-c</c> pairs that empty every filter driver <paramref name="names"/> (the NUL-separated output of
    /// <see cref="s_gitFilterDriverQuery"/>) defines, so no clean, smudge or process command runs; null when a
    /// driver's name cannot be written in a <c>-c</c> (it holds <c>=</c>, a newline or a NUL).
    /// </summary>
    internal static string[]? FilterDriverOverrides(string names)
    {
        var drivers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in names.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!name.StartsWith("filter.", StringComparison.Ordinal))
                continue;
            var last = name.LastIndexOf('.');
            if (last <= "filter.".Length)
                continue;
            var driver = name["filter.".Length..last];
            if (driver.Contains('=', StringComparison.Ordinal) || driver.Contains('\n', StringComparison.Ordinal) || driver.Contains('\r', StringComparison.Ordinal))
                return null;
            drivers.Add(driver);
        }

        return [.. drivers.SelectMany(driver => new[]
        {
            "-c", $"filter.{driver}.clean=", "-c", $"filter.{driver}.smudge=", "-c", $"filter.{driver}.process=",
        })];
    }

    /// <summary>The version a <c>git --version</c> banner names, null when it names none.</summary>
    internal static Version? ParseGitVersion(string banner)
    {
        ArgumentNullException.ThrowIfNull(banner);
        var match = System.Text.RegularExpressions.Regex.Match(
            banner, @"git version (\d+)\.(\d+)(?:\.(\d+))?", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success)
            return null;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return new Version(
            int.Parse(match.Groups[1].Value, culture),
            int.Parse(match.Groups[2].Value, culture),
            match.Groups[3].Success ? int.Parse(match.Groups[3].Value, culture) : 0);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A git that cannot be probed is an unknown version: its calls execute, never fail the tool.")]
    private static Version? ProbeHostGitVersion()
    {
        // OUT-OF-SCOPE: system probing of the git binary on the PATH, not a VFS path.
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("--version");
            using var process = System.Diagnostics.Process.Start(start);
            if (process is null)
                return null;
            process.StandardInput.Close();
            var banner = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? ParseGitVersion(banner) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the first option of <see cref="s_forbiddenGitOptions"/> present in
    /// <paramref name="tokens"/>, or null when the command line carries none. A long option
    /// is matched on its name, so <c>--output=&lt;file&gt;</c> and <c>--output &lt;file&gt;</c>
    /// are the same option, and on every abbreviation git could expand to it
    /// (<see cref="MinimumGitOptionAbbreviation"/>); its <c>--no-</c> form is refused when it undoes an
    /// option the tool injects (<see cref="s_forbiddenGitNegatedOptions"/>). A short one is matched on its
    /// prefix, because git lets it carry its value attached (<c>-o/tmp/x</c>). After <c>status</c>,
    /// <c>--verbose</c> and a <c>-v</c> anywhere in a bundle of short flags (<c>-sv</c>) are refused too.
    /// </summary>
    private static string? FindForbiddenGitOption(IReadOnlyList<string> tokens)
    {
        var status = tokens.Count > 1 && string.Equals(tokens[1], "status", StringComparison.Ordinal);
        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Length < 2 || token[0] != '-')
                continue;

            if (token[1] != '-')
            {
                if (status && token.Contains('v', StringComparison.Ordinal))
                    return "-v";
                foreach (var option in s_forbiddenGitOptions)
                {
                    if (!option.StartsWith("--", StringComparison.Ordinal) && token.StartsWith(option, StringComparison.Ordinal))
                        return option;
                }

                continue;
            }

            var equals = token.IndexOf('=', StringComparison.Ordinal);
            var name = equals > 0 ? token[..equals] : token;
            if (s_gitOptionsBesideForbiddenOnes.Contains(name, StringComparer.Ordinal))
                continue;

            var forbidden = MatchLongOption(name, s_forbiddenGitOptions)
                ?? (status ? MatchLongOption(name, s_forbiddenGitStatusOptions) : null);
            if (forbidden is not null)
                return forbidden;

            if (name.StartsWith("--no-", StringComparison.Ordinal)
                && MatchLongOption("--" + name["--no-".Length..], s_forbiddenGitNegatedOptions) is { } negated)
                return "--no-" + negated[2..];
        }

        return null;
    }

    /// <summary>
    /// The long option of <paramref name="options"/> that <paramref name="name"/> names in full or abbreviates,
    /// null when it names none.
    /// </summary>
    private static string? MatchLongOption(string name, string[] options)
    {
        foreach (var option in options)
        {
            if (!option.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (string.Equals(name, option, StringComparison.Ordinal)
                || (name.Length >= 2 + MinimumGitOptionAbbreviation && option.StartsWith(name, StringComparison.Ordinal)))
                return option;
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

    /// <summary>Truncates a string to the configured output limit, telling how many characters were omitted.</summary>
    private string Truncate(string value) =>
        Orkeon.Domain.Tools.ToolResultLimitsOptions.Cut(value, _outputLimit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shell command completed: {Command} (exit code: {ExitCode})")]
    private static partial void LogCommandCompleted(ILogger logger, string command, int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shell command timed out: {Command} (timeout: {TimeoutSeconds}s)")]
    private static partial void LogCommandTimedOut(ILogger logger, string command, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shell command refused: the configured confinement is unavailable ({Reason})")]
    private static partial void LogConfinementUnavailable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SECURITY: shell_command allows interpreters (node/dotnet/npm/find), which run arbitrary code; confinement: {Confinement}.")]
    private static partial void LogInterpretersEnabled(ILogger logger, string confinement);
}
