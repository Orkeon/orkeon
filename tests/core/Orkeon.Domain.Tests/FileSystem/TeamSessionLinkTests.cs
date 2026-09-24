using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// Rule R (STUDIO-25): the one rule that decides whether a team folder is linked to the forge
/// session its <c>forge.json</c> names. The path alone could not tell a moved original from a
/// copy, and the id alone could not tell a copy from its original; together they can. Pure:
/// every folder here is a path nobody created, and the ids other folders carry are a table.
/// </summary>
public sealed class TeamSessionLinkTests
{
    private static readonly Guid SessionId = Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f");

    private static readonly string Teams = Path.Combine(Path.GetTempPath(), "orkeon-rule-r", "teams");

    private static readonly string Original = Path.Combine(Teams, "veille");

    private static readonly string Copy = Path.Combine(Teams, "veille-copie");

    /// <summary>The ids the folders of the machine carry, as their forge.json would say.</summary>
    private static Func<string, Guid?> Carrying(params (string Folder, Guid Id)[] records) =>
        folder => records.Where(r => string.Equals(r.Folder, folder, StringComparison.Ordinal))
            .Select(r => (Guid?)r.Id)
            .FirstOrDefault();

    private static readonly Func<string, Guid?> NothingElsewhere = _ => null;

    [Fact]
    public void The_folder_the_session_promoted_to_is_linked()
    {
        var kind = TeamSessionLink.Resolve(
            Original, SessionId, new SessionPromotion(SessionId, Original), NothingElsewhere);

        Assert.Equal(TeamSessionLinkKind.Linked, kind);
        Assert.True(TeamSessionLink.IsLinked(kind));
    }

    /// <summary>
    /// Full-path and trailing-separator-blind, like every comparison of promotedTo before it: a
    /// session that recorded the folder with a final separator, or through a detour, still
    /// designates it.
    /// </summary>
    [Fact]
    public void A_trailing_separator_or_a_detour_still_designates_the_folder()
    {
        var spelledWithASeparator = new SessionPromotion(SessionId, Original + Path.DirectorySeparatorChar);
        var spelledWithADetour = new SessionPromotion(SessionId, Path.Combine(Teams, "autre", "..", "veille"));

        Assert.Equal(TeamSessionLinkKind.Linked, TeamSessionLink.Resolve(Original, SessionId, spelledWithASeparator, NothingElsewhere));
        Assert.Equal(TeamSessionLinkKind.Linked, TeamSessionLink.Resolve(Original, SessionId, spelledWithADetour, NothingElsewhere));
    }

    /// <summary>
    /// Case 2, the defect this rule exists for: a duplicate carries the original's id, and the
    /// original is still where the session says. The duplicate is a copy — linked to nothing —
    /// so it can never modify the original's session.
    /// </summary>
    [Fact]
    public void A_copy_is_not_linked_while_its_original_still_carries_the_id()
    {
        var kind = TeamSessionLink.Resolve(
            Copy, SessionId, new SessionPromotion(SessionId, Original), Carrying((Original, SessionId)));

        Assert.Equal(TeamSessionLinkKind.Copy, kind);
        Assert.False(TeamSessionLink.IsLinked(kind));
    }

    /// <summary>
    /// Case 3: the folder the session names is gone, and this one carries its id — it is the
    /// original, moved or renamed. Linked, and the session is to follow it.
    /// </summary>
    [Fact]
    public void The_original_moved_away_is_linked_and_its_session_is_to_follow_it()
    {
        var moved = Path.Combine(Teams, "veille-renommee");

        var kind = TeamSessionLink.Resolve(
            moved, SessionId, new SessionPromotion(SessionId, Original), NothingElsewhere);

        Assert.Equal(TeamSessionLinkKind.Moved, kind);
        Assert.True(TeamSessionLink.IsLinked(kind));
    }

