using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Dtos;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Security;

namespace Orkeon.Tools.Chat.Tools;

/// <summary>
/// The messages of one call as results show them: screened, withheld when they must be, with the
/// directory's record of a bounded number of their authors.
/// </summary>
internal sealed class ChatMessageDescriber
{
    private const int MaxListed = 10;
    private const string RejectedReason = "the prompt-injection screen rejected it; an operator can lift Orkeon:Tools:Chat:Screening:WithholdRejected";
    private const string PolicyReason = "the service marks it as a policy violation";
    private const string DeletedReason = "it was deleted";

    private readonly ChatContentScreen _screen;
    private readonly IChatProvider _provider;
    private readonly Dictionary<string, ChatPerson?> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the describer of one call.</summary>
    public ChatMessageDescriber(ChatContentScreen screen, IChatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(provider);
        _screen = screen;
        _provider = provider;
    }

    /// <summary><paramref name="message"/> as a result shows it, its text cut at <paramref name="textChars"/>.</summary>
    public async Task<ChatMessageDto> DescribeAsync(ChatMessage message, int textChars, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var person = await PersonAsync(message.Author, cancellationToken).ConfigureAwait(false);
        var screening = _screen.Screen(
            [message.Text, message.Author?.Name, person?.Address, .. message.Mentions.Select(mention => mention.Name), .. message.Attachments, .. message.Reactions.Keys]);
        var withheld = message switch
        {
            { IsDeleted: true } => DeletedReason,
            { ViolatesPolicy: true } => PolicyReason,
            _ => screening.Withhold ? RejectedReason : null,
        };
        var dto = new ChatMessageDto
        {
            Id = message.Id,
            Author = Author(message.Author, person),
            Time = ChatToolHelpers.Iso(message.Created),
            Replies = message.Replies,
            MoreReplies = message.MoreReplies ? true : null,
        };
        if (withheld is not null)
        {
            // Nothing of the text shows, its verdict included: the author alone is still there to read.
            return dto with
            {
                Withheld = true,
                Reason = withheld,
                Screening = withheld == RejectedReason ? null : _screen.Flag(message.Author?.Name, person?.Address),
            };
        }

        return dto with
        {
            Edited = ChatToolHelpers.Iso(message.Edited),
            Text = ChatToolHelpers.Head(message.Text, textChars),
            TextTruncated = message.Text.Length > textChars ? true : null,
            Screening = screening.Verdict == "clean" ? null : screening.Verdict,
            HiddenText = message.HadHiddenText ? true : null,
            Mentions = message.Mentions.Count == 0 ? null : message.Mentions.Take(MaxListed)
                .Select(mention => new ChatMentionDto { Id = mention.Id, Name = _screen.Label(mention.Name) }).ToList(),
            Reactions = message.Reactions.Count == 0 ? null : message.Reactions.Take(MaxListed)
                .GroupBy(reaction => _screen.Label(reaction.Key) ?? "?", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(reaction => reaction.Value), StringComparer.Ordinal),
            Attachments = message.Attachments.Count == 0 ? null : message.Attachments.Take(MaxListed).Select(name => _screen.Label(name)).OfType<string>().ToList(),
        };
    }

    private async Task<ChatPerson?> PersonAsync(ChatAuthor? author, CancellationToken cancellationToken) =>
        author is null or { IsApplication: true } or { IsExternal: true } ? null : await FindAsync(author.Id, cancellationToken).ConfigureAwait(false);

    private ChatAuthorDto? Author(ChatAuthor? author, ChatPerson? person)
    {
        if (author is null)
            return null;
        return new ChatAuthorDto
        {
            Id = author.Id,
            Name = _screen.Label(author.Name),
            Address = _screen.Label(person?.Address),
            Guest = author.IsExternal || person?.IsGuest == true ? true : null,
            Application = author.IsApplication ? true : null,
        };
    }

    private async Task<ChatPerson?> FindAsync(string? id, CancellationToken cancellationToken)
    {
        if (id is null || !ChatIds.IsDirectoryId(id))
            return null;
        if (_known.TryGetValue(id, out var known))
            return known;
        if (_known.Count >= ChatDefaults.MaxAuthorLookups)
            return null;
        ChatPerson? person = null;
        try
        {
            person = await _provider.GetPersonAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (ChatToolException)
        {
            // The message is worth returning without its author's address.
        }

        _known[id] = person;
        return person;
    }
}
