namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>Which of the two seeded machines a stop stands on.</summary>
internal enum CaptureWorldKind
{
    /// <summary>Teams, history, sessions, profiles, a CLI — a machine in use.</summary>
    Seeded,

    /// <summary>Nothing anywhere, and no CLI installed — the first-run machine.</summary>
    Pristine,
}
