using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Forge;

/// <summary>One bubble of the conversation column.</summary>
public sealed class ForgeChatMessageViewModel(string role, string text)
{
    /// <summary>Wire role, <c>assistant</c> or <c>user</c>.</summary>
    public string Role { get; } = role;

    /// <summary>The turn's text.</summary>
    public string Text { get; } = text;

    /// <summary>True for the user's own bubbles (right-aligned).</summary>
    public bool IsUser { get; } = string.Equals(role, ForgeChatMessage.User, StringComparison.Ordinal);
}

/// <summary>
/// The conversation column (UX study §3): the single thread from start to finish, plus the
/// reply box. Sending goes through the owner — this view model never touches the process.
/// </summary>
public sealed class ConversationViewModel : ObservableObject
{
    private readonly Func<string, bool> _send;
    private string _inputText = "";
    private bool _canSend;

    /// <summary>Builds the column over the owner's send callback (true = the line went out).</summary>
    public ConversationViewModel(Func<string, bool> send)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        SendCommand = new RelayCommand(Send, () => CanSend && InputText.Trim().Length > 0);
    }

    /// <summary>The thread, oldest first.</summary>
    public ObservableCollection<ForgeChatMessageViewModel> Messages { get; } = [];

    /// <summary>The reply being typed.</summary>
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
                SendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether the engine is listening (a session is alive).</summary>
    public bool CanSend
    {
        get => _canSend;
        set
        {
            if (SetProperty(ref _canSend, value))
                SendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Sends the typed reply.</summary>
    public RelayCommand SendCommand { get; }

    /// <summary>Appends the projection's new messages (the collection only ever grows).</summary>
    public void Sync(IReadOnlyList<ForgeChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        for (var i = Messages.Count; i < messages.Count; i++)
            Messages.Add(new ForgeChatMessageViewModel(messages[i].Role, messages[i].Text));
    }

    /// <summary>Empties the thread for a fresh session.</summary>
    public void Clear() => Messages.Clear();

    private void Send()
    {
        var text = InputText.Trim();
        if (text.Length > 0 && _send(text))
            InputText = "";
    }
}
