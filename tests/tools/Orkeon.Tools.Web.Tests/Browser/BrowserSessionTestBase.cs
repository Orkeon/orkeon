using Orkeon.Tools.Web.Browser;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests.Browser;

/// <summary>What the firefox tests share: a launcher of fake browsers and waits short enough for a unit test.</summary>
public abstract class BrowserSessionTestBase
{
    internal const string Home = "https://example.test/";
    internal const string Docs = "https://example.test/docs";

    internal static readonly BrowserSessionTimings Timings = new()
    {
        NavigationGrace = TimeSpan.Zero,
        TraversalWait = TimeSpan.FromMilliseconds(40),
        PollInterval = TimeSpan.FromMilliseconds(5),
    };

    internal FakeBrowserEngine Launcher { get; } = new()
    {
        Configure = browser =>
        {
            browser.Pages[Home] = new FakePage { Title = "Example", Lead = "Welcome to the example.", Text = "Welcome to the example. Guide Install it. More" }
                .Region("navigation", "Main")
                .Link("Docs", Docs, container: 0)
                .Heading(1, "Guide", "Install it.")
                .Control("textbox", "Search", "old")
                .Control("button", "Go");
            browser.Pages[Docs] = new FakePage { Title = "Docs" }.Heading(1, "Docs", "All of it.");
        },
    };

    internal static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal Task<BrowserSession> OpenAsync(FirefoxToolOptions? options = null) =>
        BrowserSession.OpenAsync(Launcher, BrowserProfileChoice.Disposable, options ?? new FirefoxToolOptions(), new FakeFileSystemService(), Timings, logger: null, Ct);

    internal BrowserSessionManager Manager(FirefoxToolOptions? options = null) =>
        new(Launcher, options ?? new FirefoxToolOptions(), new FakeFileSystemService(), logger: null, Timings);
}
