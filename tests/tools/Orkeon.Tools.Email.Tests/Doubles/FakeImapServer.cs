using System.Globalization;
using System.Text;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>A folder of the <see cref="FakeImapServer"/> model.</summary>
internal sealed class FakeImapFolder
{
    /// <summary>Creates the folder.</summary>
    public FakeImapFolder(string name, string? specialUse, uint uidValidity)
    {
        Name = name;
        SpecialUse = specialUse;
        UidValidity = uidValidity;
    }

    /// <summary>Full name, '/'-separated.</summary>
    public string Name { get; set; }

    /// <summary>RFC 6154 attribute (<c>\Sent</c>, <c>\Trash</c>…), or null.</summary>
    public string? SpecialUse { get; }

    /// <summary>The folder's UIDVALIDITY.</summary>
    public uint UidValidity { get; }

    /// <summary>The next UID to assign.</summary>
    public uint UidNext { get; set; } = 1;

    /// <summary>Messages, by ascending UID.</summary>
    public List<FakeImapMessage> Messages { get; } = [];
}

/// <summary>A message of the <see cref="FakeImapServer"/> model.</summary>
internal sealed class FakeImapMessage
{
    /// <summary>Creates the message.</summary>
    public FakeImapMessage(uint uid, byte[] raw, DateTimeOffset internalDate, IEnumerable<string> flags)
    {
        Uid = uid;
        Raw = raw;
        InternalDate = internalDate;
        Facts = FakeImapServer.Inspect(raw);
        foreach (var flag in flags)
            Flags.Add(flag);
    }

    /// <summary>The UID.</summary>
    public uint Uid { get; }

    /// <summary>The message bytes, CRLF line endings.</summary>
    public byte[] Raw { get; }

    /// <summary>The INTERNALDATE.</summary>
    public DateTimeOffset InternalDate { get; }

    /// <summary>System flags and keywords.</summary>
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the server derived from the bytes once.</summary>
    public ImapMessageFacts Facts { get; }
}

/// <summary>
/// Scripted IMAP4rev1 server over an in-memory model, implementing what MailKit sends for the
/// e-mail tools: CAPABILITY, LOGIN and AUTHENTICATE (PLAIN, XOAUTH2, with or without SASL-IR),
/// NAMESPACE, LIST (selection and RETURN options, SPECIAL-USE, STATUS), STATUS, SELECT/EXAMINE,
/// CREATE (parents must exist, as on a strict server), RENAME, DELETE, [UID] SEARCH, [UID] FETCH
/// (ENVELOPE and BODYSTRUCTURE generated from the MIME), [UID] STORE, [UID] COPY, [UID] MOVE
/// with COPYUID, [UID] EXPUNGE, APPEND with APPENDUID, NOOP and LOGOUT. Anything else is
/// answered BAD and recorded, so a test fails with the exact command MailKit sent.
/// </summary>
internal sealed partial class FakeImapServer : FakeProtocolServer
{
    /// <summary>The capabilities advertised by default.</summary>
    public const string DefaultCapabilities = "IMAP4rev1 AUTH=PLAIN AUTH=XOAUTH2 SASL-IR UIDPLUS MOVE SPECIAL-USE NAMESPACE PREVIEW";

    private static readonly DateTimeOffset DefaultInternalDate = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly List<FakeImapFolder> _folders = [];
    private readonly List<string> _authentications = [];
    private readonly Dictionary<string, Func<ImapRequest, Task<bool>>> _handlers;
    private uint _nextUidValidity = 1000;

