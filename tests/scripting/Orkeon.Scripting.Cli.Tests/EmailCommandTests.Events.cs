using System.Text.Json;
using System.Web;
using Orkeon.Constants.Protocol;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon email login --events jsonl</c> (STUDIO-70): the sign-in a program drives. Each step
/// is one event line on standard output and nothing else is written there; the address the
/// browser ended on is read as a plain line on standard input; closing that input aborts the
/// sign-in, so a driver that goes away never leaves the verb waiting. Without the option the verb
/// speaks to a terminal as it always did.
/// </summary>
public sealed partial class EmailCommandTests
{
    private const string GoogleSecretVariable = "EMAILCMD_TEST_GOOGLE_CLIENT_SECRET";

    [Fact]
    public async Task Login_WithEvents_TheDeviceSignIn_WritesTheCodeThenTheCompletion_OneJsonLineEach_AndNothingElse()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteOutlookSettings(scratch, credentials);
        using var identity = new StubOutlookHttpMessageHandler();
        using var console = new ConversationConsole();

        var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "hotmail",
            Events = "jsonl",
            ConfigureTestServices = Answering(identity),
        });

        Assert.Equal(Program.ExitOk, exit);
        var events = EventLines(console.Stdout);
        Assert.Equal(2, events.Count);

        var code = events[0];
        Assert.Equal(EmailEventKinds.LoginDeviceCode, code.GetProperty("kind").GetString());
        Assert.Equal("https://microsoft.com/devicelogin", code.GetProperty("verification_uri").GetString());
        Assert.Equal("WXYZ-1234", code.GetProperty("user_code").GetString());
        Assert.Equal(900, code.GetProperty("expires_in").GetInt32());

        var completed = events[1];
        Assert.Equal(EmailEventKinds.LoginCompleted, completed.GetProperty("kind").GetString());
        Assert.Equal("hotmail", completed.GetProperty("account").GetString());
        Assert.Equal([1, 2], events.Select(e => e.GetProperty("seq").GetInt32()));

        // The sentences of the terminal are not this mode's: a reader of events parses every line.
        Assert.DoesNotContain("Signed in", console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("orkeon email login", console.Stderr, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(credentials, "email"), "hotmail-*.json"));
    }

    [Fact]
    public async Task Login_WithEvents_TheBrowserSignIn_WritesTheAddress_TakesTheRedirectPastedOnStandardInput_ThenCompletes()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteGmailOAuthSettings(scratch, credentials);
        using var identity = new StubOutlookHttpMessageHandler();
        using var secret = new EnvironmentVariableScope(GoogleSecretVariable, "google-secret");
        using var console = new ConversationConsole();

        var login = EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "perso",
            Events = "jsonl",
            ConfigureTestServices = Answering(identity),
        });

        using var shown = JsonDocument.Parse(await console.LineAsync(0).WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken));
        Assert.Equal(EmailEventKinds.LoginAuthorizationUrl, shown.RootElement.GetProperty("kind").GetString());
        var authorization = new Uri(shown.RootElement.GetProperty("authorization_uri").GetString()!);
        Assert.Equal("accounts.google.com", authorization.Host);
        Assert.False(login.IsCompleted);

        // What a browser that could not reach this machine ends on: the registered loopback
        // address, with the code and the state of this very sign-in.
        var query = HttpUtility.ParseQueryString(authorization.Query);
        console.Input.WriteLine("this line is not an address");
        console.Input.WriteLine($"{query["redirect_uri"]}?code=auth-code-1&state={query["state"]}");

        var exit = await login.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(Program.ExitOk, exit);
        var events = EventLines(console.Stdout);
        Assert.Equal(
            [EmailEventKinds.LoginAuthorizationUrl, EmailEventKinds.LoginCompleted],
            events.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal("perso", events[1].GetProperty("account").GetString());
        Assert.Contains(identity.Bodies, body => body.Contains("code=auth-code-1", StringComparison.Ordinal)
            && body.Contains("client_secret=google-secret", StringComparison.Ordinal));
        Assert.Single(Directory.GetFiles(Path.Combine(credentials, "email"), "perso-*.json"));
    }

    [Fact]
    public async Task Login_WithEvents_OfAPasswordAccount_WritesAnErrorWithItsCode_AndExitsWithOne()
    {
        using var scratch = new ScriptScratch();
        scratch.WriteFile("appsettings.json", """
            { "Orkeon": { "Tools": { "Email": { "Accounts": {
                "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                           "Auth": { "PasswordEnvVar": "ORKEON_TEST_EMAIL_UNSET_VARIABLE" } }
            } } } } }
            """);
        using var console = new ConversationConsole();

        var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "perso",
            Events = "jsonl",
        });

        // The exit code is the one the verb has without events: a configuration matter.
        Assert.Equal(Program.ExitScriptError, exit);
        var error = Assert.Single(EventLines(console.Stdout));
        Assert.Equal(EmailEventKinds.Error, error.GetProperty("kind").GetString());
        Assert.Equal("InvalidRequest", error.GetProperty("code").GetString());
        Assert.Contains("signs in with a password", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(error.GetProperty("recoverable").GetBoolean());
        // Said once, on the channel the driver reads.
        Assert.DoesNotContain("signs in with a password", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WithEvents_WhenTheProviderGrantsNoRefreshToken_WritesTheRefusal_AndStoresNothing()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteOutlookSettings(scratch, credentials);
        using var identity = new StubOutlookHttpMessageHandler
        {
            TokenAnswer = """{ "token_type": "Bearer", "access_token": "access-1", "expires_in": 3600 }""",
        };
        using var console = new ConversationConsole();

        var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "hotmail",
            Events = "jsonl",
            ConfigureTestServices = Answering(identity),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        var events = EventLines(console.Stdout);
        Assert.Equal([EmailEventKinds.LoginDeviceCode, EmailEventKinds.Error], events.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal("LoginRequired", events[1].GetProperty("code").GetString());
        Assert.Contains("no refresh token", events[1].GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(credentials, "email"))
            && Directory.GetFiles(Path.Combine(credentials, "email"), "hotmail-*.json").Length > 0);
    }

    /// <summary>
    /// The driver's standard input is its hold on the verb: the loopback wait has no deadline, so
    /// a driver that closes the input — or dies, which closes it too — must end the sign-in, and
    /// the listening port with it.
    /// </summary>
    [Fact]
    public async Task Login_WithEvents_WhenStandardInputCloses_TheBrowserSignInIsAborted_AndNothingIsStored()
    {
        using var scratch = new ScriptScratch();
        var credentials = Path.Combine(scratch.Root, "credentials");
        WriteGmailOAuthSettings(scratch, credentials);
        using var identity = new StubOutlookHttpMessageHandler();
        using var secret = new EnvironmentVariableScope(GoogleSecretVariable, "google-secret");
        using var console = new ConversationConsole();

        var login = EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "perso",
            Events = "jsonl",
            ConfigureTestServices = Answering(identity),
        });
        await console.LineAsync(0).WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.False(login.IsCompleted);

        console.Input.Close();
        var exit = await login.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(Program.ExitCancelled, exit);
        Assert.Single(EventLines(console.Stdout));
        Assert.DoesNotContain(identity.Requests, path => path.EndsWith("/token", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(credentials, "email"))
            && Directory.GetFiles(Path.Combine(credentials, "email")).Length > 0);
    }

    [Fact]
    public async Task Login_WithEvents_WhenStandardInputCloses_TheDeviceSignInIsAbortedToo()
    {
        using var scratch = new ScriptScratch();
        WriteOutlookSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        // The person never types the code: the provider answers "pending" for as long as it is asked.
        using var identity = new StubOutlookHttpMessageHandler { TokenAnswer = """{ "error": "authorization_pending" }""" };
        using var console = new ConversationConsole();

        var login = EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "hotmail",
            Events = "jsonl",
            ConfigureTestServices = Answering(identity),
        });
        await console.LineAsync(0).WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        console.Input.Close();
        var exit = await login.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(Program.ExitCancelled, exit);
        Assert.Equal([EmailEventKinds.LoginDeviceCode], EventLines(console.Stdout).Select(e => e.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task Login_WithAnEventsFormatItDoesNotKnow_IsRefusedBeforeAnythingStarts()
    {
        using var scratch = new ScriptScratch();
        WriteOutlookSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var identity = new StubOutlookHttpMessageHandler();
        using var console = new ConversationConsole();

        var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "hotmail",
            Events = "xml",
            ConfigureTestServices = Answering(identity),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unsupported --events format 'xml'", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
        Assert.Empty(identity.Requests);
    }

    /// <summary>
    /// The vocabulary is declared once, for the CLI that writes it and Orkeon Studio that reads
    /// it: the three journeys together write every declared kind, and no other.
    /// </summary>
    [Fact]
    public async Task Login_WithEvents_WritesEveryKindOfTheSharedVocabulary_AndNoOther()
    {
        var written = new HashSet<string>(StringComparer.Ordinal);

        using (var scratch = new ScriptScratch())
        using (var identity = new StubOutlookHttpMessageHandler())
        using (var console = new ConversationConsole())
        {
            WriteOutlookSettings(scratch, Path.Combine(scratch.Root, "credentials"));
            await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root, Account = "hotmail", Events = "jsonl", ConfigureTestServices = Answering(identity),
            });
            written.UnionWith(EventLines(console.Stdout).Select(e => e.GetProperty("kind").GetString()!));
        }

        using (var scratch = new ScriptScratch())
        using (var identity = new StubOutlookHttpMessageHandler())
        using (new EnvironmentVariableScope(GoogleSecretVariable, "google-secret"))
        using (var console = new ConversationConsole())
        {
            WriteGmailOAuthSettings(scratch, Path.Combine(scratch.Root, "credentials"));
            var login = EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root, Account = "perso", Events = "jsonl", ConfigureTestServices = Answering(identity),
            });
            await console.LineAsync(0).WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);
            console.Input.Close();
            await login.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);
            written.UnionWith(EventLines(console.Stdout).Select(e => e.GetProperty("kind").GetString()!));
        }

        using (var scratch = new ScriptScratch())
        using (var console = new ConversationConsole())
        {
            scratch.WriteFile("appsettings.json", "{}");
            await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
            {
                WorkingDirectoryOverride = scratch.Root, Account = "nobody", Events = "jsonl",
            });
            written.UnionWith(EventLines(console.Stdout).Select(e => e.GetProperty("kind").GetString()!));
        }

        Assert.Equal(EmailEventKinds.All.Order(StringComparer.Ordinal), written.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Without the option nothing moved: the two sentences of the device sign-in and the closing
    /// one, as a terminal has always read them — no event, no extra line.
    /// </summary>
    [Fact]
    public async Task Login_WithoutEvents_SpeaksToTheTerminalExactlyAsBefore()
    {
        using var scratch = new ScriptScratch();
        WriteOutlookSettings(scratch, Path.Combine(scratch.Root, "credentials"));
        using var identity = new StubOutlookHttpMessageHandler();
        using var console = new TestConsole();

        var exit = await EmailCommand.ExecuteLoginAsync(new EmailLoginCommandOptions
        {
            WorkingDirectoryOverride = scratch.Root,
            Account = "hotmail",
            ConfigureTestServices = Answering(identity),
        });

        Assert.Equal(Program.ExitOk, exit);
        var newLine = Environment.NewLine;
        Assert.Equal(
            "To sign e-mail account 'hotmail' in, open https://microsoft.com/devicelogin in a browser and enter the code WXYZ-1234." + newLine
            + "Waiting for the sign-in to complete (Ctrl+C to abort)…" + newLine
            + "Signed in: the tokens of e-mail account 'hotmail' are stored. Agents can use it now." + newLine,
            console.Stdout);
    }

    /// <summary>The event lines of <paramref name="stdout"/>: every line must be one JSON document.</summary>
    private static List<JsonElement> EventLines(string stdout) =>
        [.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })];

    /// <summary>One Gmail account signing in with OAuth2 in the browser, its client secret read from <see cref="GoogleSecretVariable"/>.</summary>
    private static void WriteGmailOAuthSettings(ScriptScratch scratch, string credentials) =>
        scratch.WriteFile("appsettings.json", $$"""
            { "Orkeon": { "Tools": { "Email": {
                "CredentialsDirectory": {{JsonSerializer.Serialize(credentials)}},
                "Accounts": {
                  "perso": { "Provider": "Gmail", "Address": "me@gmail.com", "Rights": "Read",
                             "Auth": { "Method": "OAuth2", "ClientId": "client-456.apps.googleusercontent.com",
                                       "ClientSecretEnvVar": "{{GoogleSecretVariable}}" } }
                } } } } }
            """);

    /// <summary>Sets one environment variable of the process for the duration of a scope.</summary>
    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
