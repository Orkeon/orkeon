using System.Globalization;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Providers;

namespace Orkeon.Tools.Chat.Places;

/// <summary>What a call wants of a place.</summary>
internal enum PlaceUse
{
    /// <summary>Read it, under <c>Read:AllowedPlaces</c>.</summary>
    Read,

    /// <summary>Post in it, under <c>Send:AllowedPlaces</c>.</summary>
    Send,
}

/// <summary>
/// One account's places: turns the names of the settings and of the calls into ids, and checks a
/// place against the allowed ones by ids alone. The allowed places are resolved when the account
/// is first used and kept, so a rename changes nothing until the next start.
/// </summary>
internal sealed class PlaceGate : IDisposable
{
    private const string DirectChatMarker = "@unq.gbl.spaces";

    private readonly ResolvedChatAccount _account;
    private readonly IChatProvider _provider;
    private readonly SemaphoreSlim _resolving = new(1, 1);
    private readonly Dictionary<PlaceUse, PlaceRules> _rules = [];

    /// <summary>Creates the gate of <paramref name="account"/>.</summary>
    public PlaceGate(ResolvedChatAccount account, IChatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(provider);
        _account = account;
        _provider = provider;
    }

    /// <inheritdoc />
    public void Dispose() => _resolving.Dispose();