    /// <summary>Creates and starts the server with an INBOX.</summary>
    /// <param name="username">The user name accepted.</param>
    /// <param name="password">The password accepted.</param>
    /// <param name="capabilities">The capabilities advertised.</param>
    public FakeImapServer(string username, string password, string capabilities = DefaultCapabilities)
    {
        Username = username;
        Password = password;
        Capabilities = capabilities;
        _handlers = new Dictionary<string, Func<ImapRequest, Task<bool>>>(StringComparer.Ordinal)
        {
            ["CAPABILITY"] = r => ReplyAsync(r, $"* CAPABILITY {Capabilities}", "OK CAPABILITY completed"),
            ["NOOP"] = r => ReplyAsync(r, null, "OK NOOP completed"),
            ["CHECK"] = r => ReplyAsync(r, null, "OK CHECK completed"),
            ["ID"] = r => ReplyAsync(r, "* ID NIL", "OK ID completed"),
            ["ENABLE"] = r => ReplyAsync(r, "* ENABLED", "OK ENABLE completed"),
            ["LOGOUT"] = LogoutAsync,
            ["LOGIN"] = LoginAsync,
            ["AUTHENTICATE"] = AuthenticateAsync,
            ["NAMESPACE"] = r => Authenticated(r, () => ReplyAsync(r, "* NAMESPACE ((\"\" \"/\")) NIL NIL", "OK NAMESPACE completed")),
            ["LIST"] = r => Authenticated(r, () => ListAsync(r)),
            ["LSUB"] = r => Authenticated(r, () => ReplyAsync(r, null, "OK LSUB completed")),
            ["STATUS"] = r => Authenticated(r, () => StatusAsync(r)),
            ["SELECT"] = r => Authenticated(r, () => SelectAsync(r, readOnly: false)),
            ["EXAMINE"] = r => Authenticated(r, () => SelectAsync(r, readOnly: true)),
            ["CREATE"] = r => Authenticated(r, () => CreateAsync(r)),
            ["DELETE"] = r => Authenticated(r, () => DeleteAsync(r)),
            ["RENAME"] = r => Authenticated(r, () => RenameAsync(r)),
            ["SUBSCRIBE"] = r => Authenticated(r, () => ReplyAsync(r, null, "OK SUBSCRIBE completed")),
            ["UNSUBSCRIBE"] = r => Authenticated(r, () => ReplyAsync(r, null, "OK UNSUBSCRIBE completed")),
            ["APPEND"] = r => Authenticated(r, () => AppendAsync(r)),
            ["CLOSE"] = r => Selected(r, () => CloseAsync(r, expunge: true)),
            ["UNSELECT"] = r => Selected(r, () => CloseAsync(r, expunge: false)),
            ["EXPUNGE"] = r => Selected(r, () => ExpungeAsync(r)),
            ["SEARCH"] = r => Selected(r, () => SearchAsync(r)),
            ["FETCH"] = r => Selected(r, () => FetchAsync(r)),
            ["STORE"] = r => Selected(r, () => StoreAsync(r)),
            ["COPY"] = r => Selected(r, () => CopyOrMoveAsync(r, move: false)),
            ["MOVE"] = r => Selected(r, () => CopyOrMoveAsync(r, move: true)),
        };

        AddFolder("INBOX");
        Start();
    }

    /// <summary>The user name accepted.</summary>
    public string Username { get; }

    /// <summary>The password accepted.</summary>
    public string Password { get; }

    /// <summary>The advertised capabilities.</summary>
    public string Capabilities { get; }

    /// <summary>The bearer token XOAUTH2 accepts, when set.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Commands starting with this text (tag excluded) are never answered.</summary>
    public string? StallOn { get; set; }

    /// <summary>Commands starting with this text (tag excluded) get <c>* BYE</c> and the connection closes.</summary>
    public string? HangUpOn { get; set; }

    /// <summary>How clients signed in: the command or mechanism, one entry per attempt.</summary>
    public IReadOnlyList<string> Authentications
    {
        get
        {
            lock (Gate)
                return [.. _authentications];
        }
    }

    /// <summary>The commands received, without their tags.</summary>
    public IReadOnlyList<string> Commands =>
        Transcript.Select(line => line.IndexOf(' ', StringComparison.Ordinal) is var cut and > 0 ? line[(cut + 1)..] : line).ToList();

