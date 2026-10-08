using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Configuration;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Hosting;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// The part of the settings catalogue that says what <c>orkeon-repl</c> reads is produced, and held
/// here to the container <c>Program</c> composes — its own, not the runner host's. Its own section,
/// <c>Orkeon:Cli:Tui</c>, is described from this binary's type. A section the console starts or
/// stops reading fails this test until the file is written again.
/// </summary>
public sealed class SettingsCatalogFileTests
{
    private const string File = $"{SettingsCatalogFiles.RepositoryDirectory}/{SettingsHosts.Repl}.json";

    [Fact]
    public void The_file_of_the_console_is_what_its_container_declares()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(SettingsCatalogBuilder.EverySwitchOn).Build();
        var services = new ServiceCollection();
        Program.ConfigureServices(
            RunnerHost.CompositionContext(configuration),
            services,
            UiMode.Plain,
            ScriptedCommandsCliOptions.Parse([]),
            replWordWrap: false);

        var described = SettingsCatalogBuilder.Describe(services, SettingsDocumentation.From(ProducedFile.Documentation()));
        var library = SettingsCatalog.FromJson(
            ProducedFile.Read($"{SettingsCatalogFiles.RepositoryDirectory}/{SettingsCatalogFiles.Library}")
            ?? throw new InvalidOperationException("The library's file of the settings catalogue is missing."));

        Assert.Empty(SettingsHostCatalog.KeysTheLibraryLacks(described, library));
        ProducedFile.AssertCurrent(
            File,
            SettingsHostCatalog.Of(SettingsHosts.Repl, described, library).ToJson(),
            SettingsCatalogFiles.Regenerate);
    }
}
