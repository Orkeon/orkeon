using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Wpf.ViewModels.Capture;
using Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.Views;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>What the campaign was asked for.</summary>
internal sealed record CaptureOptions
{
    /// <summary>Where the PNGs go.</summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Window width. 1440×900 rather than the 1024×768 minimum: at 1024 the content column is
    /// 752 DIP, and the panels declare maximum widths of 880, 1000 and 1080 — so NONE of them ever
    /// binds, and every shot is the narrowest layout the app can produce, which is not the one the
    /// design was drawn for.
    /// </summary>
    public double Width { get; init; } = 1440;

    /// <summary>Window height.</summary>
    public double Height { get; init; } = 900;

    /// <summary>Render scale; 2 gives a retina collection.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>Which passes to run.</summary>
    public CaptureMatrix Matrix { get; init; } = CaptureMatrix.Default;
}

/// <summary>One stop that did not produce a trustworthy image.</summary>
/// <param name="Stop">Its slug.</param>
/// <param name="Pass">The pass it was in.</param>
/// <param name="Reason">What was wrong.</param>
internal sealed record CaptureFailure(string Stop, string Pass, string Reason);

/// <summary>What the campaign did.</summary>
/// <param name="Written">Files actually written — counted at the write, never at the index.</param>
/// <param name="Planned">Shots the plan asked for.</param>
/// <param name="Failures">Every stop that did not produce a trustworthy image.</param>
/// <param name="SandboxRoot">Where the seeded worlds lived, printed so a failure can be inspected.</param>
internal sealed record CaptureReport(
    int Written, int Planned, IReadOnlyList<CaptureFailure> Failures, string SandboxRoot);

/// <summary>
/// The screenshot campaign: walks every screen and every gated state of the window, over a seeded
/// scenario, in both modes and both themes, and writes one PNG per stop plus a manifest.
/// </summary>
[SuppressVfsCompliance(
    "UI-layer capture harness writing PNGs and a manifest where the operator pointed it — " +
    "Studio's own state, not framework I/O; same exception class as UiPreferences.")]
internal static class CaptureCampaign
{
    /// <summary>Runs the whole campaign.</summary>
    public static async Task<CaptureReport> RunAsync(CaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Directory.CreateDirectory(options.Directory);

        using var worlds = await CaptureWorlds.CreateAsync();
        var bindings = BindingErrorSink.Install();
        var plan = CapturePlanner.Plan(CaptureCatalog.All, options.Matrix);

        var images = new List<CaptureManifestImage>();
        var failures = new List<CaptureFailure>();
        var written = 0;
        var dpiScale = 1d;

        // Walked pass by pass, straight off the plan: the planner already decided the order and
        // the destination of every shot, and re-deriving either here is how the two would drift.
        foreach (var pass in plan.GroupBy(item => item.Appearance))
        {
            var stages = await OpenStagesAsync(worlds, pass.Key, options);
            dpiScale = VisualTreeHelper.GetDpi(stages.Values.First().Surface.Window).DpiScaleX;

            string? previousSha = null;
            foreach (var item in pass)
            {
                var (image, sha) = await TakeAsync(
                    stages[item.Stop.World], item, options, bindings, previousSha);
                images.Add(image);

                if (string.Equals(image.Status, "written", StringComparison.Ordinal))
                {
                    written++;
                    previousSha = sha;
                }
                else
                {
                    failures.Add(new CaptureFailure(
                        item.Stop.Name, pass.Key.Folder, string.Join(" ; ", image.Problems)));
                }
            }

            foreach (var stage in stages.Values)
                stage.Surface.Window.Close();
        }

        var manifest = new CaptureManifest
        {
            StudioVersion = StudioVersion(),
            Window = string.Create(CultureInfo.InvariantCulture, $"{options.Width:0}x{options.Height:0}"),
            Scale = options.Scale,
            MachineDpiScale = dpiScale,
            Planned = plan.Count,
            Written = written,
            Failed = failures.Count,
            Images = images,
        };

        await File.WriteAllTextAsync(Path.Combine(options.Directory, "manifest.json"), manifest.ToJson());

        return new CaptureReport(written, plan.Count, failures, worlds.Root);
    }