    /// <summary>Adds a folder (its parents are not required).</summary>
    public FakeImapServer AddFolder(string name, string? specialUse = null)
    {
        lock (Gate)
            _folders.Add(new FakeImapFolder(name, specialUse, _nextUidValidity++));
        return this;
    }

    /// <summary>Adds the usual special-use folders: Sent, Drafts, Trash, Junk and Archive.</summary>
    public FakeImapServer WithStandardFolders() =>
        AddFolder("Sent", "\\Sent").AddFolder("Drafts", "\\Drafts").AddFolder("Trash", "\\Trash")
            .AddFolder("Junk", "\\Junk").AddFolder("Archive", "\\Archive");

    /// <summary>Adds a message to <paramref name="folder"/>; returns its UID.</summary>
    public uint AddMessage(string folder, string rawMime, bool seen = false, bool flagged = false, DateTimeOffset? internalDate = null)
    {
        var flags = new List<string>();
        if (seen)
            flags.Add("\\Seen");
        if (flagged)
            flags.Add("\\Flagged");

        lock (Gate)
        {
            var target = FindFolder(folder) ?? throw new InvalidOperationException($"No folder '{folder}'.");
            var uid = target.UidNext++;
            target.Messages.Add(new FakeImapMessage(uid, Encoding.UTF8.GetBytes(rawMime.ReplaceLineEndings("\r\n")), internalDate ?? DefaultInternalDate, flags));
            return uid;
        }
    }

