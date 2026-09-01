using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Orkeon.Studio.Wpf.ViewModels.Capture;
using Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The rules that keep a screen from quietly leaving the collection.
/// <para>
/// A campaign is only a fidelity reference while it is complete, and completeness is the one
/// property nobody notices losing: a screen added next year and never photographed looks exactly
/// like a screen that was photographed and did not change. These read the markup as text, in the
/// idiom of the conformity tests already in this suite, so they hold on the Linux runner.
/// </para>
/// </summary>
public sealed partial class CaptureCatalogConformityTests : IAsyncLifetime
{
    private CaptureWorlds _worlds = null!;
    private MainWindowViewModel _shell = null!;

    public async ValueTask InitializeAsync()
    {
        _worlds = await CaptureWorlds.CreateAsync();
        _shell = CaptureShellBuilder.Build(_worlds.Seeded, new CaptureHost
        {
            Dispatcher = ImmediateUiDispatcher.Instance,
            Language = "fr",
            Mode = UiModeViewModel.Expert,
        });
    }

    public ValueTask DisposeAsync()
    {
        _worlds.Dispose();
        return ValueTask.CompletedTask;
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("x:Name=\"Nav(\\w+)\"[^>]*GroupName=\"Nav\"", RegexOptions.Singleline)]
    private static partial Regex NavRadioPattern();

    [GeneratedRegex("Visibility=\"\\{Binding ([\\w\\.]+), Converter=\\{StaticResource BooleanToVisibility\\}\\}\"")]
    private static partial Regex ScrimGatePattern();