    /// <summary>One window per world the pass needs, opened, initialised and settled.</summary>
    private static async Task<Dictionary<CaptureWorldKind, CaptureStage>> OpenStagesAsync(
        CaptureWorlds worlds, CaptureAppearance appearance, CaptureOptions options)
    {
        var stages = new Dictionary<CaptureWorldKind, CaptureStage>();

        foreach (var kind in new[] { CaptureWorldKind.Seeded, CaptureWorldKind.Pristine })
        {
            var world = worlds.For(kind);
            var host = new CaptureHost
            {
                Dispatcher = new WpfDispatcher(System.Windows.Application.Current.Dispatcher),
                Strings = I18nStudioStrings.Instance,
                // The real gesture, not I18n.SetLanguage: the latter only re-raises the XAML
                // indexer bindings and leaves every string a ViewModel computed and cached in the
                // previous language, which is a half-translated screenshot.
                ApplyLanguage = I18n.Instance.SetLanguage,
                Language = appearance.Language,
                Mode = appearance.Mode,
            };

            var shell = CaptureShellBuilder.Build(world, host);
            var window = new MainWindow { DataContext = shell };
            window.WindowState = WindowState.Normal;
            window.Width = options.Width;
            window.Height = options.Height;
            window.Show();

            await WaitForFirstRenderAsync(window);
            await CaptureShellBuilder.PrepareAsync(shell, world);
            window.HideSplashForCapture();
            window.ApplyThemeForCapture(appearance.IsDark);
            AlignCulture(window, appearance.Language);

            if (string.Equals(appearance.Mode, UiModeViewModel.Expert, StringComparison.Ordinal))
                shell.Mode.SetExpertCommand.Execute(null);
            else
                shell.Mode.SetNoviceCommand.Execute(null);

            var surface = new WpfCaptureSurface(window, shell);
            await surface.SettleAsync();

            stages[kind] = new CaptureStage(world, shell, surface);
            window.Hide();
        }

        return stages;
    }

    /// <summary>Arranges one stop, photographs it, checks it and writes it.</summary>
    private static async Task<(CaptureManifestImage Image, string? Sha)> TakeAsync(
        CaptureStage stage,
        CapturePlanItem item,
        CaptureOptions options,
        BindingErrorSink bindings,
        string? previousSha)
    {
        var stop = item.Stop;
        var window = stage.Surface.Window;
        var problems = new List<string>();
        var notes = new List<string>();
        string? sha = null;

        window.Show();
        bindings.Drain();

        var context = new CaptureContext
        {
            Shell = stage.Shell,
            World = stage.World,
            Surface = stage.Surface,
            Appearance = item.Appearance,
        };

        try
        {
            await stage.Surface.ShowAsync(stop.Screen);
            await stop.Arrange(context);
            await stage.Surface.SettleAsync();

            problems.AddRange(Inspect(stage, item, options, window, previousSha, notes, out sha, out var png));

            if (problems.Count == 0 && png is not null)
            {
                CaptureShot.WriteAtomic(Path.Combine(options.Directory, item.RelativePath), png);
            }
            else if (png is not null)
            {
                // Quarantined rather than dropped: it can be looked at, and it can never be
                // mistaken for part of the reference collection.
                CaptureShot.WriteAtomic(
                    Path.Combine(options.Directory, "failed", item.RelativePath), png);
            }
        }
#pragma warning disable CA1031 // Per-stop barrier: one bad stop must not hide the other three hundred.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            problems.Add(exception.Message);
        }
        finally
        {
            try
            {
                await stop.Teardown(context);
                await context.DrainHeldAsync();
            }
#pragma warning disable CA1031 // A teardown that throws is this stop's problem, not the next one's.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                problems.Add("teardown: " + exception.Message);
            }

