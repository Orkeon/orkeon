using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// STUDIO-73: the agents and tasks of a YAML crew that name their own model, which the choice of a
/// team's model does not reach — counted from the files, never refused.
/// </summary>
public sealed class CrewLlmDeclarationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-crew-llm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private static int Count(string yaml) => CrewLlmDeclarations.Count(yaml.Split('\n'));

    [Fact]
    public void An_agent_block_that_pins_a_profile_or_a_model_is_counted()
    {
        Assert.Equal(2, Count("""
            agents:
              - name: writer
                llm:
                  profile: fast
              - name: reviewer
                llm:
                  provider: openai
                  model: gpt-5
            """));
    }

    [Fact]
    public void The_default_profile_and_an_empty_block_name_nothing()
    {
        Assert.Equal(0, Count("""
            agents:
              - name: writer
                llm:
                  profile: default
              - name: reviewer
                llm:
                # nothing set
              - name: editor
                role: edits
            """));
    }

    [Fact]
    public void Inline_forms_and_task_overrides_are_counted()
    {
        Assert.Equal(1, Count("llm: writer"));
        Assert.Equal(0, Count("llm: \"default\""));
        Assert.Equal(1, Count("llm: { profile: fast }"));
        Assert.Equal(1, Count("""
            tasks:
              - id: draft
                llm_override:
                  profile: careful
              - id: polish
                llm_override:
                  temperature: 0.2
            """));
    }

    [Fact]
    public void A_team_folder_is_read_through_its_crew_agents_and_tasks()
    {
        var crew = Path.Combine(_root, "team", "crew");
        Directory.CreateDirectory(Path.Combine(crew, "agents"));
        Directory.CreateDirectory(Path.Combine(crew, "tasks"));
        File.WriteAllText(Path.Combine(crew, "crew.yaml"), "name: veille\n");
        File.WriteAllText(Path.Combine(crew, "agents", "writer.yaml"), "name: writer\nllm:\n  model: gpt-5\n");
        File.WriteAllText(Path.Combine(crew, "tasks", "draft.yml"), "id: draft\nllm_override:\n  profile: careful\n");
        File.WriteAllText(Path.Combine(crew, "agents", "notes.md"), "llm: writer\n");

        Assert.Equal(2, CrewLlmDeclarations.CountIn(Path.Combine(_root, "team")));
        Assert.Equal(1, CrewLlmDeclarations.CountIn(Path.Combine(crew, "agents", "writer.yaml")));
    }

    [Fact]
    public void A_script_a_missing_path_or_nothing_counts_zero()
    {
        Directory.CreateDirectory(_root);
        var script = Path.Combine(_root, "crew.ork.ts");
        File.WriteAllText(script, "llm: writer\n");

        Assert.Equal(0, CrewLlmDeclarations.CountIn(script));
        Assert.Equal(0, CrewLlmDeclarations.CountIn(Path.Combine(_root, "absent")));
        Assert.Equal(0, CrewLlmDeclarations.CountIn(null));
    }
}
