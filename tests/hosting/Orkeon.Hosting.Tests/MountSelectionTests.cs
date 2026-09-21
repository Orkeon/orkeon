using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The decision table of <see cref="MountSelection"/> (VFS-90): which settings entries a run
/// keeps when several declare one virtual root, and the one-line refusal for every shape that
/// cannot be resolved. Pure — no console, no disk, no host — so every branch is a row here and
/// the guards and the host only have to apply the plan.
/// </summary>
public sealed class MountSelectionTests
{
    private const string Settings = "/etc/orkeon/appsettings.json";

    private static readonly MountId A = MountId.Create();
    private static readonly MountId B = MountId.Create();
    private static readonly MountId C = MountId.Create();

    private static DeclaredMountEntry Entry(int index, string physical, string root, string rights = "rw", MountId? id = null) =>
        new(index, id is null ? $"{physical}:{root}:{rights}" : $"{id}|{physical}:{root}:{rights}");

    private static MountSelectionPlan Resolve(
        IReadOnlyList<DeclaredMountEntry> declared,
        string[]? cli = null,
        MountId[]? ids = null,
        string[]? crew = null) =>
        MountSelection.Resolve(
            declared,
            cli ?? [],
            ids ?? [],
            (crew ?? []).Select(MountReference.Parse).ToList(),
            Settings);