            window.Hide();
        }

        var image = new CaptureManifestImage
        {
            File = item.RelativePath,
            Stop = stop.Name,
            Category = CaptureCategories.Slug(stop.Category),
            Screen = stop.Screen.ToString(),
            World = stop.World.ToString(),
            Appearance = item.Appearance.Folder,
            Because = stop.Because,
            Covers = stop.Covers,
            Sha256 = sha,
            Status = problems.Count == 0 ? "written" : "failed",
            Problems = [.. problems, .. notes],
            BindingErrors = bindings.Drain(),
        };

        return (image, problems.Count == 0 ? sha : previousSha);
    }

    /// <summary>Every check, cheapest first; the PNG comes back so a failed shot can be quarantined.</summary>
    private static List<string> Inspect(
        CaptureStage stage,
        CapturePlanItem item,
        CaptureOptions options,
        MainWindow window,
        string? previousSha,
        List<string> notes,
        out string? sha,
        out byte[]? png)
    {
        var problems = new List<string>();
        sha = null;
        png = null;

        if (Math.Abs(window.ActualWidth - options.Width) > 0.5
            || Math.Abs(window.ActualHeight - options.Height) > 0.5)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"the window settled at {window.ActualWidth:0}x{window.ActualHeight:0}, not the "
                + $"{options.Width:0}x{options.Height:0} the collection claims"));
            return problems;
        }

        if (CaptureVerification.PanelIsUp(
                stage.Surface.PanelFor(item.Stop.Screen), item.Stop.Screen.ToString()) is { } panel)
        {
            problems.Add(panel);
        }

        // The stop's own claims, checked here too and not only on the Linux runner: a state that
        // holds headless and not under a real window is exactly the difference worth catching.
        foreach (var gate in item.Stop.Covers.Where(gate => !GateReader.IsTrue(stage.Shell, gate)))
            problems.Add($"claims {gate}, which is false after the arrange");

        foreach (var gate in item.Stop.CoversFalse.Where(gate => GateReader.IsTrue(stage.Shell, gate)))
            problems.Add($"claims NOT {gate}, which is true after the arrange");

        var bitmap = CaptureShot.Render(window, options.Scale, notes);
        var expectedWidth = (int)Math.Ceiling(options.Width * options.Scale);
        var expectedHeight = (int)Math.Ceiling(options.Height * options.Scale);

        if (CaptureVerification.Geometry(bitmap, expectedWidth, expectedHeight) is { } geometry)
            problems.Add(geometry);

        if (CaptureVerification.Uniformity(CaptureVerification.Sample(bitmap)) is { } uniform)
            problems.Add(uniform);

        (png, sha) = CaptureShot.Encode(bitmap);

        if (CaptureVerification.DiffersFromPrevious(sha, previousSha, item.Stop.AllowSameAsPrevious) is { } same)
            problems.Add(same);

        return problems;
    }

    /// <summary>
    /// The first shot must not race the first frame. The campaign is kicked off at Normal
    /// priority, which is ABOVE Render — so without this gate its first statement runs before the
    /// window has drawn anything at all.
    /// </summary>
    private static async Task WaitForFirstRenderAsync(Window window)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? onRendered = null;
        onRendered = (_, _) =>
        {
            window.ContentRendered -= onRendered;
            rendered.TrySetResult();
        };

        window.ContentRendered += onRendered;

        // Raced against an idle drain: ContentRendered may already have fired, and an idle drain
        // cannot complete before the first render either.
        await Task.WhenAny(
            rendered.Task,
            window.Dispatcher.InvokeAsync(
                () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
    }

    /// <summary>
    /// The culture behind the catalogue. <c>Window.Language</c> is what WPF's font fallback and
    /// line breaking consult — so it, not the string table, decides whether zh-Hans renders glyphs
    /// or boxes; and <c>CurrentCulture</c> is what every StringFormat binding and the tour's own
    /// counter format against.
    /// </summary>
    private static void AlignCulture(Window window, string languageCode)
    {
        var culture = CultureInfo.GetCultureInfo(
            string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase) ? "zh-Hans" : languageCode);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        window.Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
    }

    private static string StudioVersion() =>
        typeof(CaptureCampaign).Assembly.GetName().Version?.ToString() ?? "?";

    /// <summary>A window, its ViewModel and the world underneath, for one pass.</summary>
    private sealed record CaptureStage(
        CaptureWorld World, MainWindowViewModel Shell, WpfCaptureSurface Surface);
}