    [GeneratedRegex("Storyboard\\.TargetName=\"(\\w+)\"")]
    private static partial Regex StoryboardTargetPattern();

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(
            testsDir, "..", "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static string ReadSource(params string[] segments) =>
        File.ReadAllText(Path.Combine([WpfSourceRoot(), .. segments]));

    [Fact]
    public void Every_stop_has_a_unique_file_safe_name_and_says_why_it_exists()
    {
        var offenders = new List<string>();

        foreach (var stop in CaptureCatalog.All)
        {
            if (!SlugPattern().IsMatch(stop.Name))
                offenders.Add($"{stop.Name}: not a file-safe slug");

            // A stop nobody can justify is a stop nobody will review; the manifest carries this
            // sentence next to the image, and an empty one makes the collection unreadable.
            if (stop.Because.Length < 40)
                offenders.Add($"{stop.Name}: says too little about why it exists");
        }

        var duplicates = CaptureCatalog.All
            .GroupBy(stop => stop.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        offenders.AddRange(duplicates.Select(name => $"{name}: declared twice"));

        Assert.True(offenders.Count == 0, string.Join(" ; ", offenders));
    }

    /// <summary>
    /// Every gate a stop names must resolve against a real shell — whatever it currently holds.
    /// Renaming a ViewModel property has to break the build; it must not quietly turn every claim
    /// about that property into a vacuous truth.
    /// </summary>
    [Fact]
    public void Every_gate_a_stop_names_resolves_to_a_real_view_model_member()
    {
        var unresolved = CaptureCatalog.All
            .SelectMany(stop => stop.Covers.Concat(stop.CoversFalse).Select(gate => (stop.Name, Gate: gate)))
            .Where(entry => !GateReader.Resolves(_shell, entry.Gate))
            .Select(entry => $"{entry.Name} names {entry.Gate}")
            .ToList();

        Assert.True(unresolved.Count == 0,
            "A gate that no longer exists makes every claim about it vacuous: "
            + string.Join(" ; ", unresolved));
    }

    [Fact]
    public void Every_navigable_screen_has_a_stop_in_every_mode_it_is_reachable_in()
    {
        var missing = new List<string>();

        foreach (var mode in new[] { CaptureModes.Novice, CaptureModes.Expert })
        {
            var covered = CaptureCatalog.All
                .Where(stop => CaptureCatalog.Applies(stop, mode))
                .Select(stop => stop.Screen)
                .ToHashSet();

            missing.AddRange(Enum.GetValues<CaptureScreen>()
                .Where(screen => CaptureReachability.IsReachableIn(screen, mode))
                .Where(screen => !covered.Contains(screen))
                .Select(screen => $"{screen} has no {mode} stop"));
        }

        Assert.True(missing.Count == 0, string.Join(" ; ", missing));
    }

    /// <summary>
    /// A nav entry the campaign cannot reach is a screen no screenshot will ever show. The markup
    /// is the source of truth, so a ninth sidebar entry fails here on the day it is written.
    /// </summary>
    [Fact]
    public void Every_nav_entry_in_the_markup_maps_to_a_capture_screen()
    {
        var radios = NavRadioPattern()
            .Matches(ReadSource("Views", "MainWindow.xaml"))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(radios);

        // The four settings tabs are one nav entry, and the sidebar spells the diagnostic «Diag».
        var mapped = Enum.GetNames<CaptureScreen>()
            .Select(name => name.StartsWith("Settings", StringComparison.Ordinal) ? "Settings" : name)
            .Select(name => name == "Diagnostic" ? "Diag" : name)
            .ToHashSet(StringComparer.Ordinal);

        var unmapped = radios.Where(radio => !mapped.Contains(radio)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unmapped.Count == 0,
            "Add a CaptureScreen value and at least one stop for: " + string.Join(", ", unmapped));
    }

    /// <summary>
    /// Every scrim overlay is a whole screen laid over another one, and four of the six had never
    /// been photographed. A seventh must not be able to arrive unnoticed.
    /// </summary>
    [Fact]
    public void Every_scrim_overlay_is_reached_by_a_stop()
    {
        var markup = ReadSource("Views", "MainWindow.xaml");
        var gates = new List<string>();

        foreach (var block in markup.Split("ScrimBrush", StringSplitOptions.None).Skip(1))
        {
            if (ScrimGatePattern().Match(block) is { Success: true } match)
                gates.Add(match.Groups[1].Value);
        }

        Assert.NotEmpty(gates);

        var covered = CaptureCatalog.All
            .SelectMany(stop => stop.Covers)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = gates
            .Where(gate => !covered.Contains(gate))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(uncovered.Count == 0,
            "These modals cover the whole window and no stop ever opens one: "
            + string.Join(", ", uncovered));
    }

    /// <summary>
    /// A new endless storyboard with no pose is a new screen whose PNG differs on every run of the
    /// campaign for no reason — exactly the noise that makes a fidelity diff useless.
    /// </summary>
    [Fact]
    public void Every_endless_storyboard_target_has_a_capture_pose()
    {
        var pose = ReadSource("Services", "Capture", "CapturePose.cs");
        var targets = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(WpfSourceRoot(), "Views"), "*.xaml", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(file);
            foreach (var block in markup.Split("RepeatBehavior=\"Forever\"", StringSplitOptions.None).Skip(1))
            {
                // One storyboard's worth of markup, not the rest of the file.
                var end = block.IndexOf("</Storyboard>", StringComparison.Ordinal);
                var body = end > 0 ? block[..end] : block;
                targets.AddRange(StoryboardTargetPattern().Matches(body).Select(m => m.Groups[1].Value));
            }
        }

        Assert.NotEmpty(targets);

        // Three of these are posed by their GLYPH rather than by name — one rule covers every
        // loader-circle in the app, present and future — so the pose file names the glyph instead.
        var posedStructurally = new[] { "EngineSpin", "TrialSpin", "ProgressSpin" };

        var unposed = targets
            .Distinct(StringComparer.Ordinal)
            .Where(target => !pose.Contains(target, StringComparison.Ordinal)
                          && !posedStructurally.Contains(target, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unposed.Count == 0,
            "These animate forever and CapturePose does not pin them, so every run of the campaign "
            + "would catch them at a different phase: " + string.Join(", ", unposed));
    }

    /// <summary>
    /// A tour step whose target names nothing spotlights nothing — a silent, shippable defect the
    /// literals in the code-behind used to hide.
    /// </summary>
    [Fact]
    public void Every_guided_tour_target_names_an_element_of_the_window()
    {
        var markup = ReadSource("Views", "MainWindow.xaml");

        var missing = GuidedTourCatalog.Steps
            .Select(step => step.TargetName)
            .OfType<string>()
            .Where(target => !markup.Contains($"x:Name=\"{target}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "The spotlight would fall on nothing for: " + string.Join(", ", missing));
    }
}
