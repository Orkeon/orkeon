using Microsoft.Extensions.Configuration;
using Orkeon.Hosting;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// The part of the settings catalogue that says what the <c>orkeon</c> binary reads is produced,
/// and held here to the host <c>orkeon run</c> composes: the runner host and what the CLI adds to
/// it. A section the CLI starts or stops reading fails this test until the file is written again.
/// </summary>
public sealed class SettingsCatalogFileTests
{
    private const string File = $"{SettingsCatalogFiles.RepositoryDirectory}/{SettingsHosts.Cli}.json";

    [Fact]
    public void The_file_of_the_cli_is_what_the_host_of_a_run_declares()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(SettingsCatalogBuilder.EverySwitchOn).Build();
        var services = RunnerHost.Compose(configuration, (_, added) =>
        {
            added.AddOrkeonHumanInput();
            added.AddSemanticSearchTool();
            RunCommand.AddCliRagServices(added);
        });

        var described = SettingsCatalogBuilder.Describe(services, SettingsDocumentation.From(ProducedFile.Documentation()));
        var library = SettingsCatalog.FromJson(
            ProducedFile.Read($"{SettingsCatalogFiles.RepositoryDirectory}/{SettingsCatalogFiles.Library}")
            ?? throw new InvalidOperationException("The library's file of the settings catalogue is missing."));

        Assert.Empty(SettingsHostCatalog.KeysTheLibraryLacks(described, library));
        ProducedFile.AssertCurrent(
            File,
            SettingsHostCatalog.Of(SettingsHosts.Cli, described, library).ToJson(),
            SettingsCatalogFiles.Regenerate);
    }
}
