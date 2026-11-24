using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Dtos;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Security;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>Lists the members of a place, or looks a person up.</summary>
[ToolContract("chat_members",
    Name = "chat_members",
    Description = "List the members of a channel or a chat (`place`), or look a person up in the directory by name, address or id (`person`), to mention them or write to them with chat_send. The members of a place need the Read right; a person, the Read or the Send right.",
    Category = "Chat")]
internal sealed class ChatMembersTool : ToolBase<ChatMembersRequest, ChatMembersResponse>
{
    private readonly ChatAccess _access;
    private readonly ChatContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public ChatMembersTool(ChatAccess access, ChatContentScreen screen, ILogger<ChatMembersTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(screen);
        _access = access;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatMembersRequest request) =>
        string.IsNullOrWhiteSpace(request?.Place) == string.IsNullOrWhiteSpace(request?.Person) ? "pass either `place` or `person`" : null;

    /// <inheritdoc />
    protected override async Task<ChatMembersResponse> ExecuteTypedAsync(ChatMembersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ChatSession session;
        string scope;
        string? place = null;
        List<ChatMemberDto> all;
        if (string.IsNullOrWhiteSpace(request.Person))
        {
            session = _access.Authorize(request.Account, ChatRights.Read);
            var asked = ChatAccess.Place(request.Place);
            if (asked.Form == PlaceForm.Channel)
                ChatAccess.DemandChannelMembers(session.Account);
            var target = await session.Gate.AdmitAsync(asked, PlaceUse.Read, cancellationToken).ConfigureAwait(false);
            scope = place = target.Place;
            all = target.Person is { } person
                ? [Member(person)]
                : (await session.Provider.ListMembersAsync(target, cancellationToken).ConfigureAwait(false)).Select(Member).ToList();
        }
        else
        {
            session = _access.Authorize(request.Account, ChatRights.Read, ChatRights.Send);
            var wanted = request.Person.Trim();
            scope = $"person:{wanted}";
            all = (await FindAsync(session.Provider, wanted, cancellationToken).ConfigureAwait(false)).Select(Member).ToList();
        }

        var limit = ChatToolHelpers.Limit(request.Limit);
        var cursor = ChatCursor.Decode(request.Cursor, scope, limit);
        var rest = all.Skip(cursor.Offset).Take(limit).ToList();
        var envelope = new ChatMembersResponse
        {
            Account = session.Account.Name,
            Place = place,
            Notice = ChatContentScreen.UntrustedNotice,
            NextCursor = (cursor with { Offset = all.Count }).Encode(),
        };
        var kept = rest.Take(ChatToolHelpers.Fit(rest, envelope, ChatToolHelpers.ResultBudget(Name))).ToList();
        var shown = cursor.Offset + kept.Count;
        return envelope with
        {
            Count = kept.Count,
            Members = kept,
            NextCursor = shown < all.Count ? (cursor with { Offset = shown }).Encode() : null,
        };
    }

    private static async Task<IReadOnlyList<ChatPerson>> FindAsync(IChatProvider provider, string wanted, CancellationToken cancellationToken)
    {
        if (ChatIds.IsDirectoryId(wanted))
            return await provider.GetPersonAsync(wanted, cancellationToken).ConfigureAwait(false) is { } person ? [person] : [];
        // The form of an address is checked as a place's is: the text becomes a path segment.
        if (PlaceReference.TryParse($"user:{wanted}", out var place) && place.Form == PlaceForm.User)
            return await provider.FindByAddressAsync(place.Value, cancellationToken).ConfigureAwait(false);
        return await provider.FindByNameAsync(wanted, ChatDefaults.DefaultPage, cancellationToken).ConfigureAwait(false);
    }

    private ChatMemberDto Member(ChatPerson person) => new()
    {
        Id = ChatIds.Shown(person.Id),
        Name = _screen.Label(person.Name),
        Address = _screen.Label(person.Address),
        Guest = person.IsGuest ? true : null,
        Screening = _screen.Flag(person.Name, person.Address),
    };

    private ChatMemberDto Member(ChatMember member) => new()
    {
        Id = ChatIds.Shown(member.UserId),
        Name = _screen.Label(member.Name),
        Address = _screen.Label(member.Email),
        Guest = member.Roles.Contains("guest", StringComparer.OrdinalIgnoreCase) ? true : null,
        Owner = member.Roles.Contains("owner", StringComparer.OrdinalIgnoreCase) ? true : null,
        Screening = _screen.Flag(member.Name, member.Email),
    };
}