    [Fact]
    public void A_root_declared_once_is_mounted_as_it_is_and_nothing_is_decided()
    {
        var plan = Resolve([Entry(0, "/srv/data", "/data", "ro"), Entry(1, "/srv/out", "/output")]);

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Warnings);
        Assert.Empty(plan.Selected);
        Assert.Empty(plan.Withdrawn);
    }

    [Fact]
    public void An_id_declared_twice_is_refused()
    {
        var errors = MountSelection.ValidateDeclared(
            [Entry(0, "/srv/a", "/a", id: A), Entry(1, "/srv/b", "/b", id: A)], Settings);

        var error = Assert.Single(errors);
        Assert.Equal(
            $"mount id {A} is declared twice in {Settings}: {A}|/srv/a:/a:rw and {A}|/srv/b:/b:rw. An id names one entry.",
            error);
    }

    [Fact]
    public void A_root_declared_twice_with_an_entry_that_has_no_id_is_refused()
    {
        var errors = MountSelection.ValidateDeclared(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output")], Settings);

        var error = Assert.Single(errors);
        Assert.Equal(
            $"'/output' is declared twice in {Settings} ({A}|/srv/a:/output:rw and /srv/b:/output:rw) and "
            + "'/srv/b:/output:rw' has no id. Give every entry an id (<ulid>|<physical>:/output:<rights>; "
            + "Studio > Allowed folders writes one on save) or keep one.",
            error);
    }

    [Fact]
    public void Two_entries_of_one_root_that_both_carry_an_id_are_a_sound_declaration()
    {
        Assert.Empty(MountSelection.ValidateDeclared(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)], Settings));
    }

    /// <summary>D-04: nothing chose, so the run does not guess — and the line names every candidate.</summary>
    [Fact]
    public void Two_entries_of_one_root_with_nothing_selecting_one_are_refused_with_both_ids_named()
    {
        var plan = Resolve([Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)]);

        var error = Assert.Single(plan.Errors);
        Assert.Equal(
            $"'/output' is declared twice in {Settings} ({A}: /srv/a, {B}: /srv/b) and nothing selects one. "
            + "Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, "
            + "or pass --mount <folder>:/output:rw to replace them all.",
            error);
    }

    [Fact]
    public void A_mount_id_selects_its_entry_and_withdraws_the_others_of_that_root()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B), Entry(2, "/srv/c", "/data", id: C)],
            ids: [B]);

        Assert.Empty(plan.Errors);
        var selected = Assert.Single(plan.Selected);
        Assert.Equal((1, "/output", B, MountSelector.MountIdOption), (selected.Index, selected.VirtualRoot, selected.Id, selected.Selector));
        Assert.Null(selected.OverridesCrewChoice);
        var withdrawn = Assert.Single(plan.Withdrawn);
        Assert.Equal((0, "/output", A), (withdrawn.Index, withdrawn.VirtualRoot, withdrawn.Id));
        Assert.Equal([0], plan.WithdrawnIndices());
    }

    [Fact]
    public void The_crew_selects_an_entry_by_id_without_any_flag()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            crew: [$"{B}|/output"]);

        Assert.Empty(plan.Errors);
        var selected = Assert.Single(plan.Selected);
        Assert.Equal((1, B, MountSelector.CrewMounts), (selected.Index, selected.Id, selected.Selector));
        Assert.Equal([0], plan.WithdrawnIndices());
    }

    /// <summary>D-09: a root-only reference names the root but chooses nothing among several.</summary>
    [Fact]
    public void A_root_only_reference_does_not_choose_among_several_entries()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            crew: ["/output"]);

        var error = Assert.Single(plan.Errors);
        Assert.StartsWith($"'/output' is declared twice in {Settings}", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_root_only_reference_on_a_unique_entry_is_satisfied()
    {
        var plan = Resolve([Entry(0, "/srv/a", "/output")], crew: ["/output"]);

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Selected);
    }

    /// <summary>D-10: the option is the more specific intent; the crew's choice is overridden and said so.</summary>
    [Fact]
    public void A_mount_id_wins_over_the_crews_choice_and_the_plan_says_which_it_overrode()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            ids: [B],
            crew: [$"{A}|/output"]);

        Assert.Empty(plan.Errors);
        var selected = Assert.Single(plan.Selected);
        Assert.Equal(B, selected.Id);
        Assert.Equal(MountSelector.MountIdOption, selected.Selector);
        Assert.Equal(A, selected.OverridesCrewChoice);
        Assert.Equal([0], plan.WithdrawnIndices());
    }

    [Fact]
    public void The_same_choice_from_both_sources_overrides_nothing()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            ids: [B],
            crew: [$"{B}|/output"]);

        var selected = Assert.Single(plan.Selected);
        Assert.Null(selected.OverridesCrewChoice);
    }

    /// <summary>(c): a --mount on the root replaces every entry — the first by overwrite, the rest by withdrawal.</summary>
    [Fact]
    public void A_mount_on_a_root_withdraws_every_declared_entry_of_that_root_but_the_first()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B), Entry(2, "/srv/c", "/output", id: C)],
            cli: ["/tmp/run:/output:rw"]);

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Selected);
        Assert.Equal([1, 2], plan.WithdrawnIndices());
    }

    [Fact]
    public void A_mount_id_on_a_root_a_mount_replaces_is_a_warning_and_the_mount_wins()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            cli: ["/tmp/run:/output:rw"],
            ids: [B]);

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Selected);
        var warning = Assert.Single(plan.Warnings);
        Assert.Equal($"--mount-id {B} selects '/output', which --mount also replaces; the --mount wins.", warning);
    }

    [Fact]
    public void A_mount_on_the_root_satisfies_a_crew_reference_whatever_id_it_carries()
    {
        var unknown = MountId.Create();
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A)],
            cli: ["/tmp/run:/output:rw"],
            crew: [$"{unknown}|/output", "/reports"]);

        Assert.Single(plan.Errors);
        Assert.StartsWith("the crew requires '/reports'", plan.Errors[0], StringComparison.Ordinal);

        plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A)],
            cli: ["/tmp/run:/output:rw", "/tmp/reports:/reports:rw"],
            crew: [$"{unknown}|/output", "/reports"]);
        Assert.Empty(plan.Errors);
    }

    [Fact]
    public void A_mount_id_no_entry_carries_is_refused()
    {
        var plan = Resolve([Entry(0, "/srv/a", "/output", id: A)], ids: [B]);

        var error = Assert.Single(plan.Errors);
        Assert.Equal(
            $"no entry of {Settings} carries mount id {B} (passed as --mount-id). Declare it (Studio > Allowed folders) or drop the option.",
            error);
    }

    [Fact]
    public void A_crew_reference_to_an_id_no_entry_carries_is_refused_with_the_root_named()
    {
        var plan = Resolve([Entry(0, "/srv/a", "/output", id: A)], crew: [$"{B}|/output"]);

        var error = Assert.Single(plan.Errors);
        Assert.Equal(
            $"no entry of {Settings} carries mount id {B} (referenced by the crew's mounts: for '/output'). "
            + "Declare it (Studio > Allowed folders) or pass --mount <folder>:/output:rw.",
            error);
    }

    [Fact]
    public void A_crew_reference_whose_id_lives_under_another_root_is_refused()
    {
        var plan = Resolve([Entry(0, "/srv/a", "/docs", id: A)], crew: [$"{A}|/output"]);

        var error = Assert.Single(plan.Errors);
        Assert.Equal(
            $"mount id {A} is '/docs' in {Settings} but the crew lists it for '/output'. Fix the crew's mounts: or the settings entry.",
            error);
    }

    [Fact]
    public void Two_mount_ids_on_one_root_are_refused()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            ids: [A, B]);

        var error = Assert.Single(plan.Errors);
        Assert.Equal($"--mount-id selects two entries of '/output' ({A} and {B}); pass one.", error);
    }

    [Fact]
    public void Two_crew_references_selecting_one_root_are_refused()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            crew: [$"{A}|/output", $"{B}|/output"]);

        var error = Assert.Single(plan.Errors);
        Assert.Equal($"the crew's mounts: selects two entries of '/output' ({A} and {B}); keep one.", error);
    }

    [Fact]
    public void A_root_the_crew_requires_that_nothing_provides_is_refused()
    {
        var plan = Resolve([Entry(0, "/srv/a", "/data")], crew: ["/output"]);

        var error = Assert.Single(plan.Errors);
        Assert.Equal(
            "the crew requires '/output' (mounts: in its definition) and nothing provides it: run the team's launcher, "
            + $"declare a folder under /output in {Settings}, or pass --mount <folder>:/output:rw.",
            error);
    }

    [Fact]
    public void The_same_mount_id_passed_twice_counts_once()
    {
        var plan = Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)],
            ids: [A, A]);

        Assert.Empty(plan.Errors);
        Assert.Equal([A], plan.SelectedMountIds);
        Assert.Equal(A, Assert.Single(plan.Selected).Id);
    }

    [Fact]
    public void A_malformed_declared_entry_is_left_to_the_mount_parser()
    {
        var plan = Resolve([new DeclaredMountEntry(0, "not-a-mount"), Entry(1, "/srv/a", "/output")], crew: ["/output"]);

        Assert.Empty(plan.Errors);
    }

    [Fact]
    public void A_selection_on_a_unique_root_is_harmless_and_logged_as_a_selection()
    {
        var plan = Resolve([Entry(0, "/srv/a", "/output", id: A)], ids: [A]);

        Assert.Empty(plan.Errors);
        Assert.Equal(A, Assert.Single(plan.Selected).Id);
        Assert.Empty(plan.Withdrawn);
    }

    [Fact]
    public void Without_a_settings_file_the_messages_name_the_configuration()
    {
        var plan = MountSelection.Resolve(
            [Entry(0, "/srv/a", "/output", id: A), Entry(1, "/srv/b", "/output", id: B)], [], [], [], settingsPath: null);

        Assert.StartsWith("'/output' is declared twice in the configuration (", Assert.Single(plan.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Indices_are_the_configuration_indices_not_positions()
    {
        var plan = Resolve(
            [Entry(3, "/srv/a", "/output", id: A), Entry(7, "/srv/b", "/output", id: B)],
            ids: [B]);

        Assert.Equal(7, Assert.Single(plan.Selected).Index);
        Assert.Equal([3], plan.WithdrawnIndices());
    }
}
