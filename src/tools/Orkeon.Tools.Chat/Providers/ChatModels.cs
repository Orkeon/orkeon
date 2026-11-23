namespace Orkeon.Tools.Chat.Providers;

/// <summary>A person of the directory.</summary>
/// <param name="Id">Directory id.</param>
/// <param name="Name">Display name, written by a person: untrusted text.</param>
/// <param name="UserPrincipalName">The sign-in name in the account's tenant.</param>
/// <param name="Mail">The mail address the directory holds.</param>
/// <param name="UserType"><c>Member</c> or <c>Guest</c>; null when the directory does not say.</param>
/// <param name="CreationType">How the person came into the directory (<c>Invitation</c>…); null for an account of the tenant.</param>
internal sealed record ChatPerson(string Id, string Name, string? UserPrincipalName, string? Mail, string? UserType = null, string? CreationType = null)
{
    private const string GuestMarker = "#EXT#";
    private const string Member = "Member";
    private const string Guest = "Guest";

    /// <summary>How the directory says it created a person who signs in elsewhere.</summary>
    private static readonly string[] ExternalCreations = ["Invitation", "SelfServiceSignUp"];

    /// <summary>Whether the directory says the person is a guest, or that the home is another tenant.</summary>
    public bool IsGuest => HasMarker || string.Equals(UserType, Guest, StringComparison.OrdinalIgnoreCase);

