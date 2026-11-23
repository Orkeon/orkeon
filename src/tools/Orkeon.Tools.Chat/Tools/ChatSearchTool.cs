using System.Security.Cryptography;
using System.Text;
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

/// <summary>
/// Finds messages. The service searches everything its user sees: a hit is only an id here until
/// its place passes the read gate, and its text is then read from that place, never from the hit.
/// Nothing of the result depends on how many hits were left out, a count or a cursor included.
/// </summary>
[ToolContract("chat_search",
    Name = "chat_search",
    Description = "Find messages by words, by author or that mention the signed-in user, in the places the operator allowed reading, screened for prompt injection. Looks at the service's first hits alone: narrow the words when nothing is found. Needs the Read right and, for channels, a permission a tenant administrator grants.",
    Category = "Chat")]
internal sealed class ChatSearchTool : ToolBase<ChatSearchRequest, ChatSearchResponse>
{
    private const string UnreadReason = "the service did not return it: it is gone, or a reply whose thread the search does not tell";
    private const string FailedReason = "the service failed to return it";
    private const string ChannelsLeftOut = "Channels are left out: reading them needs ChannelMessages under Auth:AdminGranted, after a tenant administrator's consent.";

    private readonly ChatAccess _access;
    private readonly ChatContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public ChatSearchTool(ChatAccess access, ChatContentScreen screen, ILogger<ChatSearchTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(screen);
        _access = access;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatSearchRequest request)
    {
        var text = request?.Text ?? string.Empty;
        var author = request?.Author ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(author) && request?.MentionsMe != true)
            return "one of `text`, `author` and `mentions_me` is required";
        if (text.Length > ChatDefaults.MaxSearchChars || author.Length > ChatDefaults.MaxSearchChars)
            return "`text` or `author` is too long";
        if (!string.IsNullOrWhiteSpace(text) && Words(text).Count == 0)
            return "`text` holds no word to search for";
        if (author.Length > 0 && (!PlaceReference.TryParse($"user:{author}", out var person) || person.Form != PlaceForm.User))
            return "`author` must be an address, or the id of a person from chat_members";
        return request?.TextChars is <= 0 ? "`text_chars` must be positive" : null;
    }

    /// <inheritdoc />
    protected override async Task<ChatSearchResponse> ExecuteTypedAsync(ChatSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = _access.Authorize(request.Account, ChatRights.Read);
        if (session.Account.ReadPlaces.Count == 0)
        {
            throw new ChatToolException(
                ChatErrorCode.PlaceNotAllowed,
                $"Chat account '{session.Account.Name}' may read no place. An operator adds some under {ChatDefaults.SectionName}:Accounts:{session.Account.Name}:Read:AllowedPlaces.");
        }

        var limit = Math.Clamp(request.Limit ?? ChatDefaults.DefaultSearchPage, 1, ChatDefaults.MaxSearchPage);
        var cursor = ChatCursor.Decode(request.Cursor, Scope(request), limit);
        var author = string.IsNullOrWhiteSpace(request.Author) ? null : await session.Gate.PersonAsync(request.Author.Trim(), cancellationToken).ConfigureAwait(false);
        if (author is { Address: null })
            throw new ChatToolException(ChatErrorCode.NotSupportedByAccount, "The directory holds no address for this author, which the search needs.");
        var mentioned = request.MentionsMe == true ? (await session.Provider.GetSelfAsync(cancellationToken).ConfigureAwait(false)).Id : null;
        var query = new ChatSearchQuery(Words(request.Text ?? string.Empty), author?.Address, mentioned, 0, ChatDefaults.MaxSearchPage);
        return await CollectAsync(session, request, query, author, cursor, limit, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatSearchResponse> CollectAsync(
        ChatSession session, ChatSearchRequest request, ChatSearchQuery query, ChatPerson? author, ChatCursor cursor, int limit, CancellationToken cancellationToken)
    {
        var budget = ChatToolHelpers.ResultBudget(Name);
        var textChars = Math.Min(request.TextChars ?? ChatDefaults.DefaultTextChars, ChatDefaults.MaxTextChars);
        var describer = new ChatMessageDescriber(_screen, session.Provider);
        var found = new List<ChatSearchHitDto>();
        var channels = session.Account.IsGranted(ChatAdminGrants.ChannelMessages);
        var envelope = new ChatSearchResponse
        {
            Account = session.Account.Name,
            Notice = ChatContentScreen.UntrustedNotice,
            Limits = channels ? null : ChannelsLeftOut,
        };
        var length = ChatToolHelpers.RenderedLength(envelope);
        // Counts the hits in allowed places alone: a position among all of them would tell of the others.
        var position = 0;
        await foreach (var hit in AllowedAsync(session, query, channels, cancellationToken).ConfigureAwait(false))
        {
            if (position++ < cursor.Offset)
                continue;
            var here = cursor with { Offset = position - 1 };
            if (found.Count == limit)
                return Finish(envelope, found, here);
            if (await ReadAsync(session, hit, author, describer, textChars, cancellationToken).ConfigureAwait(false) is not { } dto)
                continue;
            length += ChatToolHelpers.RenderedLength(dto) + 1;
            if (found.Count > 0 && length + here.Encode().Length > budget)
                return Finish(envelope, found, here);
            found.Add(dto);
        }

        return Finish(envelope, found, null);
    }

    /// <summary>The hits whose place may be read, each once.</summary>
    private static async IAsyncEnumerable<ChatSearchHit> AllowedAsync(
        ChatSession session, ChatSearchQuery query, bool channels, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Out of reach here, the service fails the search; further on it only leaves a hit out.
        var rules = await session.Gate.RulesAsync(PlaceUse.Read, cancellationToken).ConfigureAwait(false);
        var places = new Dictionary<string, bool>(StringComparer.Ordinal);
        var lookups = 0;
        var seen = new HashSet<(string, string)>();
        for (var request = 0; request < ChatDefaults.MaxSearchRequests; request++)
        {
            var page = await session.Provider.SearchAsync(query with { Skip = request * query.Top }, cancellationToken).ConfigureAwait(false);
            foreach (var hit in page.Hits.Where(hit => channels || !hit.Place.IsChannel))
            {
                var place = hit.Place.Place;
                if (!places.TryGetValue(place, out var allowed))
                {
                    allowed = PlaceReference.TryParse(place, out var reference)
                        && (!PlaceGate.NeedsLookup(reference, rules) || ++lookups <= ChatDefaults.MaxSearchPlaces)
                        && await IsAllowedAsync(session, reference, cancellationToken).ConfigureAwait(false);
                    places[place] = allowed;
                }

                if (allowed && seen.Add((place, hit.MessageId)))
                    yield return hit;
            }

            if (!page.More)
                yield break;
        }
    }

    private static async Task<bool> IsAllowedAsync(ChatSession session, PlaceReference place, CancellationToken cancellationToken)
    {
        try
        {
            await session.Gate.AdmitAsync(place, PlaceUse.Read, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChatToolException ex) when (ex.Code != ChatErrorCode.SignInRequired)
        {
            // Whatever the reason: an error here would tell that the hit exists.
            return false;
        }
    }

    /// <summary>The message of a hit in an allowed place, read from that place; null when it is not one to show.</summary>
    private static async Task<ChatSearchHitDto?> ReadAsync(
        ChatSession session, ChatSearchHit hit, ChatPerson? author, ChatMessageDescriber describer, int textChars, CancellationToken cancellationToken)
    {
        var place = new ChatSearchHitDto { Place = hit.Place.Place, Thread = hit.Thread };
        ChatMessage message;
        try
        {
            message = await session.Provider.GetMessageAsync(hit.Place, hit.Thread, hit.MessageId, cancellationToken).ConfigureAwait(false);
        }
        catch (ChatToolException ex) when (ex.Code is ChatErrorCode.NotFound or ChatErrorCode.ConsentMissing)
        {
            return place with { Message = new ChatMessageDto { Id = hit.MessageId, Withheld = true, Reason = UnreadReason } };
        }
        catch (ChatToolException ex) when (ex.Code is ChatErrorCode.InvalidRequest or ChatErrorCode.ServerError)
        {
            return place with { Message = new ChatMessageDto { Id = hit.MessageId, Withheld = true, Reason = FailedReason } };
        }

        if (!message.IsMessage || (author is not null && !string.Equals(message.Author?.Id, author.Id, StringComparison.OrdinalIgnoreCase)))
            return null;
        return place with { Message = await describer.DescribeAsync(message, textChars, cancellationToken).ConfigureAwait(false) };
    }

    private static ChatSearchResponse Finish(ChatSearchResponse envelope, List<ChatSearchHitDto> found, ChatCursor? next) => envelope with
    {
        Count = found.Count,
        NextCursor = next?.Encode(),
        Hits = found,
    };

    private static List<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(word => word.EnumerateRunes().Any(Rune.IsLetterOrDigit)).ToList();

    private static string Scope(ChatSearchRequest request) => "search:" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{request.Text?.Trim()}\n{request.Author?.Trim()}\n{request.MentionsMe == true}")))[..16];
}
