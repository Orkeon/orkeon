using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Which language the app starts in, and what is written down (30/08 mock, T-13/T-14).
/// The rule that matters is the negative one: a DETECTED language is never persisted, so a
/// user who changes their Windows language is still followed on the next start.
/// </summary>
public sealed class LanguageSelectionTests
{
    private static (LanguageSelectorViewModel Vm, List<string> Applied, List<string> Persisted) Build(
        string? stored = null, string? system = null)
    {
        List<string> applied = [], persisted = [];
        var vm = new LanguageSelectorViewModel(stored, system, applied.Add, persisted.Add);
        return (vm, applied, persisted);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    [InlineData("zh")]
    [InlineData("en")]
    public void A_supported_system_language_is_the_one_the_app_speaks(string system)
    {
        var (vm, applied, persisted) = Build(system: system);

        Assert.Equal(system, vm.Current);
        Assert.Equal([system], applied);
        Assert.False(vm.IsExplicitChoice);

        // The detected language is applied, and NOT written down.
        Assert.Empty(persisted);
    }

    [Theory]
    [InlineData("it")]
    [InlineData("pt")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unsupported_system_language_falls_back_to_english(string? system)
    {
        var (vm, _, persisted) = Build(system: system);

        Assert.Equal("en", vm.Current);
        Assert.False(vm.IsExplicitChoice);
        Assert.Empty(persisted);
    }

    [Fact]
    public void A_stored_choice_wins_over_the_machine()
    {
        var (vm, applied, _) = Build(stored: "de", system: "fr");

        Assert.Equal("de", vm.Current);
        Assert.Equal(["de"], applied);
        Assert.True(vm.IsExplicitChoice);
    }

    [Fact]
    public void Picking_a_language_applies_it_and_records_it()
    {
        var (vm, applied, persisted) = Build(system: "fr");

        vm.PickCommand.Execute("zh");

        Assert.Equal("zh", vm.Current);
        Assert.True(vm.IsExplicitChoice);
        Assert.Equal(["fr", "zh"], applied);
        Assert.Equal(["zh"], persisted);
    }

    [Fact]
    public void The_menu_lists_the_other_languages_only()
    {
        var (vm, _, _) = Build(system: "fr");

        Assert.Equal(LanguageSelectorViewModel.Supported.Count - 1, vm.Others.Count);
        Assert.DoesNotContain(vm.Others, l => l.Code == "fr");

        vm.PickCommand.Execute("de");

        Assert.DoesNotContain(vm.Others, l => l.Code == "de");
        Assert.Contains(vm.Others, l => l.Code == "fr");
    }

    [Fact]
    public void Picking_the_language_already_in_force_records_nothing()
    {
        var (vm, applied, persisted) = Build(system: "fr");

        vm.PickCommand.Execute("fr");

        Assert.Single(applied);     // the startup one only
        Assert.Empty(persisted);
        Assert.False(vm.IsExplicitChoice);
    }

    [Fact]
    public void An_unsupported_pick_changes_nothing()
    {
        var (vm, _, persisted) = Build(system: "fr");

        vm.PickCommand.Execute("it");

        Assert.Equal("fr", vm.Current);
        Assert.Empty(persisted);
    }

    [Fact]
    public void The_menu_closes_when_a_language_is_picked()
    {
        var (vm, _, _) = Build(system: "fr");
        vm.ToggleMenuCommand.Execute(null);
        Assert.True(vm.IsMenuOpen);

        vm.PickCommand.Execute("es");

        Assert.False(vm.IsMenuOpen);
    }

    [Fact]
    public void A_region_tagged_system_language_is_read_as_its_language()
    {
        // "fr-CA", "zh-Hans-CN" — the OS gives a tag, the catalogue has a language.
        Assert.Equal("fr", Build(system: "fr-CA").Vm.Current);
        Assert.Equal("zh", Build(system: "zh-Hans").Vm.Current);
    }

    [Fact]
    public void Every_supported_language_names_itself_in_its_own_script()
    {
        Assert.Equal(
            ["Français", "English", "Español", "Deutsch", "中文"],
            LanguageSelectorViewModel.Supported.Select(l => l.Name));
    }
}
