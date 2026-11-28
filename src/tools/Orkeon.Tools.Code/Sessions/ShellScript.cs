using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>The outcome of reading a framed command's output.</summary>
internal enum ShellFrameStatus
{
    /// <summary>The frame has not been received whole yet: read more.</summary>
    Incomplete,

    /// <summary>The frame is whole and parsed.</summary>
    Complete,

    /// <summary>The stream does not hold the frame expected: the session is out of step.</summary>
    Malformed,
}

/// <summary>What one framed command printed: its output, its error output and its exit code.</summary>
/// <param name="Stdout">The head of the standard output: all of it when it fits the cap.</param>
/// <param name="Stderr">The head of the standard error.</param>
/// <param name="ExitCode">The command's exit code.</param>
/// <param name="StdoutTail">The end of the standard output when it passed the cap, empty otherwise.</param>
/// <param name="StderrTail">The end of the standard error when it passed the cap, empty otherwise.</param>
/// <param name="StdoutOmitted">The bytes of the standard output between its head and its tail, printed by neither.</param>
/// <param name="StderrOmitted">The bytes of the standard error between its head and its tail.</param>
internal sealed record ShellFrame(
    string Stdout,
    string Stderr,
    int ExitCode,
    string StdoutTail = "",
    string StderrTail = "",
    long StdoutOmitted = 0,
    long StderrOmitted = 0);

/// <summary>
/// The protocol of a POSIX shell session (<c>sh -s</c>): what the host writes on the shell's input and
/// how it reads the answer back. Pure: it starts nothing and reads nothing.
/// </summary>
/// <remarks>
/// <para>
/// The host never writes text an agent wrote, except as a word quoted here: each command is a
/// fragment <c>( cd '&lt;cwd&gt;' &amp;&amp; exec '&lt;exe&gt;' '&lt;arg&gt;'... ) &lt;/dev/null
/// &gt;"$o" 2&gt;"$e"</c> whose every word is in POSIX single quotes (<c>'</c> becomes
/// <c>'\''</c>), so the shell expands nothing in it. The command runs in a subshell: it cannot change
/// the session's own variables or directory.
/// </para>
/// <para>
/// Its output goes to two files of the session's private directory (made by <c>mktemp -d</c> when the
/// session starts, removed when it ends); the fragment then prints them, each cut at
/// <see cref="ByteCap"/> bytes, framed by a nonce drawn for that command:
/// <c>\n&lt;nonce&gt;:OUT\n</c> stdout <c>\n&lt;nonce&gt;:ERR\n</c> stderr
/// <c>\n&lt;nonce&gt;:RC:&lt;code&gt;\n</c>. A stream longer than the cap also prints its end, after
/// <c>\n&lt;nonce&gt;:TAIL:&lt;bytes omitted&gt;\n</c>: the head and the tail never overlap, and the
/// count says how many bytes lie between them, so the true length survives the cut. When the invocation
/// names a capture path and either stream passes the tool's limit, the whole output (stdout, then
/// <see cref="CaptureStderrHeader"/> and stderr when there is any) is copied there first. The command
/// never writes on the session's output itself, so it cannot interleave with the frame.
/// </para>
/// </remarks>
internal static class ShellScript
{
    /// <summary>
    /// Bytes of each stream a fragment prints. The tool keeps <see cref="ShellCommandTool.MaxOutputLength"/>
    /// characters; a UTF-8 character takes at most three bytes per UTF-16 character, so a stream cut at
    /// this many bytes still holds more characters than the tool keeps, and the tool marks it truncated.
    /// </summary>
    public const int ByteCap = (ShellCommandTool.MaxOutputLength + 1) * 3;

    /// <summary>The per-stream byte cap for a tool keeping <paramref name="outputLimit"/> characters.</summary>
    public static int ByteCapFor(int outputLimit) => (outputLimit + 1) * 3;

    private const string DirectoryVariable = "_ork_d";
    private const string StdoutVariable = "_ork_o";
    private const string StderrVariable = "_ork_e";
    private const string ExitCodeVariable = "_ork_rc";
    private const string WatchdogVariable = "_ork_w";
    private const string StdoutSizeVariable = "_ork_so";
    private const string StderrSizeVariable = "_ork_se";
    private const string TailSizeVariable = "_ork_t";

    /// <summary>What separates the standard output from the standard error in a capture file.</summary>
    public const string CaptureStderrHeader = "\n[stderr]\n";

    /// <summary>The name the watchdog runs under (its <c>$0</c>), which finds it in a process listing.</summary>
    public const string WatchdogName = "_ork_watch";

