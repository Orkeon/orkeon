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
        Assert.Equal([ToolCatalog.TavilyKeyEnv, ToolCatalog.BraveKeyEnv], tools.Secrets.Select(s => s.EnvName));
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
        Assert.Equal("the key is given at the call, by the agent", Assert.Single(web.Requirements).Text);

        var data = Assert.Single(tools.Families, f => f.Key == ToolCatalog.DataFamily);
        Assert.All(data.Requirements, r => Assert.Equal("the connection parameters are given at the call, by the agent", r.Text));

        var code = Assert.Single(tools.Families, f => f.Key == ToolCatalog.CodeFamily);
        Assert.Equal("expert setting below: Orkeon:Tools:Shell", Assert.Single(code.Requirements).Text);

        var events = Assert.Single(tools.Families, f => f.Key == ToolCatalog.EventsFamily);
        Assert.False(events.HasRequirements);
        Assert.Equal("7 tools, nothing to configure", events.QuietLine);
    }
}
