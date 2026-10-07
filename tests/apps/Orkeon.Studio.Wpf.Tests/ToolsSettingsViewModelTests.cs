using Orkeon.Studio.Core.Tools;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-21 — the Tools tab: one key row per key a tool needs, on the same store as the
/// profile keys, and the catalogue by family with what each tool needs said in the user's
/// words.
/// </summary>
public sealed class ToolsSettingsViewModelTests
{
    [Fact]
    public void One_row_per_tool_key_and_storing_lands_in_the_environment_and_wipes_the_field()
    {
        var store = new FakeApiKeyStore();
        var tools = new ToolsSettingsViewModel(store);

        Assert.True(tools.HasSecrets);
        Assert.Equal([ToolCatalog.TavilyKeyEnv, ToolCatalog.BraveKeyEnv, ToolCatalog.OpenAiImageKeyEnv], tools.Secrets.Select(s => s.EnvName));
        var tavily = tools.Secrets[0];
        Assert.Equal("web_search", tavily.UsedBy);
        Assert.True(tavily.HasConsoleUrl);
        Assert.False(tavily.HasKey);
        Assert.Equal("no key detected", tavily.StatusText);

        tavily.KeyInput = " tvly-abc ";
        Assert.True(tavily.StoreCommand.CanExecute(null));
        tavily.StoreCommand.Execute(null);

        Assert.Equal("tvly-abc", store.Saved[ToolCatalog.TavilyKeyEnv]);
        Assert.Equal("", tavily.KeyInput);
        Assert.True(tavily.HasKey);
        Assert.Equal("key remembered", tavily.StatusText);
    }

    [Fact]
    public void The_catalogue_lists_every_family_with_its_tools_and_says_what_the_needy_ones_need()
    {
        var tools = new ToolsSettingsViewModel(new FakeApiKeyStore());

        Assert.Equal(ToolCatalog.Families.Count, tools.Families.Count);
        Assert.Equal(ToolCatalog.All.Count(), tools.ToolCount);

        var search = Assert.Single(tools.Families, f => f.Key == ToolCatalog.SearchFamily);
        Assert.Equal("Search and knowledge", search.Label);
        Assert.True(search.HasRequirements);
        Assert.Contains(search.Tools, t => t.Name == "web_search" && t.NeedsSomething);
        Assert.Contains(search.Tools, t => t.Name == "cache_search" && !t.NeedsSomething);
        var webSearch = Assert.Single(search.Requirements, r => r.Name == "web_search");
        Assert.Equal("needs the key ORKEON_TAVILY_API_KEY, above", webSearch.Text);
        Assert.True(webSearch.IsKey);
        var brave = Assert.Single(search.Requirements, r => r.Name == "brave_search");
        Assert.Equal("present only once the key BRAVE_API_KEY is remembered, above", brave.Text);

        var web = Assert.Single(tools.Families, f => f.Key == ToolCatalog.WebFamily);
        var imageGeneration = Assert.Single(web.Requirements);
        Assert.Equal("image_generation", imageGeneration.Name);
        Assert.Equal($"needs the key {ToolCatalog.OpenAiImageKeyEnv}, above", imageGeneration.Text);
        Assert.True(imageGeneration.IsKey);

        var data = Assert.Single(tools.Families, f => f.Key == ToolCatalog.DataFamily);
        Assert.All(data.Requirements, r => Assert.Equal("the connection parameters are given at the call, by the agent", r.Text));

        var code = Assert.Single(tools.Families, f => f.Key == ToolCatalog.CodeFamily);
        Assert.Equal("expert setting below: Orkeon:Tools:Shell", Assert.Single(code.Requirements).Text);

        var events = Assert.Single(tools.Families, f => f.Key == ToolCatalog.EventsFamily);
        Assert.False(events.HasRequirements);
        Assert.Equal("7 tools, nothing to configure", events.QuietLine);
    }

    /// <summary>
    /// STUDIO-67 — an e-mail account is declared in Settings › E-mail, so the line sends the
    /// reader to that tab: neither to the settings file nor to a command line. It is not a key of
    /// the card above.
    /// </summary>
    [Fact]
    public void The_email_family_says_its_mailbox_tools_need_an_account_declared_in_the_email_tab()
    {
        var tools = new ToolsSettingsViewModel(new FakeApiKeyStore());

        var email = Assert.Single(tools.Families, f => f.Key == ToolCatalog.EmailFamily);
        Assert.Equal("E-mail", email.Label);
        Assert.Equal(13, email.Tools.Count);
        Assert.Equal(12, email.Requirements.Count);
        Assert.All(email.Requirements, r =>
        {
            Assert.Equal("needs an e-mail account, declared in Settings › E-mail", r.Text);
            Assert.DoesNotContain("Orkeon:Tools", r.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("orkeon email", r.Text, StringComparison.Ordinal);
            Assert.False(r.IsKey);
        });
        Assert.Contains(email.Tools, t => t.Name == "email_parser" && !t.NeedsSomething);
        Assert.DoesNotContain(email.Requirements, r => r.Name == "email_parser");
    }

    /// <summary>STUDIO-67 — the line names the tab the way each language's tab strip does.</summary>
    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("zh-Hans")]
    public void The_email_line_names_the_tab_as_the_tab_strip_spells_it(string culture)
    {
        var strings = new FakeResxStudioStrings(culture);
        var tools = new ToolsSettingsViewModel(new FakeApiKeyStore(), strings);

        var email = Assert.Single(tools.Families, f => f.Key == ToolCatalog.EmailFamily);

        Assert.All(email.Requirements, r =>
        {
            Assert.Contains(strings["Studio.Shell.Mails"], r.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("orkeon email", r.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", r.Text, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A family the label switch forgets must not pass for another one: it shows its raw key,
    /// and this is what fails on it.
    /// </summary>
    [Fact]
    public void Every_family_wears_a_label_of_its_own()
    {
        var tools = new ToolsSettingsViewModel(new FakeApiKeyStore());

        Assert.All(tools.Families, f => Assert.NotEqual(f.Key, f.Label));
        Assert.Equal(tools.Families.Count, tools.Families.Select(f => f.Label).Distinct(StringComparer.Ordinal).Count());
    }
}
