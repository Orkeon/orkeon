using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>
/// The assistant conversation — a whole panel that had never been photographed once, on any of
/// the three screens it mounts on.
/// <para>
/// One instance is shared by Create, Run and History, and it answers differently on each:
/// the primer changes, and the pinned brief card belongs to the wizard alone. That is three
/// screens' worth of design behind one property.
/// </para>
/// </summary>
internal static class AssistantStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "assistant-amorce-wizard",
            Category = CaptureCategory.Assistant,
            Screen = CaptureScreen.Create,
            Because = "The conversation as it opens on the wizard: the primer bubble instead of "
                    + "turns, and the pinned brief card that belongs to this screen only.",
            Covers = ["Chat.IsOpen", "Chat.IsEmpty", "Chat.ShowsBrief"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c =>
            {
                c.Shell.Chat.SetContext(AssistantContext.WizardStep1);
                c.Shell.Chat.OpenCommand.Execute(null);
            }),
        },

        new()
        {
            Name = "assistant-conversation",
            Category = CaptureCategory.Assistant,
            Screen = CaptureScreen.Create,
            Because = "A thread with turns in it: the bot bubbles, the user bubbles and the "
                    + "novice-only hints under them.",
            Covers = ["Chat.IsOpen"],
            CoversFalse = ["Chat.IsEmpty"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c =>
            {
                foreach (var turn in StudioFixture.AssistantTurns)
                    c.Shell.Chat.AddAssistantTurn(turn);
            }),
        },

        new()
        {
            Name = "assistant-recap-deplie",
            Category = CaptureCategory.Assistant,
            Screen = CaptureScreen.Create,
            Because = "«Ce que j'ai retenu» unfolded: the facts the assistant believes it has, "
                    + "and — the part worth reviewing — the ones it admits it has not.",
            Covers = ["Chat.IsOpen", "Chat.IsRecapExpanded"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Chat.ToggleRecapCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Chat.ToggleRecapCommand.Execute(null)),
        },

        new()
        {
            Name = "assistant-en-cours",
            Category = CaptureCategory.Assistant,
            Screen = CaptureScreen.Create,
            Because = "The assistant thinking: the sweeping hairline, the pulsing halo and the "
                    + "three dots — the one shot that has to look ALIVE, and therefore the one the "
                    + "pose exists for.",
            Covers = ["Chat.IsOpen", "Chat.IsBusy"],
            Arrange = CaptureAction.Sync(static c =>
            {
                // The real gesture, held in the pause it opens: the thread goes busy when a
                // question is sent and stays busy exactly as long as the beat lasts.
                c.World.Delay.Hold = true;
                c.Shell.Chat.Draft = "Et si mes sources changent de format ?";
                c.Shell.Chat.SendCommand.Execute(null);
            }),
            Teardown = CaptureAction.Sync(static c =>
            {
                c.World.Delay.Release();
                c.Shell.Chat.CloseCommand.Execute(null);
            }),
        },

        new()
        {
            Name = "assistant-sur-executer",
            Category = CaptureCategory.Assistant,
            Screen = CaptureScreen.Run,
            Because = "The same thread on the launcher: the primer answers about running a team "
                    + "rather than composing one, and the brief card is gone because it is not "
                    + "this screen's.",
            Covers = ["Chat.IsOpen"],
            CoversFalse = ["Chat.ShowsBrief"],
            Arrange = CaptureAction.Sync(static c =>
            {
                c.Shell.Chat.SetContext(AssistantContext.Run);
                c.Shell.Chat.OpenCommand.Execute(null);
            }),
            Teardown = CaptureAction.Sync(static c => c.Shell.Chat.CloseCommand.Execute(null)),
        },
    ];
}
