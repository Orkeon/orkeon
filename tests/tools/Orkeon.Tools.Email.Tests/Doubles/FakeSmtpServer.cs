using System.Text;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>One message a <see cref="FakeSmtpServer"/> accepted.</summary>
/// <param name="MailFrom">The <c>MAIL FROM</c> address.</param>
/// <param name="RcptTo">The <c>RCPT TO</c> addresses, in order.</param>
/// <param name="Data">The message as transmitted, dot-unstuffed.</param>
internal sealed record SmtpEnvelope(string MailFrom, IReadOnlyList<string> RcptTo, string Data);

/// <summary>
/// Scripted SMTP submission server: EHLO (with <c>SIZE</c> when set and
/// <c>AUTH PLAIN LOGIN XOAUTH2</c>), AUTH, MAIL FROM, RCPT TO, DATA up to the lone dot, RSET,
/// NOOP and QUIT. It records every accepted envelope and refuses the recipients a test lists.
/// </summary>
internal sealed class FakeSmtpServer : FakeProtocolServer
{
    private readonly List<SmtpEnvelope> _accepted = [];
    private readonly List<string> _authentications = [];

    /// <summary>Creates and starts the server.</summary>
    /// <param name="username">The user name it accepts.</param>
    /// <param name="password">The password it accepts.</param>
    /// <param name="maxSize">The <c>SIZE</c> it advertises, or null for none.</param>
    public FakeSmtpServer(string username, string password, long? maxSize = null)
    {
        Username = username;
        Password = password;
        MaxSize = maxSize;
        Start();
    }

    /// <summary>The user name accepted.</summary>
    public string Username { get; }

    /// <summary>The password accepted.</summary>
    public string Password { get; }

    /// <summary>The advertised size limit.</summary>
    public long? MaxSize { get; }

