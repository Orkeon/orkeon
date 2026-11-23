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
using Orkeon.Tools.Chat.Text;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>
/// Lists and reads the transcripts of one meeting. Its chat is what the read places are compared
/// with, whichever way it was found: a link whose chat the service does not tell is refused.
/// </summary>
[ToolContract("chat_meetings",
    Name = "chat_meetings",
    Description = "List the transcripts of a meeting and read the text of one, with who speaks, screened for prompt injection. The meeting is found by its join link or by its chat, which must be among the places the operator allowed reading. Needs the Meetings right and a permission a tenant administrator grants.",
    Category = "Chat")]
internal sealed class ChatMeetingsTool : ToolBase<ChatMeetingsRequest, ChatMeetingsResponse>
{
    private const string RejectedReason = "the prompt-injection screen rejected it; an operator can lift Orkeon:Tools:Chat:Screening:WithholdRejected";
    private const int MaxIdLength = 512;

    private const string MeetingChatMarker = "@thread.v2";

    private readonly ChatAccess _access;
    private readonly ChatContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public ChatMeetingsTool(ChatAccess access, ChatContentScreen screen, ILogger<ChatMeetingsTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(screen);
        _access = access;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatMeetingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Place) == string.IsNullOrWhiteSpace(request?.JoinLink))
            return "exactly one of `place` and `join_link` is required";
        return request!.TranscriptId is null || IsTranscriptId(request.TranscriptId) ? null : "`transcript_id` must be an id this tool listed";
    }

    /// <inheritdoc />
    protected override async Task<ChatMeetingsResponse> ExecuteTypedAsync(ChatMeetingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var byPlace = !string.IsNullOrWhiteSpace(request.Place);
        var session = _access.Authorize(request.Account, ChatRights.Meetings);
        ChatAccess.DemandMeetings(session.Account);
        var (meeting, chat) = byPlace
            ? await OfChatAsync(session, request.Place!, cancellationToken).ConfigureAwait(false)
            : await OfLinkAsync(session, request.JoinLink!, cancellationToken).ConfigureAwait(false);

        var subject = _screen.Label(meeting.Subject);
        var envelope = new ChatMeetingsResponse
        {
            Account = session.Account.Name,
            Place = chat.Place,
            Notice = ChatContentScreen.UntrustedNotice,
            Subject = subject,
            Start = ChatToolHelpers.Iso(meeting.Start),
            Screening = _screen.Flag(meeting.Subject),
        };
        var transcripts = await session.Provider.ListTranscriptsAsync(meeting, cancellationToken).ConfigureAwait(false);
        return request.TranscriptId is { } id
            ? await TextAsync(session, request, meeting, envelope, transcripts, id, cancellationToken).ConfigureAwait(false)
            : List(request, envelope, transcripts);
    }

    private static async Task<(ChatMeeting Meeting, ChatTarget Chat)> OfChatAsync(ChatSession session, string text, CancellationToken cancellationToken)
    {
        var place = ChatAccess.Place(text);
        if (place.Form != PlaceForm.Chat)
            throw new ChatToolException(ChatErrorCode.InvalidRequest, "`place` must be the chat of the meeting: chat:<id>, from chat_list.");
        if (!session.Account.Grants(ChatRights.Read))
        {
            throw new ChatToolException(
                ChatErrorCode.RightMissing,
                $"Chat account '{session.Account.Name}' has no Read right, which finding the meeting of a chat needs: pass its `join_link` instead.");
        }

        var chat = await session.Gate.AdmitAsync(place, PlaceUse.Read, cancellationToken).ConfigureAwait(false);
        var link = await session.Provider.GetMeetingLinkAsync(place.Value, cancellationToken).ConfigureAwait(false);
        if (MeetingLink.Parse(link?.JoinLink, session.Account.Cloud) is not { } joinLink)
            throw new ChatToolException(ChatErrorCode.NotFound, $"'{chat.Place}' is not the chat of a meeting with a join link.");
        var meeting = await FindAsync(session, joinLink, link?.Organizer, cancellationToken).ConfigureAwait(false);
        return meeting.ChatId is null || meeting.ChatId == place.Value
            ? (meeting, chat)
            : throw new ChatToolException(ChatErrorCode.PlaceNotAllowed, $"The service finds a meeting of another chat for '{chat.Place}'; nothing is read.");
    }

    private static async Task<(ChatMeeting Meeting, ChatTarget Chat)> OfLinkAsync(ChatSession session, string text, CancellationToken cancellationToken)
    {
        if (MeetingLink.Parse(text, session.Account.Cloud) is not { } joinLink)
        {
            throw new ChatToolException(
                ChatErrorCode.InvalidRequest,
                $"`join_link` must be the https link of a meeting on {MeetingLink.Host(session.Account.Cloud)}, as its invitation holds it.");
        }

        if (session.Account.ReadPlaces.Count == 0)
        {
            throw new ChatToolException(
                ChatErrorCode.PlaceNotAllowed,
                $"Chat account '{session.Account.Name}' may read no place. An operator adds some under {ChatDefaults.SectionName}:Accounts:{session.Account.Name}:Read:AllowedPlaces.");
        }

        var meeting = await FindAsync(session, joinLink, organizer: null, cancellationToken).ConfigureAwait(false);
        // Nothing but the service's own word ties a link to a chat: the text of the link is the model's.
        if (meeting.ChatId is not { } chatId || !ChatIds.IsConversationId(chatId) || !PlaceReference.TryParse($"chat:{chatId}", out var place) || place.Form != PlaceForm.Chat)
            throw new ChatToolException(ChatErrorCode.PlaceNotAllowed, "The service does not tell the chat of this meeting, so whether it may be read cannot be established; nothing is read.");
        if (!chatId.EndsWith(MeetingChatMarker, StringComparison.OrdinalIgnoreCase))
            throw new ChatToolException(ChatErrorCode.NotSupportedByAccount, "This meeting is not held in a chat: the transcripts of a channel's meetings are not read.");
        try
        {
            return (meeting, await session.Gate.AdmitAsync(place, PlaceUse.Read, cancellationToken).ConfigureAwait(false));
        }
        catch (ChatToolException ex) when (ex.Code == ChatErrorCode.PlaceNotAllowed)
        {
            // The refusal of the gate names the place, which the call did not.
            throw new ChatToolException(
                ChatErrorCode.PlaceNotAllowed,
                $"The chat of this meeting is not a place chat account '{session.Account.Name}' may read. An operator adds it under {ChatDefaults.SectionName}:Accounts:{session.Account.Name}:Read:AllowedPlaces.",
                ex);
        }
    }

    private static async Task<ChatMeeting> FindAsync(ChatSession session, string joinLink, string? organizer, CancellationToken cancellationToken) =>
        await session.Provider.FindMeetingAsync(joinLink, organizer, cancellationToken).ConfigureAwait(false)
        ?? throw new ChatToolException(ChatErrorCode.NotFound, "The service finds no meeting of the account with this link.");

    private ChatMeetingsResponse List(ChatMeetingsRequest request, ChatMeetingsResponse envelope, IReadOnlyList<ChatTranscript> transcripts)
    {
        var cursor = ChatCursor.Decode(request.Cursor, $"meeting:{envelope.Place}", 1);
        var left = transcripts.Skip(cursor.Offset)
            .Select(transcript => new ChatTranscriptDto { Id = transcript.Id, Created = ChatToolHelpers.Iso(transcript.Created), Ended = ChatToolHelpers.Iso(transcript.Ended) })
            .ToList();
        var room = envelope with { NextCursor = (cursor with { Offset = int.MaxValue }).Encode() };
        var shown = left.Count == 0 ? 0 : ChatToolHelpers.Fit(left, room, ChatToolHelpers.ResultBudget(Name));
        return envelope with
        {
            Transcripts = left.Take(shown).ToList(),
            NextCursor = shown < left.Count ? (cursor with { Offset = cursor.Offset + shown }).Encode() : null,
        };
    }

    private async Task<ChatMeetingsResponse> TextAsync(
        ChatSession session, ChatMeetingsRequest request, ChatMeeting meeting, ChatMeetingsResponse envelope,
        IReadOnlyList<ChatTranscript> transcripts, string id, CancellationToken cancellationToken)
    {
        if (transcripts.All(transcript => transcript.Id != id))
            throw new ChatToolException(ChatErrorCode.NotFound, "This meeting has no transcript with this id: call without `transcript_id` to list them.");
        var cursor = ChatCursor.Decode(request.Cursor, $"transcript:{envelope.Place}#{id}", 1);
        var transcript = await session.Provider.ReadTranscriptAsync(meeting, id, cancellationToken).ConfigureAwait(false);
        var read = transcript.HasSpeakers ? TranscriptText.FromVtt(transcript.Content) : TranscriptText.FromPlain(transcript.Content);

        // The whole text at every page, as it shows and as it is said: a time, a name or a cut may
        // fall inside what the screen looks for.
        var screening = _screen.ScreenLong(string.Join('\n', read.Lines), read.Words);
        envelope = envelope with
        {
            TranscriptId = id,
            NoSpeakers = transcript.HasSpeakers ? null : true,
            Screening = screening.Verdict == "rejected" || envelope.Screening is null
                ? (screening.Verdict == "clean" ? null : screening.Verdict)
                : envelope.Screening,
        };
        return screening.Withhold ? envelope with { Withheld = true, Reason = RejectedReason } : Page(envelope, read.Lines, cursor);
    }

    private ChatMeetingsResponse Page(ChatMeetingsResponse envelope, IReadOnlyList<string> lines, ChatCursor cursor)
    {
        var left = lines.Skip(cursor.Offset).ToList();
        var room = envelope with { Text = string.Empty, NextCursor = (cursor with { Offset = int.MaxValue }).Encode() };
        var shown = left.Count == 0 ? 0 : ChatToolHelpers.Fit(left, room, ChatToolHelpers.ResultBudget(Name));
        return envelope with
        {
            Text = string.Join('\n', left.Take(shown)),
            NextCursor = shown < left.Count ? (cursor with { Offset = cursor.Offset + shown }).Encode() : null,
        };
    }

    private static bool IsTranscriptId(string id) =>
        id.Length is > 0 and <= MaxIdLength && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '=' or '+' or '/' or '*' or '.' or '!' or ':' or '@');
}