    /// <summary>
    /// The watchdog, run as <c>sh -c '&lt;script&gt;' _ork_watch &lt;host pid&gt; &lt;session pid&gt;
    /// &lt;directory&gt;</c>. Every second it checks that the host and the session still exist. When
    /// the host is gone and the session is not, it stops then kills the session and every process
    /// below it (itself excepted), found through <c>/proc</c> (Linux) or <c>ps</c> (macOS) - a
    /// process tree rather than a process group, because the session shares the host's group and
    /// killing that group would reach the host's siblings - and removes the session's directory.
    /// Stopping first keeps a process from forking out of the tree between the listing and the kill.
    /// </summary>
    public const string WatchdogScript = """
        h=$1 s=$2
        while kill -0 "$h" 2>/dev/null && kill -0 "$s" 2>/dev/null; do sleep 1; done
        kill -0 "$s" 2>/dev/null || exit 0
        pairs() {
          if [ -r /proc/self/stat ]; then
            for f in /proc/[0-9]*/stat; do read -r l < "$f" 2>/dev/null || continue; r=${l##*) }; set -- $r; echo "${l%% *} $2"; done
          else
            ps -A -o pid= -o ppid=
          fi
        }
        tree() {
          t=" $s "; c=1
          while [ "$c" = 1 ]; do
            c=0
            p=$(pairs)
            while read -r a b; do
              case "$t" in *" $b "*) case "$t" in *" $a "*) ;; *) if [ "$a" != "$$" ]; then t="$t$a "; c=1; fi ;; esac ;; esac
            done <<EOF
        $p
        EOF
          done
        }
        tree; kill -STOP $t 2>/dev/null; tree; kill -KILL $t 2>/dev/null
        [ -n "$3" ] && rm -rf "$3"
        """;

    /// <summary>
    /// <paramref name="word"/> as one POSIX shell word the shell expands nothing in: between single
    /// quotes, each single quote it holds written <c>'\''</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The word holds a NUL character, which no argument can carry.</exception>
    public static string Quote(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("A shell word cannot hold a NUL character.", nameof(word));

        return "'" + word.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// What the session runs once, when it starts: its private directory, its cleanup when the shell
    /// exits, the watchdog when <paramref name="watchedHostPid"/> is given, then the ready line the
    /// host waits for, which names the directory (a killed shell runs no cleanup: the host removes it).
    /// </summary>
    /// <param name="nonce">The marker's nonce.</param>
    /// <param name="watchedHostPid">
    /// The host process a watchdog watches, killing the session's tree when it disappears: for a
    /// session that nothing else ties to the host's life (unconfined, Seatbelt). Null for none.
    /// </param>
    public static string Preamble(string nonce, int? watchedHostPid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        var builder = new StringBuilder();
        builder.Append(DirectoryVariable).Append("=$(mktemp -d 2>/dev/null) || { echo 'orkeon: mktemp -d failed' >&2; exit 97; }\n");
        builder.Append(StdoutVariable).Append("=\"$").Append(DirectoryVariable).Append("/o\"; ")
            .Append(StderrVariable).Append("=\"$").Append(DirectoryVariable).Append("/e\"\n");
        if (watchedHostPid is { } hostPid)
        {
            builder.Append("/bin/sh -c ").Append(Quote(WatchdogScript)).Append(' ').Append(WatchdogName).Append(' ')
                .Append(hostPid.ToString(CultureInfo.InvariantCulture))
                .Append(" $$ \"$").Append(DirectoryVariable).Append("\" </dev/null >/dev/null 2>&1 &\n");
            builder.Append(WatchdogVariable).Append("=$!\n");
            builder.Append("trap 'kill \"$").Append(WatchdogVariable).Append("\" 2>/dev/null; rm -rf \"$")
                .Append(DirectoryVariable).Append("\"' EXIT\n");
        }
        else
        {
            builder.Append("trap 'rm -rf \"$").Append(DirectoryVariable).Append("\"' EXIT\n");
        }

        builder.Append("printf '\\n%s:READY:%s\\n' ").Append(Quote(nonce)).Append(" \"$").Append(DirectoryVariable).Append("\"\n");
        return builder.ToString();
    }

    /// <summary>The fragment that runs <paramref name="invocation"/> and prints its frame.</summary>
    /// <param name="invocation">The command, validated by the tool.</param>
    /// <param name="nonce">The nonce framing this command's answer.</param>
    /// <param name="byteCap">The bytes of each stream printed back.</param>
    public static string Command(ShellInvocation invocation, string nonce, int byteCap = ByteCap)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.Executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCap);

