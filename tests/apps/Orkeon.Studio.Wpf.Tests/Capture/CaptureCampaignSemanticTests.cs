using Orkeon.Studio.Wpf.ViewModels.Capture;
using Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The campaign, replayed without a window.
/// <para>
/// This is the keystone of the design. The catalogue's arrange bodies touch only ViewModels and one
/// narrow surface, so the whole walk runs on the Linux runner — and every stop's <c>Covers</c> is
/// checked. A stop that quietly stops reaching the state it names fails here, by name, instead of
/// writing a confident picture of the wrong screen on a machine nobody is watching.
/// </para>
/// <para>
/// The walk runs IN ORDER, with the real teardowns, which is also exactly the test for state
/// leaking from one stop into the next: a modal left open by stop 12 breaks stop 13's claim.
/// </para>
/// </summary>
public sealed class CaptureCampaignSemanticTests : IAsyncLifetime
{
    private CaptureWorlds _worlds = null!;

    public async ValueTask InitializeAsync() => _worlds = await CaptureWorlds.CreateAsync();

    public ValueTask DisposeAsync()
    {
        _worlds.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(UiModeViewModel.Novice)]
    [InlineData(UiModeViewModel.Expert)]
    public async Task Every_stop_reaches_the_state_it_claims_when_the_whole_campaign_runs_in_order(string mode)
    {
        var appearance = new CaptureAppearance("fr", IsDark: false, mode);
        var stops = CaptureCatalog.For(appearance, CaptureMatrix.Default);
        var shells = await BuildShellsAsync(appearance);
        var surface = new RecordingCaptureSurface();
        var failures = new List<string>();

        foreach (var stop in stops)
        {
            var shell = shells[stop.World];
            var context = new CaptureContext
            {
                Shell = shell,
                World = _worlds.For(stop.World),
                Surface = surface,
                Appearance = appearance,
            };

            try
            {
                await surface.ShowAsync(stop.Screen);
                await stop.Arrange(context);
            }
            catch (Exception exception)
            {
                failures.Add($"{stop.Name}: the arrange threw — {exception.Message}");
                continue;
            }

            foreach (var gate in stop.Covers.Where(gate => !GateReader.IsTrue(shell, gate)))
                failures.Add($"{stop.Name}: claims {gate}, and it is false after the arrange");

            foreach (var gate in stop.CoversFalse.Where(gate => GateReader.IsTrue(shell, gate)))
                failures.Add($"{stop.Name}: claims NOT {gate}, and it is true after the arrange");

            await stop.Teardown(context);
            await context.DrainHeldAsync();
        }

        Assert.True(
            failures.Count == 0,
            "A stop that no longer reaches its state still writes a PNG — of the wrong screen, "
            + "silently, on a machine nobody is watching. " + string.Join(" ; ", failures));
    }

    /// <summary>
    /// No stop may leave a scrim up behind it. The next shot would carry a modal nobody asked for,
    /// and at three hundred images nobody would notice which one started it.
    /// </summary>
    [Fact]
    public async Task No_stop_leaves_a_modal_open_behind_it()
    {
        var appearance = new CaptureAppearance("fr", IsDark: false, UiModeViewModel.Expert);
        var shells = await BuildShellsAsync(appearance);
        var surface = new RecordingCaptureSurface();
        var offenders = new List<string>();

        string[] modals =
        [
            "Settings.Profiles.IsEditorOpen",
            "TeamMounts.IsOpen",
            "CreateTeam.AgentEditor.IsOpen",
            "CreateTeam.Gallery.IsOpen",
            "AllowedFolders.IsOpen",
            "About.IsOpen",
        ];

        foreach (var stop in CaptureCatalog.For(appearance, CaptureMatrix.Default))
        {
            var shell = shells[stop.World];
            var context = new CaptureContext
            {
                Shell = shell,
                World = _worlds.For(stop.World),
                Surface = surface,
                Appearance = appearance,
            };

            await stop.Arrange(context);
            await stop.Teardown(context);
            await context.DrainHeldAsync();

            offenders.AddRange(modals
                .Where(modal => GateReader.IsTrue(shell, modal))
                .Select(modal => $"{stop.Name} left {modal} up"));
        }

        Assert.True(offenders.Count == 0, string.Join(" ; ", offenders));
    }

