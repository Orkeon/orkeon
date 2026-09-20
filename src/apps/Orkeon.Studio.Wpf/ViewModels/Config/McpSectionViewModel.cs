using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>MCP</c> form of the MCP tab (STUDIO-21, expert): the switch and one card per server.
/// Every keystroke writes in place through <see cref="McpSection"/>, so a key Studio does not
/// model survives, and each row says its own problem the way the validator will refuse the
/// save — no command for a stdio server, no usable URL for an HTTP one, an identifier the
/// binder would mangle or one another server already carries.
/// </summary>
public sealed class McpSectionViewModel : DocumentSectionViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Binds the form to the <c>MCP</c> section of the document.</summary>
    public McpSectionViewModel(Func<AppSettingsDocument> document, Action onChanged, IStudioStrings? strings = null)
        : base(document, onChanged)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        AddServerCommand = new RelayCommand(AddServer);
        RemoveServerCommand = new RelayCommand(
            parameter => { if (parameter is McpServerRowViewModel row) RemoveServer(row); },
            parameter => parameter is McpServerRowViewModel);
        _strings.CultureChanged += (_, _) =>
        {
            foreach (var row in Servers)
                row.RefreshProblem();
        };
        Reload();
    }

    private McpSection Section => Document.Mcp;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>The transports the runtime knows, for the row's list.</summary>
    public static IReadOnlyList<string> Transports => McpSection.Transports;

    /// <summary>The server rows, in document order.</summary>
    public ObservableCollection<McpServerRowViewModel> Servers { get; } = [];

    /// <summary>Whether at least one server is declared.</summary>
    public bool HasServers => Servers.Count > 0;

    /// <summary>
    /// Whether a run connects the declared servers. On is the runtime's default, so on removes
    /// the key and only off is written.
    /// </summary>
    public bool Enabled
    {
        get => Section.IsEnabled;
        set => SetValue(Enabled, value, v => Section.Enabled = v ? null : false);
    }

    /// <summary>Adds an empty stdio server under a fresh identifier.</summary>
    public RelayCommand AddServerCommand { get; }

    /// <summary>Removes the row given as parameter, and everything it wrote.</summary>
    public RelayCommand RemoveServerCommand { get; }

    /// <inheritdoc />
    public override void Refresh()
    {
        Reload();
        base.Refresh();
    }

    /// <inheritdoc />
    protected override void OnSectionChanged() => OnPropertiesChanged(nameof(Exists), nameof(HasServers), nameof(Enabled));

    private void Reload()
    {
        Servers.Clear();
        foreach (var id in Section.ServerIds)
        {
            if (Section.GetServer(id) is { } server)
                Servers.Add(new McpServerRowViewModel(server, this, _strings));
        }
    }

    private void AddServer()
    {
        var taken = Section.ServerIds.ToHashSet(StringComparer.Ordinal);
        var index = Servers.Count + 1;
        string id;
        do
        {
            id = string.Create(CultureInfo.InvariantCulture, $"server-{index++}");
        }
        while (taken.Contains(id));

        var definition = new McpServerDefinition { Id = id };
        Section.SetServer(definition);
        Servers.Add(new McpServerRowViewModel(definition, this, _strings));
        NotifyDocumentChanged();
    }

    private void RemoveServer(McpServerRowViewModel row)
    {
        Section.RemoveServer(row.Id);
        Servers.Remove(row);
        foreach (var other in Servers)
            other.RefreshProblem();
        NotifyDocumentChanged();
    }

    /// <summary>Whether another row than <paramref name="row"/> carries <paramref name="id"/>.</summary>
    internal bool IsTaken(McpServerRowViewModel row, string id) =>
        Servers.Any(other => !ReferenceEquals(other, row) && string.Equals(other.Id, id, StringComparison.Ordinal));

    /// <summary>Writes the row's five fields in place and marks the document.</summary>
    internal void Write(McpServerRowViewModel row)
    {
        Section.SetServer(row.ToDefinition());
        NotifyDocumentChanged();
    }

    /// <summary>Moves the row's node under its new identifier and marks the document.</summary>
    internal void Rename(string oldId, string newId)
    {
        Section.RenameServer(oldId, newId);
        foreach (var other in Servers)
            other.RefreshProblem();
        NotifyDocumentChanged();
    }
}

/// <summary>One MCP server card: the five fields as text, and the problem the row has, if any.</summary>
public sealed class McpServerRowViewModel : ObservableObject
{
    private readonly McpSectionViewModel _owner;
    private readonly IStudioStrings _strings;
    private string _id;
    private string _idText;
    private string _transport;
    private string _command;
    private string _argsText;
    private string _url;
    private string _envText;

