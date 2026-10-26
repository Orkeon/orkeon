using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// Refonte lot 12: the agents of a YAML crew, by role, read from the files before the run so the
/// Launch screen can give each its column — a listing that never refuses.
/// </summary>
public sealed class CrewAgentRolesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-crew-roles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void The_roles_of_a_single_file_crew_come_unquoted_and_without_comments()
    {
        var roles = CrewAgentRoles.Of("""
            agents:
              - role: Researcher
                goal: find
              - role: "Writer"   # drafts the report
              - name: reviewer
                role: 'Reviewer'
            """.Split('\n'));

        Assert.Equal(["Researcher", "Writer", "Reviewer"], roles);
    }

    [Fact]
    public void A_folder_crew_lists_its_agents_once_and_skips_its_tasks()
    {
        Directory.CreateDirectory(Path.Combine(_root, "agents"));
        Directory.CreateDirectory(Path.Combine(_root, "tasks"));
        File.WriteAllText(Path.Combine(_root, "agents", "a.yaml"), "role: Analyst\n");
        File.WriteAllText(Path.Combine(_root, "agents", "b.yaml"), "role: analyst\n");
        File.WriteAllText(Path.Combine(_root, "tasks", "t.yaml"), "role: Not an agent\n");

        Assert.Equal(["Analyst"], CrewAgentRoles.In(_root));
    }

    [Fact]
    public void Nothing_to_read_lists_nothing()
    {
        Assert.Empty(CrewAgentRoles.In(null));
        Assert.Empty(CrewAgentRoles.In(Path.Combine(_root, "missing")));
    }
}
