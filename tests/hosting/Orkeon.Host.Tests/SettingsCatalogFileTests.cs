using Microsoft.Extensions.Configuration;
using Orkeon.Hosting;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Host.Tests;

/// <summary>
/// The part of the settings catalogue that says what <c>orkeon-host</c> reads is produced, and
/// held here to the host the daemon composes: the runner host and
/// <see cref="HostServiceRegistration.AddHostServices"/>, the A2A server turned on. Its own sections
/// — <c>Orkeon:Host</c>, <c>Orkeon:Host:Discord</c> — are described from this binary's types, which
/// no other assembly sees. A key added to them fails this test until the file is written again.
/// </summary>
public sealed class SettingsCatalogFileTests
{
    private const string File = $"{SettingsCatalogFiles.RepositoryDirectory}/{SettingsHosts.ServiceHost}.json";

    [Fact]
    public void The_file_of_the_service_host_is_what_the_daemon_declares()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(SettingsCatalogBuilder.EverySwitchOn).Build();
        var services = RunnerHost.Compose(
            configuration,
            (context, added) => added.AddHostServices(context.Configuration, HostCrewMounts.For([])));

        var described = SettingsCatalogBuilder.Describe(services, SettingsDocumentation.From(ProducedFile.Documentation()));
        var library = SettingsCatalog.FromJson(
            ProducedFile.Read($"{SettingsCatalogFiles.RepositoryDirectory}/{SettingsCatalogFiles.Library}")
            ?? throw new InvalidOperationException("The library's file of the settings catalogue is missing."));

        Assert.Empty(SettingsHostCatalog.KeysTheLibraryLacks(described, library));
        ProducedFile.AssertCurrent(
            File,
            SettingsHostCatalog.Of(SettingsHosts.ServiceHost, described, library).ToJson(),
            SettingsCatalogFiles.Regenerate);
    }
}
