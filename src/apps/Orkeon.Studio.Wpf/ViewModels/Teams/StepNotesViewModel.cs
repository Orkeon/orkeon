using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// The per-step consigne block (30/08 mock, StepNotes.dc.html): the field IS the consigne —
/// typed text is taken as it stands, no add step, no list — plus the confirmation that it
/// was, and a count of the messages the conversation holds.
/// <para>
/// The local mini-chat that used to sit under the field is gone. Three of them, one per
/// step, each starting empty and forgetting everything on the way to the next step, is the
/// shape <see cref="ChatThreadViewModel"/> replaces with one thread that remembers. The
/// engine channel those questions travelled down is not lost: the thread still uses it
/// whenever a forge session is listening.
/// </para>
/// </summary>
public sealed class StepNotesViewModel : ObservableObject
{
    private readonly Func<string>? _messageCount;
    private string _consigne = "";

    /// <summary>Builds the block; the count reads the window's conversation.</summary>
    internal StepNotesViewModel(Func<string>? messageCount = null) => _messageCount = messageCount;

    /// <summary>The consigne, taken as typed. Empty means none.</summary>
    public string Consigne
    {
        get => _consigne;
        set
        {
            if (SetProperty(ref _consigne, value))
                OnPropertyChanged(nameof(HasConsigne));
        }
    }

    /// <summary>Whether a consigne stands.</summary>
    public bool HasConsigne => _consigne.Trim().Length > 0;

    /// <summary>«4 messages» — what the conversation holds; empty while it holds nothing.</summary>
    public string MessageCount => _messageCount?.Invoke() ?? "";

    /// <summary>Re-reads the count after the conversation moved.</summary>
    internal void RefreshMessageCount() => OnPropertyChanged(nameof(MessageCount));
}
