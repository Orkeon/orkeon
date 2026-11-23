using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Administration;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;
using Orkeon.Tools.Chat.Security;
using Orkeon.Tools.Chat.Tests.Doubles;
using Orkeon.Tools.Chat.Tools;

namespace Orkeon.Tools.Chat.Tests.Fixtures;

/// <summary>
/// The tools over the real provider and client of one account named <c>work</c>, with Graph
/// replayed, a manual clock and a fresh token stored.
/// </summary>
internal sealed class ChatFixture : IDisposable
{
    /// <summary>The token stored at the start.</summary>
    public const string FirstAccess = "first-access";

    private readonly Dictionary<string, ToolBase> _tools = new(StringComparer.Ordinal);
    private readonly HttpClient _http;
    private readonly ChatSessions _sessions;

    /// <summary>Creates the fixture.</summary>
    public ChatFixture(ChatAccountOptions options, bool withhold = false, bool signedIn = true)
    {
        Options.Accounts["work"] = options;
        Options.Screening.WithholdRejected = withhold;
        Graph = new GraphStub(Handler);
        Graph.Get("me", Samples.Person(Samples.Self, "Me", "me@example.org"));
        _http = new HttpClient(Handler, disposeHandler: false);
        OAuth = new OAuth2Client(_http, Time, (wait, _) =>
        {
            Time.Advance(wait);
            return Task.CompletedTask;
        });
        Credentials = new ChatCredentialProvider(Store, OAuth);
        var bound = Microsoft.Extensions.Options.Options.Create(Options);
        Registry = new ChatAccountRegistry(bound);
        Account = Registry.Resolve("work");
        if (signedIn)
            SignIn(Account.OAuth.Scopes);

        _sessions = ChatSessions.Over(Credentials, () => _http, Time);
        Administration = new ChatAccountAdministration(Registry, Credentials, OAuth, _sessions);
        var access = new ChatAccess(Registry, _sessions);
        var screen = new ChatContentScreen(bound);
        Add(new ChatAccountsTool(Administration));
        Add(new ChatListTool(access, screen));
        Add(new ChatReadTool(access, screen));
        Add(new ChatMembersTool(access, screen));
        var turns = new WriteTurns(new SendQuota(Time), new PostPacer(Time));
        Add(new ChatSearchTool(access, screen));
        Add(new ChatMeetingsTool(access, screen));
        Add(new ChatSendTool(access, turns));
        Add(new ChatEditTool(access, turns));
        Add(new ChatDeleteTool(access, turns));
        Add(new ChatReactTool(access, turns));
    }

    /// <summary>The configuration.</summary>
    public ChatToolsOptions Options { get; } = new();

    /// <summary>What received the requests.</summary>
    public FakeHttpMessageHandler Handler { get; } = new();

    /// <summary>What answers them.</summary>
    public GraphStub Graph { get; }

    /// <summary>The clock.</summary>
    public ManualTimeProvider Time { get; } = new();

    /// <summary>The tokens.</summary>
    public FakeChatTokenStore Store { get; } = new();

    /// <summary>The OAuth client, whose waits move the clock.</summary>
    public OAuth2Client OAuth { get; }

    /// <summary>The credentials.</summary>
    public ChatCredentialProvider Credentials { get; }

    /// <summary>The accounts.</summary>
    public ChatAccountRegistry Registry { get; }

    /// <summary>The account.</summary>
    public ResolvedChatAccount Account { get; }

    /// <summary>The administration.</summary>
    public ChatAccountAdministration Administration { get; }

    /// <summary>The tools by name.</summary>
    public IReadOnlyDictionary<string, ToolBase> Tools => _tools;

    /// <summary>The account in use: its provider and its places.</summary>
    public ChatSession Session => _sessions.Get(Account);

    /// <summary>Stores a token good for an hour, granted <paramref name="scopes"/>.</summary>
    public void SignIn(IReadOnlyList<string> scopes) => Store.With(ChatCredentialProvider.TokenKey(Account), new ChatTokenSet
    {
        AccessToken = FirstAccess,
        RefreshToken = "first-refresh",
        ExpiresAt = Time.GetUtcNow().AddHours(1),
        Scopes = scopes,
    });

    /// <summary>The place <paramref name="text"/>, admitted for <paramref name="use"/>.</summary>
    public Task<ChatTarget> AdmitAsync(string text, PlaceUse use = PlaceUse.Send)
    {
        Assert.True(PlaceReference.TryParse(text, out var place), text);
        return Session.Gate.AdmitAsync(place, use, TestContext.Current.CancellationToken);
    }

    /// <summary>The refusal of <paramref name="text"/> for <paramref name="use"/>.</summary>
    public Task<ChatToolException> RefusedAsync(string text, PlaceUse use = PlaceUse.Send) =>
        Assert.ThrowsAsync<ChatToolException>(() => AdmitAsync(text, use));

    /// <summary>Calls <paramref name="tool"/> through the tool protocol.</summary>
    public Task<ToolCallResponse> CallAsync(string tool, params (string Key, object? Value)[] parameters) =>
        _tools[tool].CallAsync(ToolResults.Call(tool, parameters), TestContext.Current.CancellationToken);

    /// <summary>The result of a call that succeeds.</summary>
    public async Task<Dictionary<string, object?>> ResultAsync(string tool, params (string Key, object? Value)[] parameters) =>
        ToolResults.Success(await CallAsync(tool, parameters));

    /// <summary>The error of a call that fails.</summary>
    public async Task<string> ErrorAsync(string tool, params (string Key, object? Value)[] parameters) =>
        ToolResults.Failure(await CallAsync(tool, parameters));

    /// <summary>Waits until <paramref name="pending"/> waits on the clock; nothing here sleeps.</summary>
    public async Task UntilWaitingAsync(Task pending)
    {
        while (Time.PendingTimers == 0 && !pending.IsCompleted)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        Assert.False(pending.IsCompleted);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var tool in _tools.Values)
            tool.Dispose();
        _sessions.Dispose();
        _http.Dispose();
        Handler.Dispose();
    }

    private void Add(ToolBase tool) => _tools.Add(tool.Name, tool);
}
