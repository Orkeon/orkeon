using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The library's file of the settings catalogue is produced, and held here to what the code
/// declares: a key added to an options type, a default that changes or a comment that is reworded
/// fails this test until the file is written again.
/// </summary>
public sealed class SettingsCatalogFileTests
{
    [Fact]
    public void The_library_file_is_what_the_public_registrations_and_the_runner_host_declare() =>
        ProducedFile.AssertCurrent(
            SettingsLibraryProducer.File,
            SettingsLibraryProducer.Catalog.ToJson(),
            SettingsCatalogFiles.Regenerate);

    [Fact]
    public void Every_public_registration_is_called_or_listed()
    {
        var notCalled = SettingsLibraryProducer.NotCalled;

        Assert.True(
            s_notCalled.SequenceEqual(notCalled, StringComparer.Ordinal),
            "The public registrations the catalogue cannot call have changed. Not called now:\n" + string.Join("\n", notCalled));
    }

    /// <summary>
    /// The public registrations the producer does not call, so whose sections the catalogue would
    /// miss if they declared any: none of them does. A registration that joins this list is looked
    /// at once — does it read a section? — and listed here, or described in the producer.
    /// </summary>
    private static readonly string[] s_notCalled =
    [
        // Generic: registers a type the caller names, reads no setting.
        "EventHubServiceCollectionExtensions.AddEventHubMiddleware<TMiddleware>(): generic",
        "HumanInputExtensions.AddOrkeonHumanInput<TProvider>(): generic",
        // Refuses to run before AddOrkeonRag, which declares its section (Orkeon:Rag:Retrieval:Hybrid).
        "HybridRetrievalExtensions.AddOrkeonHybridRetrieval(IConfiguration, Func`2): threw InvalidOperationException",
        // Take a hook, a file system or options built in code: no section of their own —
        // but Plugins, which AddOrkeonPlugins binds and the producer describes by its options type.
        "KickoffHookExtensions.AddAfterKickoffHook(AfterKickoffHook): a parameter the producer cannot supply",
        "KickoffHookExtensions.AddBeforeKickoffHook(BeforeKickoffHook): a parameter the producer cannot supply",
        "OrkeonPluginsServiceCollectionExtensions.AddOrkeonPlugins(IFileSystemService, Action`1): a parameter the producer cannot supply",
        "OrkeonPluginsServiceCollectionExtensions.AddOrkeonPlugins(IFileSystemService, IConfiguration): a parameter the producer cannot supply",
        "RaggableTreeInfrastructureExtensions.AddRaggableTreeWithLogging(RaggableTreeOptions): a parameter the producer cannot supply",
        // The declaring of a section itself.
        "SettingsDeclarationExtensions.DeclareSettingsShape(String, Type): a parameter the producer cannot supply",
        "SettingsDeclarationExtensions.IsDeclared(String, Type): a parameter the producer cannot supply",
        "SettingsServiceCollectionExtensions.AddOrkeonSettings<TOptions>(String): generic",
    ];
}