    /// <summary>
    /// STUDIO-69: the E-mail stops show what the engine makes of each account, and that comes
    /// from the scripted CLI — asked about the world's own settings file. A connection test is
    /// the one gesture of the tab that reaches a mail server: no stop ever makes it.
    /// </summary>
    [Fact]
    public async Task The_email_stops_read_the_accounts_states_from_the_scripted_cli_and_never_test_a_connection()
    {
        var appearance = new CaptureAppearance("fr", IsDark: false, UiModeViewModel.Expert);
        var shells = await BuildShellsAsync(appearance);
        var surface = new RecordingCaptureSurface();
        var world = _worlds.For(CaptureWorldKind.Seeded);
        var shell = shells[CaptureWorldKind.Seeded];
        var stops = CaptureCatalog.For(appearance, CaptureMatrix.Default)
            .Where(stop => stop.Screen == CaptureScreen.SettingsEmail)
            .ToList();
        Assert.NotEmpty(stops);

        foreach (var stop in stops)
        {
            var context = new CaptureContext { Shell = shell, World = world, Surface = surface, Appearance = appearance };
            await stop.Arrange(context);

            Assert.All(shell.Config.Email.Accounts, row => Assert.True(row.HasState, $"{stop.Name}: {row.Name} shows no state"));
            Assert.Contains(shell.Config.Email.Accounts, row => row.IsReady);
            Assert.Contains(shell.Config.Email.Accounts, row => row.HasStateDetail);

            await stop.Teardown(context);
            await context.DrainHeldAsync();
        }

        Assert.Contains(world.Cli.Requests, request =>
            request.Arguments is ["email", "accounts", "--json", "--settings", var path] && path == world.SettingsPath);
        Assert.DoesNotContain(world.Cli.Requests, request => request.Arguments is ["email", "check", ..]);
    }

    /// <summary>
    /// STUDIO-70: the sign-in stop shows the panel of a device sign-in while the scripted
    /// <c>orkeon email login</c> waits — the page, a made-up code, the time it has left —, opens
    /// nothing, and its teardown leaves no child behind for the stops that follow.
    /// </summary>
    [Theory]
    [InlineData(UiModeViewModel.Novice)]
    [InlineData(UiModeViewModel.Expert)]
    public async Task The_sign_in_stop_shows_the_device_code_panel_and_leaves_no_child_behind(string mode)
    {
        var appearance = new CaptureAppearance("fr", IsDark: false, mode);
        var shells = await BuildShellsAsync(appearance);
        var world = _worlds.For(CaptureWorldKind.Seeded);
        var shell = shells[CaptureWorldKind.Seeded];
        var stop = Assert.Single(CaptureCatalog.For(appearance, CaptureMatrix.Default), s => s.Name == "reglages-mails-connexion");
        var context = new CaptureContext { Shell = shell, World = world, Surface = new RecordingCaptureSurface(), Appearance = appearance };

        await stop.Arrange(context);

        var row = shell.Config.Email.SelectedAccount;
        Assert.NotNull(row);
        Assert.True(row.ShowSignIn);
        Assert.NotNull(row.SignIn);
        Assert.True(row.SignIn.IsDeviceCode);
        Assert.Equal(Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures.StudioFixture.DeviceSignInCode, row.SignIn.UserCode);
        Assert.True(row.SignIn.HasExpiry);
        Assert.True(row.SignIn.CanOpen);
        Assert.Equal(1, world.Cli.LiveConversations);
        Assert.Contains(world.Cli.Requests, request =>
            request.Arguments is ["email", "login", _, "--events", "jsonl", "--settings", var path] && path == world.SettingsPath);

        await stop.Teardown(context);
        await context.DrainHeldAsync();

        Assert.False(shell.Config.Email.IsSigningIn);
        Assert.Equal(0, world.Cli.LiveConversations);
        Assert.Same(shell.Config.Email.Accounts[0], shell.Config.Email.SelectedAccount);
    }

    private async Task<Dictionary<CaptureWorldKind, MainWindowViewModel>> BuildShellsAsync(
        CaptureAppearance appearance)
    {
        var shells = new Dictionary<CaptureWorldKind, MainWindowViewModel>();

        foreach (var kind in new[] { CaptureWorldKind.Seeded, CaptureWorldKind.Pristine })
        {
            var shell = CaptureShellBuilder.Build(_worlds.For(kind), new CaptureHost
            {
                Dispatcher = ImmediateUiDispatcher.Instance,
                Language = appearance.Language,
                Mode = appearance.Mode,
            });

            await CaptureShellBuilder.PrepareAsync(
                shell, _worlds.For(kind), TestContext.Current.CancellationToken);

            if (string.Equals(appearance.Mode, UiModeViewModel.Expert, StringComparison.Ordinal))
                shell.Mode.SetExpertCommand.Execute(null);
            else
                shell.Mode.SetNoviceCommand.Execute(null);

            shells[kind] = shell;
        }

        return shells;
    }
}