    /// <summary>
    /// «Otherwise» is the whole complement of cases 1 and 2: a session that recorded no folder,
    /// or whose folder now holds a team carrying another id, leaves the folder with its id as
    /// the original.
    /// </summary>
    [Fact]
    public void Without_a_folder_still_carrying_the_id_elsewhere_the_folder_is_the_moved_original()
    {
        var otherTeam = Guid.Parse("0b9e8d7c-6a5f-4e3d-8c2b-1a0f9e8d7c6b");

        var neverRecorded = TeamSessionLink.Resolve(
            Original, SessionId, new SessionPromotion(SessionId, PromotedTo: null), NothingElsewhere);
        var takenOver = TeamSessionLink.Resolve(
            Copy, SessionId, new SessionPromotion(SessionId, Original), Carrying((Original, otherTeam)));

        Assert.Equal(TeamSessionLinkKind.Moved, neverRecorded);
        Assert.Equal(TeamSessionLinkKind.Moved, takenOver);
    }

    /// <summary>No forge.json, or one without an id: nothing links the folder, whatever session exists.</summary>
    [Fact]
    public void A_folder_without_an_id_is_linked_to_nothing()
    {
        var session = new SessionPromotion(SessionId, Original);

        var withoutRecord = TeamSessionLink.Resolve(Original, teamSessionId: null, session, NothingElsewhere);
        var withTheEmptyId = TeamSessionLink.Resolve(Original, Guid.Empty, new SessionPromotion(Guid.Empty, Original), NothingElsewhere);

        Assert.Equal(TeamSessionLinkKind.NoIdentity, withoutRecord);
        Assert.Equal(TeamSessionLinkKind.NoIdentity, withTheEmptyId);
        Assert.False(TeamSessionLink.IsLinked(withoutRecord));
    }

    /// <summary>
    /// An id no session carries — the session was deleted, or the team was forged on another
    /// machine — links to nothing, and neither does a session that carries another id: the path
    /// it names is not enough on its own.
    /// </summary>
    [Fact]
    public void An_id_no_session_carries_is_linked_to_nothing()
    {
        var anotherSession = new SessionPromotion(Guid.Parse("0b9e8d7c-6a5f-4e3d-8c2b-1a0f9e8d7c6b"), Original);

        var none = TeamSessionLink.Resolve(Original, SessionId, session: null, NothingElsewhere);
        var another = TeamSessionLink.Resolve(Original, SessionId, anotherSession, NothingElsewhere);

        Assert.Equal(TeamSessionLinkKind.NoSession, none);
        Assert.Equal(TeamSessionLinkKind.NoSession, another);
        Assert.False(TeamSessionLink.IsLinked(another));
    }

    /// <summary>
    /// The rule reads another folder only when it has to — when the session names a folder that
    /// is not this one. Every other verdict is taken on what the caller already read.
    /// </summary>
    [Fact]
    public void Another_folder_is_read_only_when_the_session_names_one()
    {
        var reads = new List<string>();
        Guid? Record(string folder)
        {
            reads.Add(folder);
            return null;
        }

        TeamSessionLink.Resolve(Original, SessionId, new SessionPromotion(SessionId, Original), Record);
        TeamSessionLink.Resolve(Original, teamSessionId: null, new SessionPromotion(SessionId, Copy), Record);
        TeamSessionLink.Resolve(Original, SessionId, session: null, Record);
        TeamSessionLink.Resolve(Original, SessionId, new SessionPromotion(SessionId, PromotedTo: null), Record);
        Assert.Empty(reads);

        TeamSessionLink.Resolve(Copy, SessionId, new SessionPromotion(SessionId, Original), Record);
        Assert.Equal([Original], reads);
    }

    /// <summary>A promotedTo that is no path at all is a folder nobody can find — never an exception.</summary>
    [Fact]
    public void A_promoted_to_that_is_no_path_never_takes_the_rule_down()
    {
        var kind = TeamSessionLink.Resolve(
            Original, SessionId, new SessionPromotion(SessionId, "\0"), NothingElsewhere);

        Assert.Equal(TeamSessionLinkKind.Moved, kind);
    }
}
