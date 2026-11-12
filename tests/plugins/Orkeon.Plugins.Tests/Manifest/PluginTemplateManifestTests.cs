using Orkeon.Plugins;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Plugins.Tests.Manifest;

/// <summary>
/// The manifest that <c>dotnet new orkeon-plugin</c> writes is accepted by the host of this build:
/// a version bump that leaves the template's <c>orkeonVersion</c> range behind fails here instead
/// of in the first plugin a user creates.
/// </summary>
public class PluginTemplateManifestTests
{
    private const string TemplateManifest = "src/packaging/Orkeon.Templates/templates/orkeon-plugin/plugin.json";

    [Fact]
    public void ShouldBeAccepted_WhenTheHostOfThisBuildValidatesIt()
    {
        var json = File.ReadAllText(Path.Combine(ProducedFile.RepositoryRoot(), TemplateManifest));

        var manifest = PluginManifest.Parse(json);

        PluginManifestValidator.Validate(manifest, PluginHostVersion.Current);
    }
}
