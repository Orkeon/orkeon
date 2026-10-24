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
internal sealed record ShellFrame(string Stdout, string Stderr, int ExitCode);

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
/// <c>\n&lt;nonce&gt;:RC:&lt;code&gt;\n</c>. The command never writes on the session's output itself,
/// so it cannot interleave with the frame.
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

        builder.Append("printf '\\n%s:OUT\\n' ").Append(quotedNonce)
            .Append("; head -c ").Append(cap).Append(" \"$").Append(StdoutVariable).Append('"')
            .Append("; printf '\\n%s:ERR\\n' ").Append(quotedNonce)
            .Append("; head -c ").Append(cap).Append(" \"$").Append(StderrVariable).Append('"')
            .Append("; printf '\\n%s:RC:%s\\n' ").Append(quotedNonce).Append(" \"$").Append(ExitCodeVariable).Append("\"\n");
        return builder.ToString();
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

        var stdout = buffer[outMarker.Length..errIndex];
        var stderr = buffer[(errIndex + errMarker.Length)..rcIndex];
        frame = new ShellFrame(stdout, stderr, exitCode);
        consumed = lineEnd + 1;
        return ShellFrameStatus.Complete;
    }
}