        var cap = byteCap.ToString(CultureInfo.InvariantCulture);
        var quotedNonce = Quote(nonce);
        var builder = new StringBuilder();

        // The command may have removed the directory: it is made again rather than lose the output.
        builder.Append("[ -d \"$").Append(DirectoryVariable).Append("\" ] || mkdir -p \"$").Append(DirectoryVariable).Append("\"\n");
        builder.Append("( ");
        if (!string.IsNullOrEmpty(invocation.WorkingDirectory))
            builder.Append("cd ").Append(Quote(invocation.WorkingDirectory)).Append(" && ");
        builder.Append("exec ").Append(Quote(invocation.Executable));
        foreach (var argument in invocation.Arguments)
            builder.Append(' ').Append(Quote(argument));
        builder.Append(" ) </dev/null >\"$").Append(StdoutVariable).Append("\" 2>\"$").Append(StderrVariable).Append("\"; ")
            .Append(ExitCodeVariable).Append("=$?\n");

        // The true size of each stream, read before anything is cut (wc may pad it with spaces).
        builder.Append(StdoutSizeVariable).Append("=$(wc -c <\"$").Append(StdoutVariable).Append("\"); ")
            .Append(StdoutSizeVariable).Append("=$((").Append(StdoutSizeVariable).Append("+0)); ")
            .Append(StderrSizeVariable).Append("=$(wc -c <\"$").Append(StderrVariable).Append("\"); ")
            .Append(StderrSizeVariable).Append("=$((").Append(StderrSizeVariable).Append("+0))\n");
        if (!string.IsNullOrEmpty(invocation.CapturePath))
            AppendCapture(builder, invocation.CapturePath, invocation.OutputLimit);

