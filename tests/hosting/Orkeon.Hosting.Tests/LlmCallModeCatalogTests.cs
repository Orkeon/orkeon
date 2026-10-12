namespace Orkeon.Hosting.Tests;

/// <summary>BR-05: the library catalogue lists the call mode of the <c>Llm</c> section and of its profiles.</summary>
public sealed class LlmCallModeCatalogTests
{
    [Theory]
    [Trait("BR", "BR-05")]
    [InlineData("Llm:CallMode")]
    [InlineData("Llm:Profiles:<name>:CallMode")]
    public void ShouldListCallModeInTheSettingsCatalogue(string path)
    {
        var entry = SettingsLibraryProducer.Catalog.Setting(path);

        Assert.NotNull(entry);
        Assert.Equal("string", entry.Type);
        Assert.Equal("\"Auto\"", entry.Default);
    }
}