    internal McpServerRowViewModel(McpServerDefinition server, McpSectionViewModel owner, IStudioStrings strings)
    {
        _owner = owner;
        _strings = strings;
        _id = server.Id;
        _idText = server.Id;
        _transport = McpSection.Transports.FirstOrDefault(t => string.Equals(t, server.Transport, StringComparison.OrdinalIgnoreCase))
            ?? server.Transport;
        _command = server.Command ?? "";
        _argsText = string.Join(Environment.NewLine, server.Args);
        _url = server.Url ?? "";
        _envText = string.Join(Environment.NewLine, server.Env.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>The identifier the document holds the server under.</summary>
    public string Id => _id;

    /// <summary>
    /// The identifier as typed. A usable, free identifier moves the server under it at once;
    /// an unusable or taken one stays on screen with its problem, and the document keeps the
    /// last good one.
    /// </summary>
    public string IdText
    {
        get => _idText;
        set
        {
            var text = value ?? "";
            if (!SetProperty(ref _idText, text))
                return;

            if (McpSection.IsValidServerId(text) && !_owner.IsTaken(this, text) && !string.Equals(text, _id, StringComparison.Ordinal))
            {
                var old = _id;
                _id = text;
                OnPropertyChanged(nameof(Id));
                _owner.Rename(old, text);
            }

            RefreshProblem();
        }
    }

    /// <summary>The transport, one of <see cref="McpSectionViewModel.Transports"/>.</summary>
    public string Transport
    {
        get => _transport;
        set
        {
            if (SetProperty(ref _transport, value ?? McpSection.StdioTransport))
            {
                OnPropertiesChanged(nameof(IsStdio), nameof(IsSse));
                _owner.Write(this);
                RefreshProblem();
            }
        }
    }

    /// <summary>Whether the transport is stdio: the command, arguments and environment apply.</summary>
    public bool IsStdio => string.Equals(_transport, McpSection.StdioTransport, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the transport is HTTP: the URL applies.</summary>
    public bool IsSse => string.Equals(_transport, McpSection.SseTransport, StringComparison.OrdinalIgnoreCase);

    /// <summary>The process to launch (stdio).</summary>
    public string Command
    {
        get => _command;
        set => Write(ref _command, value);
    }

    /// <summary>The arguments, one per line (stdio).</summary>
    public string ArgsText
    {
        get => _argsText;
        set => Write(ref _argsText, value);
    }

    /// <summary>The endpoint (HTTP).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056",
        Justification = "The field as typed, which may be an in-progress or malformed URL; the row and the validator report it.")]
    public string Url
    {
        get => _url;
        set => Write(ref _url, value);
    }

    /// <summary>The environment variables, NAME=value, one per line (stdio).</summary>
    public string EnvText
    {
        get => _envText;
        set => Write(ref _envText, value);
    }

    /// <summary>The row's problem, localized, or null when the server is complete.</summary>
    public string? Problem
    {
        get
        {
            if (!McpSection.IsValidServerId(_idText))
                return _strings[StudioStringKeys.McpProblemId];
            if (_owner.IsTaken(this, _idText))
                return _strings[StudioStringKeys.McpProblemDuplicate];
            if (IsStdio && string.IsNullOrWhiteSpace(_command))
                return _strings[StudioStringKeys.McpProblemCommand];
            if (IsSse && !(Uri.TryCreate(_url, UriKind.Absolute, out var url)
                && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)))
                return _strings[StudioStringKeys.McpProblemUrl];
            return null;
        }
    }

    /// <summary>Whether the row has a problem to show.</summary>
    public bool HasProblem => Problem is not null;

    internal void RefreshProblem() => OnPropertiesChanged(nameof(Problem), nameof(HasProblem));

    internal McpServerDefinition ToDefinition() => new()
    {
        Id = _id,
        Transport = _transport,
        Command = string.IsNullOrWhiteSpace(_command) ? null : _command.Trim(),
        Args = Lines(_argsText),
        Url = string.IsNullOrWhiteSpace(_url) ? null : _url.Trim(),
        Env = ParseEnv(_envText),
    };

    private void Write(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value ?? "", propertyName))
            return;

        _owner.Write(this);
        RefreshProblem();
    }

    private static IReadOnlyList<string> Lines(string text) =>
        [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static Dictionary<string, string> ParseEnv(string text)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(text))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            env[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return env;
    }
}