    /// <summary>The allowed places for <paramref name="use"/>, resolved once. A failure to reach the service is not kept, nor a missing consent.</summary>
    public async Task<PlaceRules> RulesAsync(PlaceUse use, CancellationToken cancellationToken)
    {
        await _resolving.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_rules.TryGetValue(use, out var known))
                return known;
            var rules = await ResolveRulesAsync(use == PlaceUse.Read ? _account.ReadPlaces : _account.SendPlaces, cancellationToken).ConfigureAwait(false);
            // A consent may be given while this process runs.
            if (!rules.Provisional)
                _rules[use] = rules;
            return rules;
        }
        finally
        {
            _resolving.Release();
        }
    }

    /// <summary>
    /// The place <paramref name="place"/> designates, checked for <paramref name="use"/>. A person's
    /// chat is returned as the person: whether a chat exists is not looked at, and none is opened.
    /// </summary>
    public async Task<ChatTarget> AdmitAsync(PlaceReference place, PlaceUse use, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        var rules = await RulesAsync(use, cancellationToken).ConfigureAwait(false);
        switch (place.Form)
        {
            case PlaceForm.Channel:
                var team = await TeamIdAsync(place.Value, cancellationToken).ConfigureAwait(false);
                var channel = await ChannelIdAsync(team, place.Channel, cancellationToken).ConfigureAwait(false);
                if (!rules.AllowsChannel(team, channel))
                    throw Refuse(place, use, rules);
                // An allowed team covers its own channels: an id that came with the call is not one until the team lists it.
                if (!rules.Any && !rules.NamesChannel(team, channel)
                    && (await _provider.ListChannelsAsync(team, cancellationToken).ConfigureAwait(false)).All(listed => listed.Id != channel))
                {
                    throw Refuse(place, use, rules);
                }

                return new ChatTarget { TeamId = team, ChannelId = channel };
            case PlaceForm.Chat:
                if (rules.AllowsChat(place.Value) || await AllowsDirectChatAsync(place, use, rules, cancellationToken).ConfigureAwait(false))
                    return new ChatTarget { ChatId = place.Value };
                throw Refuse(place, use, rules);
            case PlaceForm.User:
                var person = await PersonAsync(place.Value, cancellationToken).ConfigureAwait(false);
                if (string.Equals(person.Id, (await _provider.GetSelfAsync(cancellationToken).ConfigureAwait(false)).Id, StringComparison.OrdinalIgnoreCase))
                    throw new ChatToolException(ChatErrorCode.NotSupportedByAccount, $"'{place.Text}' is the account itself, which has no one-to-one chat with itself.");
                return AllowsPerson(rules, person) ? new ChatTarget { Person = person } : throw Refuse(place, use, rules);
            default:
                throw new ChatToolException(
                    ChatErrorCode.InvalidRequest,
                    $"'{place.Text}' is not one place: write team:<team>/channel:<channel>, chat:<id> or user:<address>.");
        }
    }

    /// <summary>Whether admitting a place, given by its ids, asks the service anything.</summary>
    public static bool NeedsLookup(PlaceReference place, PlaceRules rules)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(rules);
        return place.Form == PlaceForm.Channel
            ? !rules.Any && rules.AllowsChannel(place.Value, place.Channel) && !rules.NamesChannel(place.Value, place.Channel)
            : !rules.AllowsChat(place.Value) && rules.NamesPeople && place.Value.EndsWith(DirectChatMarker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The id of a team a call names, listed or read by whoever may see it.</summary>
    public async Task<string> TeamIdAsync(string nameOrId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nameOrId);
        if (ChatIds.IsDirectoryId(nameOrId))
            return nameOrId;
        var teams = await _provider.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        return Single(teams.Where(team => SameName(team.Name, nameOrId)).Select(team => team.Id).ToList(), $"team '{nameOrId}'");
    }

    /// <summary>The person an address or an id designates.</summary>
    public async Task<ChatPerson> PersonAsync(string addressOrId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addressOrId);
        if (ChatIds.IsDirectoryId(addressOrId))
        {
            return await _provider.GetPersonAsync(addressOrId, cancellationToken).ConfigureAwait(false)
                ?? throw new ChatToolException(ChatErrorCode.NotFound, $"The directory has no person '{addressOrId}'.");
        }

        var found = await _provider.FindByAddressAsync(addressOrId, cancellationToken).ConfigureAwait(false);
        return found.Count == 1
            ? found[0]
            : throw new ChatToolException(ChatErrorCode.NotFound, $"{Count(found.Count)} in the directory for the address '{addressOrId}': pass the id chat_members returns.");
    }

    /// <summary>Whether a listed chat is allowed: by its id, or by the person a one-to-one chat is with.</summary>
    public async Task<bool> AllowsChatAsync(ChatConversation chat, PlaceUse use, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chat);
        var rules = await RulesAsync(use, cancellationToken).ConfigureAwait(false);
        return rules.AllowsChat(chat.Id) || await AllowsPeerAsync(rules, chat, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> AllowsDirectChatAsync(PlaceReference place, PlaceUse use, PlaceRules rules, CancellationToken cancellationToken)
    {
        if (!rules.NamesPeople || !place.Value.EndsWith(DirectChatMarker, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!_account.Grants(ChatRights.Read))
        {
            throw new ChatToolException(
                ChatErrorCode.PlaceNotAllowed,
                $"Chat account '{_account.Name}' cannot tell who '{place.Text}' is with, since it has no Read right: write user:<address> instead. Setting: {Setting(use)}.");
        }

        var chat = await _provider.GetChatAsync(place.Value, cancellationToken).ConfigureAwait(false);
        return await AllowsPeerAsync(rules, chat, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> AllowsPeerAsync(PlaceRules rules, ChatConversation chat, CancellationToken cancellationToken)
    {
        if (!chat.IsOneOnOne || !rules.NamesPeople)
            return false;
        var self = await _provider.GetSelfAsync(cancellationToken).ConfigureAwait(false);
        var peers = chat.Members.Where(member => !string.Equals(member.UserId, self.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (peers.Count != 1 || peers[0].UserId is not { Length: > 0 } peer)
            return false;
        if (rules.Users.Contains(peer))
            return true;
        if (rules.Domains.Count == 0 || !ChatIds.IsDirectoryId(peer))
            return false;
        // The directory's word alone: someone of another tenant, unknown to it, matches no domain.
        var person = await _provider.GetPersonAsync(peer, cancellationToken).ConfigureAwait(false);
        return person is not null && AllowsPerson(rules, person);
    }

    private static bool AllowsPerson(PlaceRules rules, ChatPerson person) =>
        rules.Any || rules.Users.Contains(person.Id) || rules.Domains.Any(person.IsOfDomain);

    private async Task<string> ChannelIdAsync(string teamId, string nameOrId, CancellationToken cancellationToken)
    {
        if (ChatIds.IsChannelId(nameOrId))
            return nameOrId;
        var channels = await _provider.ListChannelsAsync(teamId, cancellationToken).ConfigureAwait(false);
        var id = Single(channels.Where(channel => SameName(channel.Name, nameOrId)).Select(channel => channel.Id).ToList(), $"channel '{nameOrId}'");
        return ChatIds.IsConversationId(id) ? id : throw new ChatToolException(ChatErrorCode.ServerError, $"The service returned an unexpected id for channel '{nameOrId}'.");
    }

    private async Task<PlaceRules> ResolveRulesAsync(IReadOnlyList<PlaceReference> entries, CancellationToken cancellationToken)
    {
        var teams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var channels = new List<(string Team, string Channel)>();
        var chats = new HashSet<string>(StringComparer.Ordinal);
        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domains = new List<string>();
        var problems = new List<string>();
        var provisional = false;
        foreach (var entry in entries)
        {
            try
            {
                switch (entry.Form)
                {
                    case PlaceForm.Team:
                        teams.Add(await TeamIdAsync(entry.Value, cancellationToken).ConfigureAwait(false));
                        break;
                    case PlaceForm.Channel:
                        var team = await TeamIdAsync(entry.Value, cancellationToken).ConfigureAwait(false);
                        channels.Add((team, await ChannelIdAsync(team, entry.Channel, cancellationToken).ConfigureAwait(false)));
                        break;
                    case PlaceForm.Chat:
                        chats.Add(entry.Value);
                        break;
                    case PlaceForm.User:
                        var person = await PersonAsync(entry.Value, cancellationToken).ConfigureAwait(false);
                        users.Add(person.Id);
                        break;
                    case PlaceForm.Domain:
                        domains.Add(entry.Value);
                        break;
                    default:
                        break;
                }
            }
            catch (ChatToolException ex) when (ex.Code is ChatErrorCode.NotFound or ChatErrorCode.ConsentMissing or ChatErrorCode.InvalidRequest)
            {
                provisional |= ex.Code == ChatErrorCode.ConsentMissing;
                problems.Add($"'{entry.Text}' is set aside: {ex.Message}");
            }
        }

        return new PlaceRules
        {
            Any = entries.Any(entry => entry.Form == PlaceForm.Any),
            Teams = teams,
            Channels = channels,
            Chats = chats,
            Users = users,
            Domains = domains,
            Problems = problems,
            Provisional = provisional,
        };
    }

    private ChatToolException Refuse(PlaceReference place, PlaceUse use, PlaceRules rules)
    {
        var aside = rules.Problems.Count == 0 ? string.Empty : $" Entries set aside: {string.Join(" ", rules.Problems)}";
        return new ChatToolException(
            ChatErrorCode.PlaceNotAllowed,
            $"'{place.Text}' is not a place chat account '{_account.Name}' may {(use == PlaceUse.Read ? "read" : "post in")}. An operator adds it under {Setting(use)}.{aside}");
    }

    private string Setting(PlaceUse use) => $"{ChatDefaults.SectionName}:Accounts:{_account.Name}:{use}:AllowedPlaces";

    private static string Single(List<string> ids, string what) => ids.Count == 1
        ? ids[0]
        : throw new ChatToolException(ChatErrorCode.NotFound, $"{Count(ids.Count)} for {what}: use the id chat_list returns.");

    private static string Count(int found) => found == 0 ? "No match" : string.Create(CultureInfo.InvariantCulture, $"{found} matches");

    private static bool SameName(string name, string wanted) => string.Equals(name.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);
}