    /// <summary>The folder names, sorted.</summary>
    public IReadOnlyList<string> FolderNames
    {
        get
        {
            lock (Gate)
                return _folders.Select(f => f.Name).Order(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>The UIDVALIDITY of <paramref name="folder"/>.</summary>
    public uint UidValidityOf(string folder)
    {
        lock (Gate)
            return (FindFolder(folder) ?? throw new InvalidOperationException($"No folder '{folder}'.")).UidValidity;
    }

    /// <summary>The UIDs in <paramref name="folder"/>, ascending.</summary>
    public IReadOnlyList<uint> UidsOf(string folder)
    {
        lock (Gate)
            return (FindFolder(folder) ?? throw new InvalidOperationException($"No folder '{folder}'.")).Messages.Select(m => m.Uid).ToList();
    }

    /// <summary>The flags of one message.</summary>
    public IReadOnlySet<string> FlagsOf(string folder, uint uid)
    {
        lock (Gate)
        {
            var message = (FindFolder(folder) ?? throw new InvalidOperationException($"No folder '{folder}'.")).Messages.Single(m => m.Uid == uid);
            return new HashSet<string>(message.Flags, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Flags a message \Deleted, as another client would, without expunging it.</summary>
    public void MarkDeleted(string folder, uint uid)
    {
        lock (Gate)
            (FindFolder(folder) ?? throw new InvalidOperationException($"No folder '{folder}'.")).Messages.Single(m => m.Uid == uid).Flags.Add("\\Deleted");
    }

    /// <summary>The subject of one message.</summary>
    public string SubjectOf(string folder, uint uid)
    {
        lock (Gate)
            return (FindFolder(folder) ?? throw new InvalidOperationException($"No folder '{folder}'.")).Messages.Single(m => m.Uid == uid).Facts.Subject;
    }

    /// <summary>The raw bytes of one message, as text.</summary>
    public string RawOf(string folder, uint uid)
    {
        lock (Gate)
            return Encoding.UTF8.GetString((FindFolder(folder) ?? throw new InvalidOperationException($"No folder '{folder}'.")).Messages.Single(m => m.Uid == uid).Raw);
    }

    /// <inheritdoc />
    protected override async Task ServeAsync(MailConnection connection, CancellationToken cancellationToken)
    {
        var session = new ImapSession(connection);
        await connection.WriteLineAsync($"* OK [CAPABILITY {Capabilities}] Fake IMAP ready", cancellationToken);

        while (await ReadCommandAsync(connection, cancellationToken) is { } command)
        {
            Record(command.Display);
            var tokens = Tokenize(command.Text, command.Literals);
            if (tokens.Count < 2)
            {
                RecordUnknown(command.Display);
                await connection.WriteLineAsync("* BAD Empty command", cancellationToken);
                continue;
            }

            var name = tokens[1].Text.ToUpperInvariant();
            var arguments = tokens.Skip(2).ToList();
            var uid = false;
            if (name == "UID" && arguments.Count > 0)
            {
                uid = true;
                name = arguments[0].Text.ToUpperInvariant();
                arguments = arguments.Skip(1).ToList();
            }

            var withoutTag = command.Display[(command.Display.IndexOf(' ', StringComparison.Ordinal) + 1)..];
            if (StallOn is { } stall && withoutTag.StartsWith(stall, StringComparison.Ordinal))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }

            if (HangUpOn is { } hangUp && withoutTag.StartsWith(hangUp, StringComparison.Ordinal))
            {
                await connection.WriteLineAsync("* BYE Server shutting down", cancellationToken);
                return;
            }

            var request = new ImapRequest(session, tokens[0].Text, name, uid, arguments, command.Display, cancellationToken);
            if (!_handlers.TryGetValue(name, out var handler) || (uid && name is not ("SEARCH" or "FETCH" or "STORE" or "COPY" or "MOVE" or "EXPUNGE")))
            {
                RecordUnknown(command.Display);
                await TaggedAsync(request, "BAD Unknown command");
                continue;
            }

            bool keepGoing;
            try
            {
                keepGoing = await handler(request);
            }
            catch (NotSupportedException ex)
            {
                RecordUnknown($"{command.Display} :: {ex.Message}");
                await TaggedAsync(request, "BAD " + ex.Message);
                keepGoing = true;
            }

            if (!keepGoing)
                return;
        }
    }

    private async Task<bool> LogoutAsync(ImapRequest request)
    {
        await request.Session.Connection.WriteLineAsync("* BYE Fake IMAP logging out", request.CancellationToken);
        await TaggedAsync(request, "OK LOGOUT completed");
        return false;
    }

    private async Task<bool> LoginAsync(ImapRequest request)
    {
        lock (Gate)
            _authentications.Add("LOGIN");
        var accepted = request.Arguments.Count == 2 && request.Arguments[0].Text == Username && request.Arguments[1].Text == Password;
        request.Session.Authenticated = accepted;
        await TaggedAsync(request, accepted ? $"OK [CAPABILITY {Capabilities}] LOGIN completed" : "NO [AUTHENTICATIONFAILED] Invalid credentials");
        return true;
    }

    private async Task<bool> AuthenticateAsync(ImapRequest request)
    {
        var mechanism = request.Arguments.Count > 0 ? request.Arguments[0].Text.ToUpperInvariant() : string.Empty;
        lock (Gate)
            _authentications.Add(mechanism);
        if (mechanism is not ("PLAIN" or "XOAUTH2"))
        {
            await TaggedAsync(request, "NO Unsupported authentication mechanism");
            return true;
        }

        string response;
        if (request.Arguments.Count > 1)
        {
            response = request.Arguments[1].Text;
        }
        else
        {
            await request.Session.Connection.WriteLineAsync("+ ", request.CancellationToken);
            response = await request.Session.Connection.ReadLineAsync(request.CancellationToken) ?? "*";
            Record(response);
        }

        if (response == "*")
        {
            await TaggedAsync(request, "BAD Authentication cancelled");
            return true;
        }

        var decoded = Decode(response);
        var accepted = mechanism == "PLAIN"
            ? decoded.Split('\0') is [_, var user, var password] && user == Username && password == Password
            : AccessToken is not null && decoded == $"user={Username}\u0001auth=Bearer {AccessToken}\u0001\u0001";
        request.Session.Authenticated = accepted;
        await TaggedAsync(request, accepted ? $"OK [CAPABILITY {Capabilities}] AUTHENTICATE completed" : "NO [AUTHENTICATIONFAILED] Invalid credentials");
        return true;
    }

    private async Task<bool> ListAsync(ImapRequest request)
    {
        var arguments = request.Arguments;
        var index = 0;
        var selectSpecialUse = false;
        if (index < arguments.Count && arguments[index].Kind == TokenKind.List)
        {
            selectSpecialUse = arguments[index].Items.Any(item => item.Text.Equals("SPECIAL-USE", StringComparison.OrdinalIgnoreCase));
            index++;
        }

        if (arguments.Count < index + 2)
            return await ReplyAsync(request, null, "BAD LIST needs a reference and a pattern");

        var reference = arguments[index++].Text;
        var patternToken = arguments[index++];
        var patterns = patternToken.Kind == TokenKind.List ? patternToken.Items.Select(item => reference + item.Text).ToList() : [reference + patternToken.Text];
        var statusItems = ReturnedStatusItems(arguments, index);

        var lines = new List<string>();
        if (patterns is [""])
        {
            lines.Add("* LIST (\\Noselect) \"/\" \"\"");
        }
        else
        {
            lock (Gate)
            {
                foreach (var folder in _folders.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (!patterns.Exists(pattern => NameMatches(pattern, folder.Name)) || (selectSpecialUse && folder.SpecialUse is null))
                        continue;

                    var attributes = new List<string> { _folders.Exists(f => f.Name.StartsWith(folder.Name + "/", StringComparison.Ordinal)) ? "\\HasChildren" : "\\HasNoChildren" };
                    if (folder.SpecialUse is { } specialUse)
                        attributes.Add(specialUse);
                    lines.Add($"* LIST ({string.Join(' ', attributes)}) \"/\" {Quote(folder.Name)}");
                    if (statusItems is not null)
                        lines.Add(StatusLine(folder, statusItems));
                }
            }
        }

        foreach (var line in lines)
            await request.Session.Connection.WriteLineAsync(line, request.CancellationToken);
        await TaggedAsync(request, "OK LIST completed");
        return true;
    }

    private static List<string>? ReturnedStatusItems(List<Token> arguments, int index)
    {
        if (index + 1 >= arguments.Count || !arguments[index].Text.Equals("RETURN", StringComparison.OrdinalIgnoreCase) || arguments[index + 1].Kind != TokenKind.List)
            return null;

        var options = arguments[index + 1].Items;
        for (var i = 0; i + 1 < options.Count; i++)
        {
            if (options[i].Text.Equals("STATUS", StringComparison.OrdinalIgnoreCase) && options[i + 1].Kind == TokenKind.List)
                return options[i + 1].Items.Select(item => item.Text.ToUpperInvariant()).ToList();
        }

        return null;
    }

    private async Task<bool> StatusAsync(ImapRequest request)
    {
        if (request.Arguments.Count < 2 || request.Arguments[1].Kind != TokenKind.List)
            return await ReplyAsync(request, null, "BAD STATUS needs a mailbox and items");

        string? line;
        lock (Gate)
        {
            var folder = FindFolder(request.Arguments[0].Text);
            line = folder is null ? null : StatusLine(folder, request.Arguments[1].Items.Select(item => item.Text.ToUpperInvariant()).ToList());
        }

        return line is null
            ? await ReplyAsync(request, null, "NO [NONEXISTENT] No such mailbox")
            : await ReplyAsync(request, line, "OK STATUS completed");
    }

    private static string StatusLine(FakeImapFolder folder, List<string> items)
    {
        var values = new List<string>();
        foreach (var item in items)
        {
            var value = item switch
            {
                "MESSAGES" => folder.Messages.Count,
                "UNSEEN" => folder.Messages.Count(m => !m.Flags.Contains("\\Seen")),
                "RECENT" => 0,
                "UIDNEXT" => (long)folder.UidNext,
                "UIDVALIDITY" => (long)folder.UidValidity,
                "SIZE" => folder.Messages.Sum(m => (long)m.Raw.Length),
                _ => -1L,
            };
            if (value >= 0)
                values.Add(string.Create(CultureInfo.InvariantCulture, $"{item} {value}"));
        }

        return $"* STATUS {Quote(folder.Name)} ({string.Join(' ', values)})";
    }

    private async Task<bool> SelectAsync(ImapRequest request, bool readOnly)
    {
        var lines = new List<string>();
        FakeImapFolder? folder;
        lock (Gate)
        {
            folder = request.Arguments.Count > 0 ? FindFolder(request.Arguments[0].Text) : null;
            if (folder is not null)
            {
                lines.Add("* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)");
                lines.Add(readOnly
                    ? "* OK [PERMANENTFLAGS ()] Read-only mailbox"
                    : "* OK [PERMANENTFLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft \\*)] Flags permitted");
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"* {folder.Messages.Count} EXISTS"));
                lines.Add("* 0 RECENT");
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"* OK [UIDVALIDITY {folder.UidValidity}] UIDs valid"));
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"* OK [UIDNEXT {folder.UidNext}] Predicted next UID"));
            }
        }