        builder.Append("printf '\\n%s:OUT\\n' ").Append(quotedNonce)
            .Append("; head -c ").Append(cap).Append(" \"$").Append(StdoutVariable).Append('"');
        AppendTail(builder, quotedNonce, cap, StdoutVariable, StdoutSizeVariable);
        builder.Append("; printf '\\n%s:ERR\\n' ").Append(quotedNonce)
            .Append("; head -c ").Append(cap).Append(" \"$").Append(StderrVariable).Append('"');
        AppendTail(builder, quotedNonce, cap, StderrVariable, StderrSizeVariable);
        builder.Append("; printf '\\n%s:RC:%s\\n' ").Append(quotedNonce).Append(" \"$").Append(ExitCodeVariable).Append("\"\n");
        return builder.ToString();
    }

    // A stream longer than the cap prints its last bytes too, never more than the cap and never a byte the head
    // printed, after a marker naming the bytes in between.
    private static void AppendTail(StringBuilder builder, string quotedNonce, string cap, string streamVariable, string sizeVariable)
    {
        builder.Append("; if [ \"$").Append(sizeVariable).Append("\" -gt ").Append(cap).Append(" ]; then ")
            .Append(TailSizeVariable).Append("=$((").Append(sizeVariable).Append(" - ").Append(cap).Append(")); ")
            .Append("[ \"$").Append(TailSizeVariable).Append("\" -gt ").Append(cap).Append(" ] && ")
            .Append(TailSizeVariable).Append('=').Append(cap).Append("; ")
            .Append("printf '\\n%s:TAIL:%s\\n' ").Append(quotedNonce)
            .Append(" $((").Append(sizeVariable).Append(" - ").Append(cap).Append(" - ").Append(TailSizeVariable).Append(")); ")
            .Append("tail -c \"$").Append(TailSizeVariable).Append("\" \"$").Append(streamVariable).Append("\"; fi");
    }

    // The whole output copied to the capture path when a stream passes the limit: a byte count above it is
    // needed for a character count above it, so no cut output goes without its copy.
    private static void AppendCapture(StringBuilder builder, string capturePath, int outputLimit)
    {
        var limit = outputLimit.ToString(CultureInfo.InvariantCulture);
        var slash = capturePath.LastIndexOf('/');
        var directory = slash > 0 ? capturePath[..slash] : "/";
        builder.Append("if [ \"$").Append(StdoutSizeVariable).Append("\" -gt ").Append(limit)
            .Append(" ] || [ \"$").Append(StderrSizeVariable).Append("\" -gt ").Append(limit).Append(" ]; then ")
            .Append("mkdir -p ").Append(Quote(directory)).Append(" 2>/dev/null; ")
            .Append("{ cat \"$").Append(StdoutVariable).Append("\"; if [ -s \"$").Append(StderrVariable).Append("\" ]; then printf '\\n[stderr]\\n'; cat \"$")
            .Append(StderrVariable).Append("\"; fi; } 2>/dev/null >").Append(Quote(capturePath)).Append("; fi\n");
    }

    /// <summary>The start of the ready marker the preamble prints; the session's private directory follows, then a newline.</summary>
    public static string ReadyMarker(string nonce) => $"\n{nonce}:READY:";

    /// <summary>
    /// Finds the whole ready line in <paramref name="buffer"/>: <paramref name="consumed"/> is then the
    /// length up to its end and <paramref name="directory"/> the session's private directory.
    /// </summary>
    public static bool TryFindReady(string buffer, string nonce, out int consumed, out string directory)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var marker = ReadyMarker(nonce);
        consumed = 0;
        directory = "";
        var index = buffer.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return false;
        var start = index + marker.Length;
        var end = buffer.IndexOf('\n', start);
        if (end < 0)
            return false;
        directory = buffer[start..end];
        consumed = end + 1;
        return true;
    }

    /// <summary>
    /// Reads the frame of the command framed by <paramref name="nonce"/> from the start of
    /// <paramref name="buffer"/>. Complete when the buffer holds the whole frame, the exit code line
    /// included: <paramref name="consumed"/> is then its length and <paramref name="frame"/> its content.
    /// Malformed when the buffer cannot be the start of that frame.
    /// </summary>
    public static ShellFrameStatus ParseFrame(string buffer, string nonce, out ShellFrame? frame, out int consumed)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        frame = null;
        consumed = 0;

        var outMarker = $"\n{nonce}:OUT\n";
        if (buffer.Length < outMarker.Length)
            return outMarker.StartsWith(buffer, StringComparison.Ordinal) ? ShellFrameStatus.Incomplete : ShellFrameStatus.Malformed;
        if (!buffer.StartsWith(outMarker, StringComparison.Ordinal))
            return ShellFrameStatus.Malformed;

        // The exit code line is the last thing the fragment prints: the frame is whole once the
        // buffer holds it, and what the streams printed lies before it.
        var rcMarker = $"\n{nonce}:RC:";
        var rcIndex = buffer.IndexOf(rcMarker, outMarker.Length, StringComparison.Ordinal);
        if (rcIndex < 0)
            return ShellFrameStatus.Incomplete;
        var codeStart = rcIndex + rcMarker.Length;
        var lineEnd = buffer.IndexOf('\n', codeStart);
        if (lineEnd < 0)
            return ShellFrameStatus.Incomplete;
        if (!int.TryParse(buffer.AsSpan(codeStart, lineEnd - codeStart), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exitCode))
            return ShellFrameStatus.Malformed;

        var errMarker = $"\n{nonce}:ERR\n";
        var errIndex = buffer.AsSpan(0, rcIndex).LastIndexOf(errMarker.AsSpan(), StringComparison.Ordinal);
        if (errIndex < outMarker.Length)
            return ShellFrameStatus.Malformed;

        var tailMarker = $"\n{nonce}:TAIL:";
        if (!TrySplitTail(buffer[outMarker.Length..errIndex], tailMarker, out var stdout, out var stdoutTail, out var stdoutOmitted)
            || !TrySplitTail(buffer[(errIndex + errMarker.Length)..rcIndex], tailMarker, out var stderr, out var stderrTail, out var stderrOmitted))
            return ShellFrameStatus.Malformed;
        frame = new ShellFrame(stdout, stderr, exitCode, stdoutTail, stderrTail, stdoutOmitted, stderrOmitted);
        consumed = lineEnd + 1;
        return ShellFrameStatus.Complete;
    }

    // A stream's head, and its tail and the bytes between them when the fragment printed a tail marker.
    private static bool TrySplitTail(string segment, string tailMarker, out string head, out string tail, out long omitted)
    {
        head = segment;
        tail = "";
        omitted = 0;
        var index = segment.IndexOf(tailMarker, StringComparison.Ordinal);
        if (index < 0)
            return true;
        var countStart = index + tailMarker.Length;
        var lineEnd = segment.IndexOf('\n', countStart);
        if (lineEnd < 0
            || !long.TryParse(segment.AsSpan(countStart, lineEnd - countStart), NumberStyles.None, CultureInfo.InvariantCulture, out omitted))
            return false;
        head = segment[..index];
        tail = segment[(lineEnd + 1)..];
        return true;
    }
}
