using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class FirefoxProfilePolicyTests
{
    private static FirefoxProfilePolicy Policy(string? profile = null, string[]? allowed = null, string[]? denied = null)
    {
        var options = new FirefoxToolOptions { Profile = profile };
        foreach (var name in allowed ?? [])
            options.AllowedProfiles.Add(name);
        foreach (var name in denied ?? [])
            options.DeniedProfiles.Add(name);
        return new FirefoxProfilePolicy(options);
    }

    [Fact]
    public void Should_GiveADisposableProfile_When_NothingIsConfiguredAndNothingAsked()
    {
        var policy = Policy();

        Assert.True(policy.Default.IsDisposable);
        Assert.True(policy.Resolve(null).IsDisposable);
        Assert.True(policy.Resolve(" ").IsDisposable);
        Assert.True(policy.Resolve("Disposable").IsDisposable);
    }

    [Fact]
    public void Should_GiveTheConfiguredProfile_When_NothingIsAsked()
    {
        var policy = Policy(profile: "work");

        Assert.Equal("work", policy.Resolve(null).Name);
        Assert.True(policy.Resolve(" Work ").Same(policy.Default));
        Assert.True(policy.Resolve("disposable").IsDisposable);
    }

    [Fact]
    public void Should_RefuseAProfileTheHostDoesNotList_AndNameTheOnesItDoes()
    {
        var policy = Policy(profile: "work", allowed: ["research"]);

        var error = Assert.Throws<FirefoxException>(() => policy.Resolve("default"));
        var research = policy.Resolve("RESEARCH");

        Assert.Equal("'default' is not a profile this host lets an agent name (Orkeon:Tools:Firefox:AllowedProfiles): disposable, work, research.", error.Message);
        Assert.Equal("RESEARCH", research.Name);
        Assert.True(research.Same(new FirefoxProfileChoice("research")));
    }

    [Fact]
    public void Should_RefuseADeniedProfile_EvenAsTheDefaultOrAnAllowedOne()
    {
        var policy = Policy(profile: "default", allowed: ["work"], denied: ["Default", "work"]);

        var asDefault = Assert.Throws<FirefoxException>(() => policy.Resolve(null));
        var asAllowed = Assert.Throws<FirefoxException>(() => policy.Resolve("work"));

        Assert.Equal("'default' is a profile this host forbids (Orkeon:Tools:Firefox:DeniedProfiles).", asDefault.Message);
        Assert.Contains("'work' is a profile this host forbids", asAllowed.Message, StringComparison.Ordinal);
        Assert.True(policy.Resolve("disposable").IsDisposable);
    }
}