    /// <summary>The bearer token XOAUTH2 accepts, when set.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Recipients answered with 550.</summary>
    public HashSet<string> RejectedRecipients { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The messages accepted.</summary>
    public IReadOnlyList<SmtpEnvelope> Accepted
    {
        get
        {
            lock (Gate)
                return [.. _accepted];
        }
    }

    /// <summary>The mechanisms clients authenticated (or tried to) with.</summary>
    public IReadOnlyList<string> Authentications
    {
        get
        {
            lock (Gate)
                return [.. _authentications];
        }
    }

    /// <inheritdoc />
    protected override async Task ServeAsync(MailConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteLineAsync("220 fake.smtp.test ESMTP ready", cancellationToken);
        string? mailFrom = null;
        var rcptTo = new List<string>();
        var authenticated = false;

        while (await connection.ReadLineAsync(cancellationToken) is { } line)
        {
            Record(line);
            var verb = (line.Length >= 4 ? line[..4] : line).ToUpperInvariant();
            switch (verb)
            {
                case "EHLO":
                    var greeting = new List<string> { "fake.smtp.test greets you" };
                    if (MaxSize is { } size)
                        greeting.Add($"SIZE {size}");
                    greeting.Add("AUTH PLAIN LOGIN XOAUTH2");
                    greeting.Add("ENHANCEDSTATUSCODES");
                    for (var i = 0; i < greeting.Count; i++)
                        await connection.WriteLineAsync($"250{(i == greeting.Count - 1 ? ' ' : '-')}{greeting[i]}", cancellationToken);
                    break;

                case "HELO":
                    await connection.WriteLineAsync("250 fake.smtp.test", cancellationToken);
                    break;

                case "AUTH":
                    authenticated = await AuthenticateAsync(connection, line, cancellationToken);
                    break;

                case "MAIL":
                    if (!authenticated)
                    {
                        await connection.WriteLineAsync("530 5.7.0 Authentication required", cancellationToken);
                        break;
                    }

                    mailFrom = Address(line);
                    rcptTo.Clear();
                    await connection.WriteLineAsync("250 2.1.0 Sender OK", cancellationToken);
                    break;

                case "RCPT":
                    var recipient = Address(line);
                    if (RejectedRecipients.Contains(recipient))
                    {
                        await connection.WriteLineAsync($"550 5.1.1 <{recipient}>: Recipient address rejected", cancellationToken);
                        break;
                    }

                    rcptTo.Add(recipient);
                    await connection.WriteLineAsync("250 2.1.5 Recipient OK", cancellationToken);
                    break;

                case "DATA":
                    if (mailFrom is null || rcptTo.Count == 0)
                    {
                        await connection.WriteLineAsync("503 5.5.1 Bad sequence of commands", cancellationToken);
                        break;
                    }

                    await connection.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>", cancellationToken);
                    var data = await ReadDataAsync(connection, cancellationToken);
                    lock (Gate)
                        _accepted.Add(new SmtpEnvelope(mailFrom, [.. rcptTo], data));
                    mailFrom = null;
                    rcptTo.Clear();
                    await connection.WriteLineAsync("250 2.0.0 Ok: queued as FAKE42", cancellationToken);
                    break;

                case "RSET":
                    mailFrom = null;
                    rcptTo.Clear();
                    await connection.WriteLineAsync("250 2.0.0 Reset", cancellationToken);
                    break;

                case "NOOP":
                    await connection.WriteLineAsync("250 2.0.0 OK", cancellationToken);
                    break;

                case "QUIT":
                    await connection.WriteLineAsync("221 2.0.0 Bye", cancellationToken);
                    return;

                default:
                    RecordUnknown(line);
                    await connection.WriteLineAsync("502 5.5.2 Command not implemented", cancellationToken);
                    break;
            }
        }
    }

    private async Task<bool> AuthenticateAsync(MailConnection connection, string line, CancellationToken cancellationToken)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var mechanism = parts.Length > 1 ? parts[1].ToUpperInvariant() : string.Empty;
        lock (Gate)
            _authentications.Add(mechanism);

        bool accepted;
        switch (mechanism)
        {
            case "PLAIN":
            {
                var response = parts.Length > 2 ? parts[2] : await ChallengeAsync(connection, string.Empty, cancellationToken);
                var fields = Decode(response).Split('\0');
                accepted = fields.Length == 3 && fields[1] == Username && fields[2] == Password;
                break;
            }

            case "LOGIN":
            {
                var user = parts.Length > 2
                    ? Decode(parts[2])
                    : Decode(await ChallengeAsync(connection, "VXNlcm5hbWU6", cancellationToken));
                var password = Decode(await ChallengeAsync(connection, "UGFzc3dvcmQ6", cancellationToken));
                accepted = user == Username && password == Password;
                break;
            }

            case "XOAUTH2":
            {
                var response = parts.Length > 2 ? parts[2] : await ChallengeAsync(connection, string.Empty, cancellationToken);
                accepted = AccessToken is not null && Decode(response) == $"user={Username}\u0001auth=Bearer {AccessToken}\u0001\u0001";
                break;
            }

            default:
                await connection.WriteLineAsync("504 5.5.4 Unrecognized authentication type", cancellationToken);
                return false;
        }

        await connection.WriteLineAsync(accepted ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication credentials invalid", cancellationToken);
        return accepted;
    }

    private async Task<string> ChallengeAsync(MailConnection connection, string challenge, CancellationToken cancellationToken)
    {
        await connection.WriteLineAsync($"334 {challenge}", cancellationToken);
        var answer = await connection.ReadLineAsync(cancellationToken) ?? string.Empty;
        Record(answer);
        return answer;
    }

    private static async Task<string> ReadDataAsync(MailConnection connection, CancellationToken cancellationToken)
    {
        var data = new StringBuilder();
        while (await connection.ReadLineBytesAsync(cancellationToken) is { } raw)
        {
            var line = Encoding.UTF8.GetString(raw);
            if (line == ".")
                break;
            data.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
        }

        return data.ToString();
    }

    private static string Address(string line)
    {
        var open = line.IndexOf('<', StringComparison.Ordinal);
        var close = line.IndexOf('>', StringComparison.Ordinal);
        return open >= 0 && close > open ? line[(open + 1)..close] : string.Empty;
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
