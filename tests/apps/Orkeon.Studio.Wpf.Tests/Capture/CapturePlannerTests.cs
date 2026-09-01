using System.Text.Json;
using Orkeon.Studio.Wpf.ViewModels.Capture;
using Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>The shape of the collection: which passes, how many shots, and where each one lands.</summary>
public sealed class CapturePlannerTests
{
    [Fact]
    public void The_matrix_walks_both_themes_and_both_modes_plus_a_language_sweep()
    {
        var matrix = CaptureMatrix.Default;
        var passes = matrix.Passes;

        // Four appearance passes in the base language…
        var main = passes.Where(pass => !matrix.IsSweep(pass)).ToList();
        Assert.Equal(4, main.Count);
        Assert.Contains(main, pass => pass is { IsDark: true, Mode: UiModeViewModel.Novice });
        Assert.Contains(main, pass => pass is { IsDark: false, Mode: UiModeViewModel.Expert });

        // …plus one per other supported language, so a sixth language sweeps itself.
        var sweep = passes.Where(matrix.IsSweep).ToList();
        Assert.Equal(LanguageSelectorViewModel.Supported.Count - 1, sweep.Count);
        Assert.All(sweep, pass => Assert.False(pass.IsDark));
    }

    /// <summary>
    /// The ordinal is the stop's place in the CATALOGUE, not in the write order — so the same stop
    /// carries the same relative path in every pass and comparing light against dark is a directory
    /// diff. Getting this wrong is what would make a collection of hundreds of images unreadable.
    /// </summary>
    [Fact]
    public void The_same_stop_lands_at_the_same_relative_path_in_every_pass()
    {
        var plan = CapturePlanner.Plan(CaptureCatalog.All, CaptureMatrix.Default);

        foreach (var group in plan.GroupBy(item => item.Stop.Name))
        {
            var names = group.Select(item => Path.GetFileName(item.RelativePath)).Distinct(StringComparer.Ordinal);
            Assert.Single(names);
        }
    }

    [Fact]
    public void Every_planned_path_is_unique()
    {
        var plan = CapturePlanner.Plan(CaptureCatalog.All, CaptureMatrix.Default);

        Assert.Equal(plan.Count, plan.Select(item => item.RelativePath).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_mode_neutral_stop_is_captured_once_and_not_twice()
    {
        var neutral = CaptureCatalog.All.First(stop => stop.Modes == CaptureModes.Either);
        var plan = CapturePlanner.Plan(CaptureCatalog.All, CaptureMatrix.Default);

        var shots = plan.Where(item => item.Stop.Name == neutral.Name).ToList();

        // Once per theme, never twice in the same theme: two identical images in one collection is
        // exactly the noise the "differs from previous" check exists to refuse.
        Assert.All(shots, shot => Assert.Equal(UiModeViewModel.Novice, shot.Appearance.Mode));
    }

    /// <summary>
    /// The size of the collection, pinned. Not a rule about how big it should be — a way of making
    /// a change to it a visible line in a diff, so nobody grows the campaign from three hundred
    /// images to a thousand without noticing, and nobody shrinks it without saying why.
    /// </summary>
    [Fact]
    public void The_collection_is_the_size_the_catalogue_says()
    {
        var plan = CapturePlanner.Plan(CaptureCatalog.All, CaptureMatrix.Default);

        Assert.Equal(48, CaptureCatalog.All.Count);
        Assert.Equal(250, plan.Count);
    }

    [Fact]
    public void The_manifest_round_trips_through_its_own_json()
    {
        var manifest = new CaptureManifest
        {
            StudioVersion = "1.0.0.0",
            Window = "1440x900",
            Planned = 2,
            Written = 1,
            Failed = 1,
            Images =
            [
                new()
                {
                    File = "fr/light/novice/001-accueil-demarrage.png",
                    Stop = "demarrage",
                    Category = "accueil",
                    Screen = "Create",
                    World = "Seeded",
                    Appearance = "fr/light/novice",
                    Because = "The startup plate.",
                    Sha256 = "abc",
                    Status = "written",
                },
            ],
        };

        using var document = JsonDocument.Parse(manifest.ToJson());
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("planned").GetInt32());
        Assert.Equal("abc", root.GetProperty("images")[0].GetProperty("sha256").GetString());
        Assert.NotEmpty(root.GetProperty("notes").EnumerateArray());
    }
}