    private bool HasMarker => UserPrincipalName?.Contains(GuestMarker, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Whether the directory says the account is the tenant's own. A guest's sign-in name may carry no
    /// marker, so its absence proves nothing: what the directory does not say is not assumed.
    /// </summary>
    private bool IsOwnMember =>
        !HasMarker
        && string.Equals(UserType, Member, StringComparison.OrdinalIgnoreCase)
        && !ExternalCreations.Contains(CreationType, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The address of the person's home tenant. A guest's sign-in name is
    /// <c>name_home.org#EXT#@inviting.tenant</c>: the home address is before the marker, with its
    /// last underscore for the at sign. What follows the marker is the inviting tenant's domain, and
    /// a guest's <see cref="Mail"/> proves nothing about their home: neither is ever used. Without the
    /// marker the sign-in name is the home address of the tenant's own members only; null when the home
    /// cannot be established.
    /// </summary>
    public string? HomeAddress
    {
        get
        {
            if (UserPrincipalName is not { Length: > 0 } name)
                return null;
            var marker = name.IndexOf(GuestMarker, StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                return IsOwnMember ? name : null;
            var encoded = name[..marker];
            var at = encoded.LastIndexOf('_');
            return at <= 0 || at == encoded.Length - 1 ? null : $"{encoded[..at]}@{encoded[(at + 1)..]}";
        }
    }

    /// <summary>The address to show: the home one, else the sign-in name without the marker, else the directory's.</summary>
    public string? Address => HomeAddress ?? (HasMarker ? null : UserPrincipalName) ?? Mail;

    /// <summary>Whether <paramref name="address"/> is the person's: the home address, the sign-in name, and for the tenant's own member the mail too.</summary>
    public bool HasAddress(string address) =>
        string.Equals(HomeAddress, address, StringComparison.OrdinalIgnoreCase)
        || string.Equals(UserPrincipalName, address, StringComparison.OrdinalIgnoreCase)
        || (IsOwnMember && UserPrincipalName is not null && string.Equals(Mail, address, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the person's home is <paramref name="domain"/>.</summary>
    public bool IsOfDomain(string domain) =>
        EndsWithDomain(HomeAddress, domain) || (IsOwnMember && UserPrincipalName is not null && EndsWithDomain(Mail, domain));

    private static bool EndsWithDomain(string? address, string domain) =>
        address is not null
        && address.LastIndexOf('@') is > 0 and var at
        && string.Equals(address[(at + 1)..], domain, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A team the account has joined.</summary>
internal sealed record ChatTeam(string Id, string Name);

/// <summary>A channel of a team.</summary>
/// <param name="Id">Channel id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Membership"><c>standard</c>, <c>private</c> or <c>shared</c>.</param>
internal sealed record ChatChannel(string Id, string Name, string? Membership);

/// <summary>A member of a place, as the place lists them.</summary>
internal sealed record ChatMember(string? UserId, string Name, string? Email, IReadOnlyList<string> Roles);

/// <summary>A chat of the account.</summary>
/// <param name="Id">Chat id.</param>
/// <param name="Kind"><c>oneOnOne</c>, <c>group</c> or <c>meeting</c>.</param>
/// <param name="Topic">Topic, when it has one: untrusted text.</param>
/// <param name="Members">Its members (the first 25 in a listing).</param>
internal sealed record ChatConversation(string Id, string Kind, string? Topic, IReadOnlyList<ChatMember> Members)
{
    /// <summary>The kind of a chat between two people.</summary>
    public const string OneOnOne = "oneOnOne";

    /// <summary>Whether it is between two people.</summary>
    public bool IsOneOnOne => Kind == OneOnOne;
}

/// <summary>Who wrote a message.</summary>
/// <param name="Id">User or application id.</param>
/// <param name="Name">Display name: untrusted text.</param>
/// <param name="IsApplication">Whether an application wrote it.</param>
/// <param name="IsExternal">Whether the service says the author is outside the tenant.</param>
internal sealed record ChatAuthor(string? Id, string? Name, bool IsApplication, bool IsExternal);

/// <summary>A person a message mentions.</summary>
internal sealed record ChatMention(string? Id, string Name);

/// <summary>A message, its body rendered to text.</summary>
internal sealed record ChatMessage
{
    /// <summary>Message id.</summary>
    public required string Id { get; init; }

    /// <summary>Who wrote it.</summary>
    public ChatAuthor? Author { get; init; }

    /// <summary>When it was posted.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>When it was last edited.</summary>
    public DateTimeOffset? Edited { get; init; }

    /// <summary>Its text: untrusted.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Whether the body holds images.</summary>
    public bool HasImages { get; init; }

    /// <summary>Whether the body hid text from a reader.</summary>
    public bool HadHiddenText { get; init; }

    /// <summary>Whether it is a person's or an application's message, not an event of the service.</summary>
    public bool IsMessage { get; init; } = true;

    /// <summary>Whether it was deleted.</summary>
    public bool IsDeleted { get; init; }

    /// <summary>Whether the service marks it as a policy violation.</summary>
    public bool ViolatesPolicy { get; init; }

    /// <summary>The people it mentions.</summary>
    public IReadOnlyList<ChatMention> Mentions { get; init; } = [];

    /// <summary>Its reactions, counted by kind.</summary>
    public IReadOnlyDictionary<string, int> Reactions { get; init; } = new Dictionary<string, int>();

    /// <summary>The names of its attachments: untrusted text.</summary>
    public IReadOnlyList<string> Attachments { get; init; } = [];

    /// <summary>How many replies came with it; null where the service has none to count.</summary>
    public int? Replies { get; init; }

    /// <summary>Whether it has more replies than <see cref="Replies"/>.</summary>
    public bool MoreReplies { get; init; }
}

/// <summary>One page of the service, and the token of the next.</summary>
/// <param name="Items">The page.</param>
/// <param name="Next">The paging token, or null on the last page.</param>
internal sealed record ChatPage<T>(IReadOnlyList<T> Items, string? Next);

/// <summary>A place to read from or post to, by its ids.</summary>
internal sealed record ChatTarget
{
    /// <summary>The team, for a channel.</summary>
    public string? TeamId { get; init; }

    /// <summary>The channel.</summary>
    public string? ChannelId { get; init; }

    /// <summary>The chat.</summary>
    public string? ChatId { get; init; }

    /// <summary>The other person of a one-to-one chat that is not known yet.</summary>
    public ChatPerson? Person { get; init; }

    /// <summary>Whether it is a channel.</summary>
    public bool IsChannel => ChannelId is not null;

    /// <summary>The place as a call writes it, with ids.</summary>
    public string Place => this switch
    {
        { ChannelId: not null } => $"team:{TeamId}/channel:{ChannelId}",
        { ChatId: not null } => $"chat:{ChatId}",
        _ => $"user:{Person?.Id}",
    };
}

/// <summary>What a read asks of a place.</summary>
/// <param name="Top">Page size.</param>
/// <param name="Page">The paging token, or null for the first page.</param>
/// <param name="Until">Only messages before this date (a chat filters on the service; a channel does not).</param>
/// <param name="Thread">The message whose replies are read, in a channel.</param>
internal sealed record ChatReadQuery(int Top, string? Page, DateTimeOffset? Until, string? Thread);

/// <summary>A message to post.</summary>
/// <param name="Html">The body, without its mentions.</param>
/// <param name="Mentions">The people mentioned ahead of the body.</param>
/// <param name="ReplyTo">The message replied to.</param>
internal sealed record ChatOutgoing(string Html, IReadOnlyList<ChatPerson> Mentions, string? ReplyTo);

/// <summary>A message posted.</summary>
/// <param name="Id">Its id, when the service's answer could be read.</param>
/// <param name="Created">When.</param>
internal sealed record ChatSent(string? Id, DateTimeOffset? Created);

/// <summary>What a search looks for. Every criterion given must hold.</summary>
/// <param name="Words">The words a message holds, each taken literally.</param>
/// <param name="From">The address of its author.</param>
/// <param name="Mentions">The directory id of a person it mentions.</param>
/// <param name="Skip">How many hits to pass.</param>
/// <param name="Top">How many hits to return.</param>
internal sealed record ChatSearchQuery(IReadOnlyList<string> Words, string? From, string? Mentions, int Skip, int Top);

/// <summary>A hit of a search: where the message is and nothing of what it says.</summary>
/// <param name="Place">The place, by its ids, not checked against the allowed ones.</param>
/// <param name="Thread">The message it replies to, when the service's link tells.</param>
/// <param name="MessageId">The message.</param>
internal sealed record ChatSearchHit(ChatTarget Place, string? Thread, string MessageId);

/// <summary>A window of hits, and whether the service has more.</summary>
internal sealed record ChatSearchPage(IReadOnlyList<ChatSearchHit> Hits, bool More);

/// <summary>A meeting.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Subject">Its subject: untrusted text.</param>
/// <param name="Start">When it starts.</param>
/// <param name="ChatId">The chat it is held in, when the service tells.</param>
/// <param name="Organizer">The user it was found under, for an application; null for the signed-in user.</param>
internal sealed record ChatMeeting(string Id, string? Subject, DateTimeOffset? Start, string? ChatId, string? Organizer = null);

/// <summary>How the meeting of a chat is found.</summary>
/// <param name="JoinLink">Its join link.</param>
/// <param name="Organizer">The directory id of who organizes it, when the service tells.</param>
internal sealed record ChatMeetingLink(string JoinLink, string? Organizer);

/// <summary>A transcript of a meeting.</summary>
internal sealed record ChatTranscript(string Id, DateTimeOffset? Created, DateTimeOffset? Ended);

/// <summary>The content of a transcript. <paramref name="HasSpeakers"/> is false when it is plain text.</summary>
internal sealed record ChatTranscriptText(string Content, bool HasSpeakers);