        request.Session.Selected = folder;
        request.Session.ReadOnly = readOnly;
        if (folder is null)
            return await ReplyAsync(request, null, "NO [NONEXISTENT] No such mailbox");

        foreach (var line in lines)
            await request.Session.Connection.WriteLineAsync(line, request.CancellationToken);
        await TaggedAsync(request, readOnly ? "OK [READ-ONLY] EXAMINE completed" : "OK [READ-WRITE] SELECT completed");
        return true;
    }

    private async Task<bool> CreateAsync(ImapRequest request)
    {
        var name = request.Arguments.Count > 0 ? request.Arguments[0].Text.TrimEnd('/') : string.Empty;
        string answer;
        lock (Gate)
        {
            var cut = name.LastIndexOf('/');
            if (name.Length == 0)
                answer = "BAD CREATE needs a name";
            else if (FindFolder(name) is not null)
                answer = "NO [ALREADYEXISTS] Mailbox already exists";
            else if (cut > 0 && FindFolder(name[..cut]) is null)
                answer = "NO [NONEXISTENT] The parent mailbox does not exist";
            else
            {
                _folders.Add(new FakeImapFolder(name, null, _nextUidValidity++));
                answer = "OK CREATE completed";
            }
        }

        return await ReplyAsync(request, null, answer);
    }

    private async Task<bool> DeleteAsync(ImapRequest request)
    {
        bool removed;
        lock (Gate)
            removed = request.Arguments.Count > 0 && _folders.RemoveAll(f => f.Name == request.Arguments[0].Text) > 0;
        return await ReplyAsync(request, null, removed ? "OK DELETE completed" : "NO [NONEXISTENT] No such mailbox");
    }

    private async Task<bool> RenameAsync(ImapRequest request)
    {
        if (request.Arguments.Count < 2)
            return await ReplyAsync(request, null, "BAD RENAME needs two names");

        var from = request.Arguments[0].Text;
        var to = request.Arguments[1].Text.TrimEnd('/');
        string answer;
        lock (Gate)
        {
            if (FindFolder(from) is not { } source || source.Name.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
                answer = "NO [NONEXISTENT] Cannot rename that mailbox";
            else if (FindFolder(to) is not null)
                answer = "NO [ALREADYEXISTS] Target mailbox exists";
            else
            {
                foreach (var folder in _folders.Where(f => f.Name == source.Name || f.Name.StartsWith(source.Name + "/", StringComparison.Ordinal)).ToList())
                    folder.Name = to + folder.Name[source.Name.Length..];
                answer = "OK RENAME completed";
            }
        }

        return await ReplyAsync(request, null, answer);
    }

    private async Task<bool> AppendAsync(ImapRequest request)
    {
        var arguments = request.Arguments;
        var literal = arguments.LastOrDefault(token => token.Kind == TokenKind.Literal);
        if (arguments.Count < 2 || literal?.Bytes is not { } bytes)
            return await ReplyAsync(request, null, "BAD APPEND needs a mailbox and a message literal");

        var flags = arguments.Count > 2 && arguments[1].Kind == TokenKind.List ? arguments[1].Items.Select(item => item.Text).ToList() : [];
        string answer;
        lock (Gate)
        {
            if (FindFolder(arguments[0].Text) is not { } folder)
            {
                answer = "NO [TRYCREATE] No such mailbox";
            }
            else
            {
                var uid = folder.UidNext++;
                folder.Messages.Add(new FakeImapMessage(uid, bytes, DefaultInternalDate, flags));
                answer = string.Create(CultureInfo.InvariantCulture, $"OK [APPENDUID {folder.UidValidity} {uid}] APPEND completed");
            }
        }

        return await ReplyAsync(request, null, answer);
    }

    private async Task<bool> CloseAsync(ImapRequest request, bool expunge)
    {
        if (expunge && !request.Session.ReadOnly)
        {
            lock (Gate)
                request.Session.Selected!.Messages.RemoveAll(m => m.Flags.Contains("\\Deleted"));
        }

        request.Session.Selected = null;
        return await ReplyAsync(request, null, expunge ? "OK CLOSE completed" : "OK UNSELECT completed");
    }

    private async Task<bool> ExpungeAsync(ImapRequest request)
    {
        if (request.Session.ReadOnly)
            return await ReplyAsync(request, null, "NO [READ-ONLY] Mailbox is read-only");

        List<string> lines;
        lock (Gate)
        {
            var folder = request.Session.Selected!;
            var scope = request.Uid && request.Arguments.Count > 0
                ? ResolveSet(folder, request.Arguments[0].Text, uid: true)
                : folder.Messages;
            lines = RemoveMessages(folder, scope.Where(m => m.Flags.Contains("\\Deleted")).ToList());
        }

        foreach (var line in lines)
            await request.Session.Connection.WriteLineAsync(line, request.CancellationToken);
        await TaggedAsync(request, "OK EXPUNGE completed");
        return true;
    }

    private async Task<bool> StoreAsync(ImapRequest request)
    {
        var arguments = request.Arguments.Where(token => token.Kind != TokenKind.List || token.Items.Count == 0 || !token.Items[0].Text.Equals("UNCHANGEDSINCE", StringComparison.OrdinalIgnoreCase)).ToList();
        if (arguments.Count < 3)
            return await ReplyAsync(request, null, "BAD STORE needs a set, an action and flags");
        if (request.Session.ReadOnly)
            return await ReplyAsync(request, null, "NO [READ-ONLY] Mailbox is read-only");

        var action = arguments[1].Text.ToUpperInvariant();
        var silent = action.EndsWith(".SILENT", StringComparison.Ordinal);
        var flags = arguments[2].Kind == TokenKind.List ? arguments[2].Items.Select(item => item.Text).ToList() : [arguments[2].Text];
        var lines = new List<string>();
        lock (Gate)
        {
            var folder = request.Session.Selected!;
            foreach (var message in ResolveSet(folder, arguments[0].Text, request.Uid))
            {
                if (action.StartsWith("+FLAGS", StringComparison.Ordinal))
                    message.Flags.UnionWith(flags);
                else if (action.StartsWith("-FLAGS", StringComparison.Ordinal))
                    message.Flags.ExceptWith(flags);
                else
                {
                    message.Flags.Clear();
                    message.Flags.UnionWith(flags);
                }

                if (!silent)
                {
                    var sequence = folder.Messages.IndexOf(message) + 1;
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"* {sequence} FETCH (UID {message.Uid} FLAGS ({string.Join(' ', message.Flags)}))"));
                }
            }
        }

        foreach (var line in lines)
            await request.Session.Connection.WriteLineAsync(line, request.CancellationToken);
        await TaggedAsync(request, "OK STORE completed");
        return true;
    }

    private async Task<bool> CopyOrMoveAsync(ImapRequest request, bool move)
    {
        if (request.Arguments.Count < 2)
            return await ReplyAsync(request, null, "BAD COPY/MOVE needs a set and a mailbox");
        if (move && request.Session.ReadOnly)
            return await ReplyAsync(request, null, "NO [READ-ONLY] Mailbox is read-only");

        var lines = new List<string>();
        string answer;
        lock (Gate)
        {
            var source = request.Session.Selected!;
            var messages = ResolveSet(source, request.Arguments[0].Text, request.Uid);
            if (FindFolder(request.Arguments[1].Text) is not { } target)
            {
                answer = "NO [TRYCREATE] No such mailbox";
            }
            else if (messages.Count == 0)
            {
                answer = "OK No messages matched";
            }
            else
            {
                var copies = messages.Select(message => (message.Uid, Copy: new FakeImapMessage(target.UidNext++, message.Raw, message.InternalDate, message.Flags))).ToList();
                target.Messages.AddRange(copies.Select(c => c.Copy));
                var copyUid = string.Create(CultureInfo.InvariantCulture,
                    $"[COPYUID {target.UidValidity} {string.Join(',', copies.Select(c => c.Uid))} {string.Join(',', copies.Select(c => c.Copy.Uid))}]");
                if (move)
                {
                    lines.Add($"* OK {copyUid} Moved");
                    lines.AddRange(RemoveMessages(source, messages));
                    answer = "OK MOVE completed";
                }
                else
                {
                    answer = $"OK {copyUid} COPY completed";
                }
            }
        }

        foreach (var line in lines)
            await request.Session.Connection.WriteLineAsync(line, request.CancellationToken);
        await TaggedAsync(request, answer);
        return true;
    }

    private static List<string> RemoveMessages(FakeImapFolder folder, List<FakeImapMessage> doomed)
    {
        var lines = new List<string>();
        foreach (var message in doomed.OrderBy(m => m.Uid))
        {
            var sequence = folder.Messages.IndexOf(message) + 1;
            if (sequence <= 0)
                continue;
            folder.Messages.RemoveAt(sequence - 1);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"* {sequence} EXPUNGE"));
        }

        return lines;
    }

    private static Task<bool> Authenticated(ImapRequest request, Func<Task<bool>> handler) =>
        request.Session.Authenticated ? handler() : ReplyAsync(request, null, "NO Not authenticated");

    private static Task<bool> Selected(ImapRequest request, Func<Task<bool>> handler) =>
        !request.Session.Authenticated ? ReplyAsync(request, null, "NO Not authenticated")
        : request.Session.Selected is null ? ReplyAsync(request, null, "BAD No mailbox selected")
        : handler();

    private FakeImapFolder? FindFolder(string name) =>
        _folders.Find(f => f.Name == name || (f.Name.Equals("INBOX", StringComparison.OrdinalIgnoreCase) && name.Equals("INBOX", StringComparison.OrdinalIgnoreCase)));

    private static async Task<bool> ReplyAsync(ImapRequest request, string? untagged, string tagged)
    {
        if (untagged is not null)
            await request.Session.Connection.WriteLineAsync(untagged, request.CancellationToken);
        await TaggedAsync(request, tagged);
        return true;
    }

    private static Task TaggedAsync(ImapRequest request, string response) =>
        request.Session.Connection.WriteLineAsync($"{request.Tag} {response}", request.CancellationToken);

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

    /// <summary>The state of one connection.</summary>
    private sealed class ImapSession(MailConnection connection)
    {
        public MailConnection Connection { get; } = connection;

        public bool Authenticated { get; set; }

        public FakeImapFolder? Selected { get; set; }

        public bool ReadOnly { get; set; }
    }

    /// <summary>One command, parsed.</summary>
    private sealed record ImapRequest(
        ImapSession Session, string Tag, string Name, bool Uid, List<Token> Arguments, string Display, CancellationToken CancellationToken);
}
