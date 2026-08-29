using Orkeon.Studio.Core.Forge;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// The generated-definition card shows the rendered YAML itself (v3 W-06):
/// config first, then agents, then tasks, each part named by a mono comment.
/// </summary>
public sealed class ForgeRenderReaderTests : IDisposable
{
    private readonly string _session =
        Path.Combine(Path.GetTempPath(), "orkeon-render-reader-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_session))
            Directory.Delete(_session, recursive: true);
    }

    [Fact]
    public void Concatenates_config_then_agents_then_tasks_with_named_separators()
    {
        var crew = Path.Combine(_session, "crew");
        Directory.CreateDirectory(Path.Combine(crew, "agents"));
        Directory.CreateDirectory(Path.Combine(crew, "tasks"));
        File.WriteAllText(Path.Combine(crew, "config.yaml"), "name: veille\n");
        File.WriteAllText(Path.Combine(crew, "agents", "collecteur.yaml"), "role: Lecteur\n");
        File.WriteAllText(Path.Combine(crew, "tasks", "extraire.yaml"), "agent: collecteur\n");

        var definition = ForgeRenderReader.ReadDefinition(_session);

        Assert.Equal(
            "# --- crew/config.yaml\nname: veille\n\n" +
            "# --- crew/agents/collecteur.yaml\nrole: Lecteur\n\n" +
            "# --- crew/tasks/extraire.yaml\nagent: collecteur\n",
            definition);
    }

    [Fact]
    public void A_session_without_a_render_yields_an_empty_definition()
    {
        Directory.CreateDirectory(_session);

        Assert.Equal("", ForgeRenderReader.ReadDefinition(_session));
        Assert.Equal("", ForgeRenderReader.ReadDefinition(""));
    }
}
