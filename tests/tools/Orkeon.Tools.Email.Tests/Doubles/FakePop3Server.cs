using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// Scripted POP3 server over an in-memory maildrop: CAPA, USER/PASS and AUTH PLAIN, STAT, LIST,
/// UIDL, TOP, RETR, DELE, RSET, NOOP and QUIT. Message numbers are fixed for a session and, as
/// RFC 1939 wants, DELE only marks: the deletions are applied at QUIT, and a session that drops
/// without QUIT deletes nothing.
/// </summary>
internal sealed class FakePop3Server : FakeProtocolServer
{
    private readonly List<(string Uid, byte[] Raw)> _maildrop = [];

    /// <summary>Creates and starts the server.</summary>
    /// <param name="username">The user name it accepts.</param>
    /// <param name="password">The password it accepts.</param>
    /// <param name="advertiseSasl">Whether CAPA advertises <c>SASL PLAIN</c> (else only USER/PASS).</param>
    public FakePop3Server(string username, string password, bool advertiseSasl = false)
    {
        Username = username;
        Password = password;
        AdvertiseSasl = advertiseSasl;
        Start();
    }

    /// <summary>The user name accepted.</summary>
    public string Username { get; }

    /// <summary>The password accepted.</summary>
    public string Password { get; }

    /// <summary>Whether SASL is advertised.</summary>
    public bool AdvertiseSasl { get; }

    /// <summary>The unique ids of the messages still in the maildrop, oldest first.</summary>
    public IReadOnlyList<string> Uids
    {
        get
        {
            lock (Gate)
                return _maildrop.Select(message => message.Uid).ToList();
        }
    }

    /// <summary>Adds a message at the end of the maildrop (the newest).</summary>
    public FakePop3Server Add(string uid, string rawMime)
    {
        lock (Gate)
            _maildrop.Add((uid, Encoding.UTF8.GetBytes(rawMime.ReplaceLineEndings("\r\n"))));
        return this;
    }

    /// <inheritdoc />
    protected override async Task ServeAsync(MailConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteLineAsync("+OK fake POP3 server ready", cancellationToken);
        List<(string Uid, byte[] Raw)> session;
        lock (Gate)
            session = [.. _maildrop];
        var deleted = new HashSet<int>();
        var authenticated = false;
        string? user = null;

        while (await connection.ReadLineAsync(cancellationToken) is { } line)
        {
            Record(line);
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var verb = parts.Length == 0 ? string.Empty : parts[0].ToUpperInvariant();
            switch (verb)
            {
                case "CAPA":
                    await connection.WriteLineAsync("+OK Capability list follows", cancellationToken);
                    foreach (var capability in Capabilities())
                        await connection.WriteLineAsync(capability, cancellationToken);
                    await connection.WriteLineAsync(".", cancellationToken);
                    break;

                case "USER":
                    user = parts.Length > 1 ? parts[1] : null;
                    await connection.WriteLineAsync("+OK send PASS", cancellationToken);
                    break;

                case "PASS":
                    authenticated = user == Username && line.Length > 5 && line[5..] == Password;
                    await connection.WriteLineAsync(authenticated ? "+OK maildrop locked and ready" : "-ERR [AUTH] invalid user name or password", cancellationToken);
                    break;

                case "AUTH" when parts.Length > 1 && parts[1].Equals("PLAIN", StringComparison.OrdinalIgnoreCase):
                    var response = parts.Length > 2 ? parts[2] : await ChallengeAsync(connection, cancellationToken);
                    var fields = Decode(response).Split('\0');
                    authenticated = fields.Length == 3 && fields[1] == Username && fields[2] == Password;
                    await connection.WriteLineAsync(authenticated ? "+OK authenticated" : "-ERR [AUTH] authentication failed", cancellationToken);
                    break;

                case "QUIT":
                    if (authenticated)
                    {
                        lock (Gate)
                        {
                            foreach (var index in deleted.OrderDescending())
                                _maildrop.RemoveAll(message => message.Uid == session[index].Uid);
                        }
                    }

                    await connection.WriteLineAsync("+OK bye", cancellationToken);
                    return;

                case "NOOP":
                    await connection.WriteLineAsync("+OK", cancellationToken);
                    break;

                case "STAT" or "LIST" or "UIDL" or "TOP" or "RETR" or "DELE" or "RSET" when !authenticated:
                    await connection.WriteLineAsync("-ERR not authenticated", cancellationToken);
                    break;

                case "STAT":
                    var live = Enumerable.Range(0, session.Count).Where(i => !deleted.Contains(i)).ToList();
                    await connection.WriteLineAsync(
                        string.Create(CultureInfo.InvariantCulture, $"+OK {live.Count} {live.Sum(i => session[i].Raw.Length)}"), cancellationToken);
                    break;

                case "LIST" or "UIDL":
                    await ListAsync(connection, verb, parts, session, deleted, cancellationToken);
                    break;

                case "TOP" or "RETR":
                    await RetrieveAsync(connection, verb, parts, session, deleted, cancellationToken);
                    break;

                case "DELE":
                    if (TryIndex(parts, session, deleted, out var target))
                    {
                        deleted.Add(target);
                        await connection.WriteLineAsync("+OK message deleted", cancellationToken);
                    }
                    else
                    {
                        await connection.WriteLineAsync("-ERR no such message", cancellationToken);
                    }

                    break;

                case "RSET":
                    deleted.Clear();
                    await connection.WriteLineAsync("+OK", cancellationToken);
                    break;

                default:
                    RecordUnknown(line);
                    await connection.WriteLineAsync("-ERR unknown command", cancellationToken);
                    break;
            }
        }
    }

