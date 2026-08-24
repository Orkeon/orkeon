using System.Collections.ObjectModel;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>One question/answer exchange with the assistant, newest first in the thread.</summary>
public sealed class StepNotesExchange : ObservableObject
{
    private string? _answer;

    internal StepNotesExchange(string question) => Question = question;

    /// <summary>The user's question, verbatim.</summary>
    public string Question { get; }

    /// <summary>The assistant's reply; null while it is still owed.</summary>
    public string? Answer
    {
        get => _answer;
        internal set
        {
            if (SetProperty(ref _answer, value))
                OnPropertyChanged(nameof(HasAnswer));
        }
    }

    /// <summary>Whether the reply arrived.</summary>
    public bool HasAnswer => _answer is not null;
}

/// <summary>
/// The per-step "consigne + questions" block of the v3 wizard (the StepNotes component):
/// the field IS the consigne — typed text is taken as it stands, no add step, no list —
/// and the collapsible thread is a small conversation with the forge assistant. Questions
/// travel down the engine's ordinary <c>user.message</c> channel; the wizard routes the
/// next assistant turn back here.
/// </summary>
public sealed class StepNotesViewModel : ObservableObject
{
    private readonly Func<StepNotesViewModel, string, bool> _ask;
    private string _consigne = "";
    private string _questionDraft = "";
    private bool _isThreadOpen;
    private string? _notice;

    internal StepNotesViewModel(Func<StepNotesViewModel, string, bool> ask)
    {
        _ask = ask;
        AskCommand = new RelayCommand(Ask, () => _questionDraft.Trim().Length > 0);
        ToggleThreadCommand = new RelayCommand(() => IsThreadOpen = !IsThreadOpen);
        ClearCommand = new RelayCommand(
            () => { Items.Clear(); ClearCommand!.RaiseCanExecuteChanged(); },
            () => Items.Count > 0);
    }

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

    /// <summary>The question being typed.</summary>
    public string QuestionDraft
    {
        get => _questionDraft;
        set
        {
            if (SetProperty(ref _questionDraft, value))
            {
                AskCommand.RaiseCanExecuteChanged();
                Notice = null;
            }
        }
    }

    /// <summary>
    /// Why the last question could not leave (the assistant is not running). Null while
    /// everything is fine; cleared as soon as the user types again. A swallowed question
    /// with no feedback reads as a dead button — this is the feedback.
    /// </summary>
    public string? Notice
    {
        get => _notice;
        internal set => SetProperty(ref _notice, value);
    }

    /// <summary>Whether the question panel is unfolded.</summary>
    public bool IsThreadOpen
    {
        get => _isThreadOpen;
        set => SetProperty(ref _isThreadOpen, value);
    }

    /// <summary>The exchanges, newest first — the thread renders top-anchored to the latest.</summary>
    public ObservableCollection<StepNotesExchange> Items { get; } = [];

    /// <summary>Sends the typed question to the assistant.</summary>
    public RelayCommand AskCommand { get; }

    /// <summary>Folds/unfolds the question panel.</summary>
    public RelayCommand ToggleThreadCommand { get; }

    /// <summary>Empties the thread (the engine's transcript keeps its own copy).</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>Hands the assistant's turn to the oldest question still owed a reply.</summary>
    internal bool TryDeliverAnswer(string text)
    {
        var pending = Items.LastOrDefault(x => !x.HasAnswer);
        if (pending is null)
            return false;

        pending.Answer = text;
        return true;
    }

    private void Ask()
    {
        var question = _questionDraft.Trim();
        if (question.Length == 0 || !_ask(this, question))
            return;

        Items.Insert(0, new StepNotesExchange(question));
        QuestionDraft = "";
        ClearCommand.RaiseCanExecuteChanged();
    }
}