    private IEnumerable<string> Capabilities()
    {
        yield return "USER";
        yield return "UIDL";
        yield return "TOP";
        yield return "PIPELINING";
        if (AdvertiseSasl)
            yield return "SASL PLAIN";
    }

    private static async Task ListAsync(
        MailConnection connection, string verb, string[] parts, List<(string Uid, byte[] Raw)> session, HashSet<int> deleted, CancellationToken cancellationToken)
    {
        string Line(int index) => verb == "UIDL"
            ? string.Create(CultureInfo.InvariantCulture, $"{index + 1} {session[index].Uid}")
            : string.Create(CultureInfo.InvariantCulture, $"{index + 1} {session[index].Raw.Length}");

        if (parts.Length > 1)
        {
            await connection.WriteLineAsync(
                TryIndex(parts, session, deleted, out var index) ? "+OK " + Line(index) : "-ERR no such message", cancellationToken);
            return;
        }

        await connection.WriteLineAsync("+OK", cancellationToken);
        for (var index = 0; index < session.Count; index++)
        {
            if (!deleted.Contains(index))
                await connection.WriteLineAsync(Line(index), cancellationToken);
        }

        await connection.WriteLineAsync(".", cancellationToken);
    }

    private static async Task RetrieveAsync(
        MailConnection connection, string verb, string[] parts, List<(string Uid, byte[] Raw)> session, HashSet<int> deleted, CancellationToken cancellationToken)
    {
        if (!TryIndex(parts, session, deleted, out var index))
        {
            await connection.WriteLineAsync("-ERR no such message", cancellationToken);
            return;
        }

        var text = Encoding.UTF8.GetString(session[index].Raw);
        if (verb == "TOP")
        {
            var bodyLines = parts.Length > 2 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var n) ? n : 0;
            var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var headers = split < 0 ? text : text[..(split + 2)];
            var body = split < 0 ? string.Empty : text[(split + 4)..];
            text = headers + "\r\n" + string.Join("\r\n", body.Split("\r\n").Take(bodyLines));
        }

        var payload = new StringBuilder("+OK message follows\r\n");
        foreach (var line in text.TrimEnd('\r', '\n').Split("\r\n"))
            payload.Append(line.StartsWith('.') ? "." + line : line).Append("\r\n");
        payload.Append(".\r\n");
        await connection.WriteAsync(Encoding.UTF8.GetBytes(payload.ToString()), cancellationToken);
    }

    private static bool TryIndex(string[] parts, List<(string Uid, byte[] Raw)> session, HashSet<int> deleted, out int index)
    {
        index = parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var number) ? number - 1 : -1;
        return index >= 0 && index < session.Count && !deleted.Contains(index);
    }

    private async Task<string> ChallengeAsync(MailConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteLineAsync("+ ", cancellationToken);
        var answer = await connection.ReadLineAsync(cancellationToken) ?? string.Empty;
        Record(answer);
        return answer;
    }

    private static string Decode(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
