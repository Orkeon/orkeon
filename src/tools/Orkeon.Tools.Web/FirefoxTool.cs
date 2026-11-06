using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Abstractions.Security;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web;

/// <summary>Strongly-typed request for <see cref="FirefoxTool"/>.</summary>
public sealed record FirefoxRequest
{
    /// <summary>Gets the action to perform.</summary>
    [FieldSchema(
        Description = "What to do: navigate (url), back, snapshot (the outline of the page: sections, links, controls, each with a node id), " +
            "read (the text of the page, or of node_id), find (nodes by kind and name, under node_id), click (node_id), type (node_id, text), " +
            "press (key), evaluate (script), screenshot (save_to_path), close",
        IsRequired = true,
        Enum = ["navigate", "back", "snapshot", "read", "find", "click", "type", "press", "evaluate", "screenshot", "close"])]
    public string Action { get; init; } = "";

    /// <summary>Gets the address <c>navigate</c> opens.</summary>
    [FieldSchema(Description = "navigate: the http or https address to open", IsRequired = false)]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "What the model wrote: the tool validates it, and says why when it is not an address.")]
    public string? Url { get; init; }

    /// <summary>Gets the id of the node an action applies to.</summary>
    [FieldSchema(Description = "A node id returned by snapshot or find (p1#12). click, type: the element; read: the node to read; find: search under this node", IsRequired = false)]
    public string? NodeId { get; init; }

    /// <summary>Gets the text <c>type</c> writes.</summary>
    [FieldSchema(Description = "type: the text that replaces what the element holds; a line feed presses Enter. Never a secret: the text goes through the conversation", IsRequired = false)]
    public string? Text { get; init; }

    /// <summary>Gets the key <c>press</c> presses.</summary>
    [FieldSchema(Description = "press: a key or a chord on the focused element - Enter, Tab, Escape, ArrowDown, PageDown, a, Control+a, Shift+Tab", IsRequired = false)]
    public string? Key { get; init; }

    /// <summary>Gets the kind of nodes <c>find</c> returns.</summary>
    [FieldSchema(Description = "find: only nodes of this kind", IsRequired = false, Enum = ["section", "link", "control"])]
    public string? Kind { get; init; }

    /// <summary>Gets the text the name of a node <c>find</c> returns holds.</summary>
    [FieldSchema(Description = "find: only nodes whose name holds this text, whatever the case", IsRequired = false)]
    public string? Name { get; init; }

    /// <summary>Gets the script <c>evaluate</c> runs.</summary>
    [FieldSchema(Description = "evaluate: a JavaScript expression run in the page; its value is returned, a promise is awaited", IsRequired = false)]
    public string? Script { get; init; }

    /// <summary>Gets the virtual path <c>screenshot</c> writes its image to.</summary>
    [FieldSchema(Description = "screenshot: the virtual path the PNG image of the visible part of the page is written to (/output/page.png)", IsRequired = false)]
    public string? SaveToPath { get; init; }

    /// <summary>Gets the most nodes a listing returns.</summary>
    [FieldSchema(Description = "snapshot, find: the most nodes to return (the default is set by the host, 200 at most)", IsRequired = false)]
    public int? MaxNodes { get; init; }

    /// <summary>Gets what a listing or a reading skips.</summary>
    [FieldSchema(Description = "snapshot, find: nodes to skip, to read the next part of a listing; read: characters to skip", IsRequired = false)]
    public int? Offset { get; init; }
}

/// <summary>Strongly-typed response for <see cref="FirefoxTool"/>.</summary>
public sealed record FirefoxResponse : IBoundedToolResponse
{
    /// <summary>Gets the action that was performed.</summary>
    [ReturnSchema(Description = "The action that was performed")]
    public string Action { get; init; } = "";

    /// <summary>Gets whether the action succeeded.</summary>
    [ReturnSchema(Description = "Whether the action succeeded")]
    public bool Success { get; init; }

    /// <summary>Gets why the action failed.</summary>
    [ReturnSchema(Description = "Why the action failed")]
    public string? Error { get; init; }

    /// <summary>Gets the address of the page shown after the action.</summary>
    [ReturnSchema(Description = "Address of the page shown after the action")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The address as the browser reports it, serialized for the model.")]
    public string? Url { get; init; }

    /// <summary>Gets the title of the page shown after the action.</summary>
    [ReturnSchema(Description = "Title of the page shown after the action")]
    public string? Title { get; init; }

    /// <summary>Gets whether more nodes or more text remain than what is returned.</summary>
    [ReturnSchema(Description = "True when more nodes or more text remain: ask again with offset")]
    public bool Truncated { get; init; }

    /// <summary>Gets how many nodes the listing holds in all.</summary>
    [ReturnSchema(Description = "Nodes the listing holds in all, returned or not")]
    public int? TotalNodes { get; init; }

    /// <summary>Gets the nodes of the page the action returns.</summary>
    [ReturnSchema(Description = "Nodes of the page: id, kind (section, link, control), role, name, parent_id (the section or region that holds it), text (the start of a section's text, the target of a link, the value of a control). " +
        "Returned by snapshot and find, and by any action that opened another page: earlier node ids no longer apply then")]
    public IReadOnlyList<FirefoxNode> Nodes { get; init; } = [];

    /// <summary>Gets the text the action read.</summary>
    [ReturnSchema(Description = "read: the text of the page or of the node")]
    public string? Text { get; init; }

    /// <summary>Gets the virtual path the screenshot was written to.</summary>
    [ReturnSchema(Description = "screenshot: the virtual path of the image written")]
    public string? SavedPath { get; init; }

    /// <summary>Gets the value the action produced.</summary>
    [ReturnSchema(Description = "evaluate: the value of the script, as text or JSON")]
    public string? Value { get; init; }

    /// <summary>Gets what the dialogs the page opened said.</summary>
    [ReturnSchema(Description = "What the dialogs the page opened said (alert, confirm, prompt); they were dismissed")]
    public IReadOnlyList<string> Dialogs { get; init; } = [];

    /// <summary>The tool bounds what it returns itself: the nodes by <c>max_nodes</c>, the text by the host's limit.</summary>
    [JsonIgnore]
    public bool AlreadyBounded => true;
}

/// <summary>One node of the semantic graph of a page: a section, a link or a control.</summary>
public sealed record FirefoxNode
{
    /// <summary>Gets the id of the node, the handle actions take.</summary>
    public string Id { get; init; } = "";

    /// <summary>Gets the kind of the node: <c>section</c>, <c>link</c> or <c>control</c>.</summary>
    public string Kind { get; init; } = "";

    /// <summary>Gets the role of the element: <c>h2</c>, <c>navigation</c>, <c>form</c>, <c>button</c>, <c>textbox</c>.</summary>
    public string Role { get; init; } = "";

    /// <summary>Gets the accessible name of the element.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the id of the section or region that holds the node; <c>null</c> at the top of the page.</summary>
    public string? ParentId { get; init; }

    /// <summary>Gets the start of a section's text, the target of a link, or the value of a control.</summary>
    public string Text { get; init; } = "";
}

/// <summary>
/// Drives a Firefox of its own: opens pages, reads them as a semantic graph whose node ids are the
/// handles to click and type, runs scripts and captures the screen. One browser per agent run, started
/// at the first action and closed with the run.
/// </summary>
[ToolContract("firefox",
    Name = "firefox",
    Description = "Drive a Firefox browser: open a page (navigate), read it as an outline of sections, links and controls (snapshot, find, read), " +
        "then act on a node by its id (click, type), press keys, run a script (evaluate) or save a screenshot. " +
        "The browser stays open between calls of the same run; close ends it.",
    Category = "Web Operations")]
public sealed partial class FirefoxTool : ToolBase<FirefoxRequest, FirefoxResponse>
{
    internal const string Navigate = "navigate";
    internal const string Back = "back";
    internal const string Snapshot = "snapshot";
    internal const string Read = "read";
    internal const string Find = "find";
    internal const string Click = "click";
    internal const string TypeText = "type";
    internal const string Press = "press";
    internal const string Evaluate = "evaluate";
    internal const string Screenshot = "screenshot";
    internal const string Close = "close";

    /// <summary>The most nodes one answer lists, whatever <c>max_nodes</c> asks.</summary>
    internal const int MaxNodesCap = 200;

    private const string Blank = "about:blank";
    private const int MaxNameLength = 100;
    private const int MaxNodeTextLength = 120;

    private static readonly string[] s_actions =
        [Navigate, Back, Snapshot, Read, Find, Click, TypeText, Press, Evaluate, Screenshot, Close];

    // The actions that only look at the web. Every other one, a missing or unknown one included, acts
    // on a page - or, for screenshot, writes a file.
    private static readonly HashSet<string> s_readActions = new(StringComparer.OrdinalIgnoreCase)
    {
        Navigate, Back, Snapshot, Read, Find,
    };

    private static readonly Dictionary<string, UniversalNodeKind> s_kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["section"] = UniversalNodeKind.Section,
        ["link"] = UniversalNodeKind.Link,
        ["control"] = UniversalNodeKind.Control,
    };

    private readonly FirefoxSessionManager _sessions;
    private readonly FirefoxToolOptions _options;
    private readonly IFileSystemService _fileSystem;
    private readonly IUrlValidator? _urlValidator;

    /// <summary>Initializes a new instance of <see cref="FirefoxTool"/> that starts the Firefox installed on the host.</summary>
    /// <param name="options">Where Firefox is, whether it shows a window, and the limits of an answer.</param>
    /// <param name="fileSystem">The virtual file system screenshots are written through.</param>
    /// <param name="urlValidator">The policy of the addresses the browser may open; the fail-closed default guard when <see langword="null"/>.</param>
    /// <param name="logger">Optional logger instance.</param>
    public FirefoxTool(
        FirefoxToolOptions options, IFileSystemService fileSystem, IUrlValidator? urlValidator = null, ILogger? logger = null)
        : this(new FirefoxProcessLauncher(options, logger), options, fileSystem, urlValidator, logger)
    {
    }

    internal FirefoxTool(
        IFirefoxLauncher launcher, FirefoxToolOptions options, IFileSystemService fileSystem,
        IUrlValidator? urlValidator = null, ILogger? logger = null, FirefoxSessionTimings? timings = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _options = options;
        _fileSystem = fileSystem;
        _urlValidator = urlValidator;
        _sessions = new FirefoxSessionManager(launcher, options, fileSystem, logger, timings);
    }

    /// <summary>
    /// The most restrictive class the tool can take (<c>click</c>, <c>type</c>, <c>evaluate</c>); a gate
    /// reads the class of each call from <see cref="AccessFor"/>.
    /// </summary>
    public override ToolAccess Access => ToolAccess.Execute;

    /// <summary>
    /// <see cref="ToolAccess.Read"/> for <c>navigate</c>, <c>back</c>, <c>snapshot</c>, <c>read</c> and
    /// <c>find</c>; <see cref="ToolAccess.Edit"/> for <c>screenshot</c>, which writes a file;
    /// <see cref="ToolAccess.Execute"/> for <c>click</c>, <c>type</c>, <c>press</c>, <c>evaluate</c>,
    /// <c>close</c> and any unknown or missing action (fail-closed).
    /// </summary>
    /// <param name="arguments">Arguments the model supplied for the call.</param>
    /// <returns>The access class of this call.</returns>
    public override ToolAccess AccessFor(IReadOnlyDictionary<string, object?> arguments)
    {
        var action = StringArgument(arguments, "action", out _)?.Trim();
        if (action is null)
            return ToolAccess.Execute;
        if (s_readActions.Contains(action))
            return ToolAccess.Read;
        return string.Equals(action, Screenshot, StringComparison.OrdinalIgnoreCase) ? ToolAccess.Edit : ToolAccess.Execute;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(FirefoxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var supported = string.Join(", ", s_actions);
        if (string.IsNullOrWhiteSpace(request.Action))
            return $"action is required. Supported actions: {supported}";

        var action = Canonical(request.Action);
        if (action is null)
            return $"Unsupported action: {request.Action}. Supported actions: {supported}";

        if (request.MaxNodes is <= 0)
            return "max_nodes must be greater than 0";
        if (request.Offset is < 0)
            return "offset must not be negative";
        if (!string.IsNullOrWhiteSpace(request.NodeId) && !PageSnapshot.TryParseId(request.NodeId.Trim(), out _, out _))
            return $"'{request.NodeId}' is not a node id: node ids come from snapshot and find (p1#12)";

        return action switch
        {
            Navigate => InvalidUrl(request.Url),
            Click => Missing(request.NodeId, "node_id", action),
            TypeText => Missing(request.NodeId, "node_id", action) ?? (request.Text is null ? "text is required for action 'type'" : null),
            Press => string.IsNullOrEmpty(request.Key)
                ? "key is required for action 'press'"
                : FirefoxKeys.TryParse(request.Key, out _) ? null : $"'{request.Key}' is not a key. {FirefoxKeys.Names}",
            Evaluate => Missing(request.Script, "script", action),
            Screenshot => Missing(request.SaveToPath, "save_to_path", action),
            Find => string.IsNullOrWhiteSpace(request.Kind) || s_kinds.ContainsKey(request.Kind.Trim())
                ? null
                : $"Unsupported kind: {request.Kind}. Supported kinds: section, link, control",
            _ => null,
        };
    }

    /// <inheritdoc />
    protected override async Task<FirefoxResponse> ExecuteTypedAsync(FirefoxRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var action = Canonical(request.Action) ?? request.Action;
        try
        {
            if (action == Close)
            {
                var closed = await _sessions.CloseAsync(cancellationToken).ConfigureAwait(false);
                return new FirefoxResponse { Action = action, Success = true, Value = closed ? "The browser was closed." : "No browser was open." };
            }

            if (action == Navigate)
            {
                // Refused before any browser starts: an address the policy forbids costs no Firefox.
                var url = request.Url!.Trim();
                if (await RefusalAsync(url, cancellationToken).ConfigureAwait(false) is { } refusal)
                    throw new FirefoxException($"'{url}' was not opened: {refusal}.");
            }

            return await _sessions
                .UseAsync((session, ct) => RunAsync(session, action, request, ct), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IExpectedToolError)
        {
            // What the agent can act on: the browser, when it is still there, was kept as it is.
            return new FirefoxResponse { Action = action, Success = false, Error = ex.Message };
        }
    }

    private static string? Canonical(string action) =>
        Array.Find(s_actions, known => string.Equals(known, action.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? Missing(string? value, string name, string action) =>
        string.IsNullOrWhiteSpace(value) ? $"{name} is required for action '{action}'" : null;

    private static string? InvalidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "url is required for action 'navigate'";

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                ? null
                : $"'{url}' is not an absolute http or https address";
    }

    private async Task<FirefoxResponse> RunAsync(
        FirefoxSession session, string action, FirefoxRequest request, CancellationToken cancellationToken)
    {
        // What the browser shows was left there by an earlier action - or by the page itself, since: it
        // is checked before anything is read from it or done in it.
        var state = action == Navigate ? null : await GuardAsync(session, cancellationToken).ConfigureAwait(false);
        string? value = null;
        switch (action)
        {
            case Navigate:
                await session.NavigateAsync(request.Url!.Trim(), cancellationToken).ConfigureAwait(false);
                state = null;
                break;
            case Back:
                await session.BackAsync(cancellationToken).ConfigureAwait(false);
                state = null;
                break;
            case Click:
                await session.ClickAsync(Known(session).SharedId(request.NodeId!.Trim()), cancellationToken).ConfigureAwait(false);
                state = null;
                break;
            case TypeText:
                await session.TypeAsync(Known(session).SharedId(request.NodeId!.Trim()), request.Text!, cancellationToken).ConfigureAwait(false);
                state = null;
                break;
            case Press:
                if (!FirefoxKeys.TryParse(request.Key, out var chord))
                    throw new FirefoxException($"'{request.Key}' is not a key. {FirefoxKeys.Names}");
                await session.PressAsync(chord, cancellationToken).ConfigureAwait(false);
                state = null;
                break;
            case Evaluate:
                value = Cut(await session.EvaluateAsync(request.Script!, cancellationToken).ConfigureAwait(false), _options.MaxTextLength);
                state = null;
                break;
        }

        // The action may have led elsewhere - a redirect, a link, a form, a script: where it landed is
        // checked before anything of that page is returned.
        state ??= await GuardAsync(session, cancellationToken).ConfigureAwait(false);
        var response = action switch
        {
            Snapshot => await ListAsync(session, request, fresh: true, cancellationToken).ConfigureAwait(false),
            Find => await ListAsync(session, request, fresh: false, cancellationToken).ConfigureAwait(false),
            Read => await ReadAsync(session, request, cancellationToken).ConfigureAwait(false),
            Screenshot => await CaptureAsync(session, request.SaveToPath!.Trim(), cancellationToken).ConfigureAwait(false),
            Navigate or Back => await ListAsync(session, new FirefoxRequest(), fresh: true, cancellationToken).ConfigureAwait(false),
            // An action in the page that opened another one: its outline comes with the answer, since
            // the node ids the agent holds name the page it left.
            _ when session.Page is null => await ListAsync(session, new FirefoxRequest(), fresh: true, cancellationToken).ConfigureAwait(false),
            _ => new FirefoxResponse(),
        };

        return response with
        {
            Action = action,
            Success = true,
            Url = state.Url,
            Title = state.Title,
            Value = value,
            Dialogs = session.TakeDialogs(),
        };
    }

    /// <summary>
    /// The page the browser shows, once its address passed the policy. A page that does not is left
    /// for a blank one, and the action fails.
    /// </summary>
    private async Task<PageState> GuardAsync(FirefoxSession session, CancellationToken cancellationToken)
    {
        var state = await session.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(state.Url, session.CheckedUrl, StringComparison.Ordinal))
            return state;

        var refusal = await RefusalAsync(state.Url, cancellationToken).ConfigureAwait(false);
        if (refusal is null)
        {
            session.CheckedUrl = state.Url;
            return state;
        }

        session.CheckedUrl = null;
        await session.NavigateAsync(Blank, cancellationToken).ConfigureAwait(false);
        throw new FirefoxException($"The browser reached '{state.Url}', which it may not show ({refusal}): the page was left for a blank one.");
    }

    /// <summary>Why <paramref name="url"/> may not be shown, or <c>null</c> when it may.</summary>
    private async Task<string?> RefusalAsync(string url, CancellationToken cancellationToken)
    {
        if (string.Equals(url, Blank, StringComparison.Ordinal))
            return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            return "only http and https pages are opened";
        }

        var checkedUrl = _urlValidator is not null
            ? await _urlValidator.ValidateUrlAsync(address, cancellationToken).ConfigureAwait(false)
            : await DefaultSsrfGuard.ValidateAsync(address, _logger, cancellationToken).ConfigureAwait(false);
        return checkedUrl.IsAllowed ? null : checkedUrl.DenialReason ?? "the address is refused by the URL policy";
    }

    /// <summary>The snapshot the node ids of a request refer to.</summary>
    private static PageSnapshot Known(FirefoxSession session) =>
        session.Page ?? throw new FirefoxException("The page changed since it was last read: call snapshot, then use the node ids it returns.");

    /// <summary>The snapshot of what the browser shows now: the last one while nothing was done since, a new one otherwise.</summary>
    private static async Task<PageSnapshot> CurrentAsync(FirefoxSession session, CancellationToken cancellationToken) =>
        session.PageIsCurrent ? session.Page! : await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Lists nodes of the page from its graph: all of them, or those <c>find</c> asks for. A snapshot
    /// reads the page again, except to give the next part of the listing it just gave.
    /// </summary>
    private async Task<FirefoxResponse> ListAsync(
        FirefoxSession session, FirefoxRequest request, bool fresh, CancellationToken cancellationToken)
    {
        var under = string.IsNullOrWhiteSpace(request.NodeId) ? null : request.NodeId.Trim();
        var offset = request.Offset ?? 0;
        PageSnapshot page;
        if (under is not null)
            page = Known(session);
        else if (fresh && (offset == 0 || !session.PageIsCurrent))
            page = await session.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        else
            page = await CurrentAsync(session, cancellationToken).ConfigureAwait(false);

        var root = under is null ? null : page.Node(under);
        var kinds = !string.IsNullOrWhiteSpace(request.Kind) && s_kinds.TryGetValue(request.Kind.Trim(), out var kind)
            ? (UniversalNodeKind[])[kind]
            : [.. s_kinds.Values];
        var found = await page.Store.QueryAsync(
            new NodeQuery { Kinds = [.. kinds], Take = FirefoxSession.MaxElements + 1 }, cancellationToken).ConfigureAwait(false);

        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var matching = found
            .Where(node => !node.Tags.ContainsKey(PageOutlineMapper.HiddenTag))
            .Where(node => root is null || IsUnder(page, node, root))
            .Where(node => name is null || node.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var most = Math.Min(request.MaxNodes ?? _options.MaxNodes, MaxNodesCap);
        var listed = matching.Skip(offset).Take(most).Select(node => Describe(node, page.RootId)).ToList();
        return new FirefoxResponse
        {
            TotalNodes = matching.Count,
            Nodes = listed,
            Truncated = offset + listed.Count < matching.Count || page.Truncated,
        };
    }

    /// <summary>The text of the page, or of one node with the nodes it holds.</summary>
    private async Task<FirefoxResponse> ReadAsync(FirefoxSession session, FirefoxRequest request, CancellationToken cancellationToken)
    {
        var offset = request.Offset ?? 0;
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            var page = await CurrentAsync(session, cancellationToken).ConfigureAwait(false);
            var (text, more) = Slice(page.Text, offset, _options.MaxTextLength);
            return new FirefoxResponse { Text = text, Truncated = more || page.TextTruncated };
        }

        var known = Known(session);
        var node = known.Node(request.NodeId.Trim());
        var children = await known.Store.GetChildrenAsync(node.Id, cancellationToken).ConfigureAwait(false);
        var shown = children.Where(child => !child.Tags.ContainsKey(PageOutlineMapper.HiddenTag)).ToList();
        var most = Math.Min(request.MaxNodes ?? _options.MaxNodes, MaxNodesCap);
        var (nodeText, moreText) = Slice(TextOf(node), offset, _options.MaxTextLength);
        return new FirefoxResponse
        {
            Text = nodeText,
            TotalNodes = shown.Count,
            Nodes = [.. shown.Take(most).Select(child => Describe(child, known.RootId))],
            Truncated = moreText || shown.Count > most,
        };
    }

    /// <summary>Writes the image of the visible part of the page through the virtual file system.</summary>
    private async Task<FirefoxResponse> CaptureAsync(FirefoxSession session, string saveToPath, CancellationToken cancellationToken)
    {
        // Checked before the capture: a path the agent may not write costs no screenshot.
        var target = _fileSystem.ResolveAndValidate(saveToPath, FileAccessRights.Write);
        if (!target.IsAllowed)
        {
            throw new FirefoxException(
                $"The screenshot was not written to '{saveToPath}': {target.DenialReason ?? "the path is refused by the file system policy"}.");
        }

        var image = await session.ScreenshotAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _fileSystem.WriteAllBytesAsync(saveToPath, image, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileAccessDeniedException)
        {
            throw new FirefoxException($"The screenshot was not written to '{saveToPath}': {ex.Message}", ex);
        }

        return new FirefoxResponse
        {
            SavedPath = saveToPath,
            Text = string.Create(CultureInfo.InvariantCulture, $"PNG image, {image.Length} bytes"),
        };
    }

    private static bool IsUnder(PageSnapshot page, RaggableNode node, RaggableNode root)
    {
        if (root.Id == page.RootId)
            return true;

        for (var parent = node.ParentId; parent is not null && parent != page.RootId; parent = page.Node(parent).ParentId)
        {
            if (parent == root.Id)
                return true;
        }

        return false;
    }

    private static FirefoxNode Describe(RaggableNode node, string rootId) => new()
    {
        Id = node.Id,
        Kind = node.Kind switch
        {
            UniversalNodeKind.Link => "link",
            UniversalNodeKind.Control => "control",
            _ => "section",
        },
        Role = node.Tags.TryGetValue(PageOutlineMapper.LevelTag, out var level)
            ? $"h{level}"
            : node.Tags.GetValueOrDefault(PageOutlineMapper.RoleTag, string.Empty),
        Name = Cut(node.Name, MaxNameLength),
        ParentId = node.ParentId == rootId ? null : node.ParentId,
        Text = Cut(TextOf(node), MaxNodeTextLength),
    };

    /// <summary>What a node says beside its name: where a link leads, else the text or the value it holds.</summary>
    private static string TextOf(RaggableNode node) =>
        node.Kind == UniversalNodeKind.Link ? node.Signature ?? string.Empty : node.SourceSnippet ?? string.Empty;

    private static string Cut(string text, int length) =>
        text.Length <= length ? text : string.Concat(text.AsSpan(0, length), "...");

    private static (string Text, bool More) Slice(string text, int offset, int length)
    {
        if (offset >= text.Length)
            return (string.Empty, false);

        var end = Math.Min(text.Length, offset + length);
        return (text[offset..end], end < text.Length);
    }
}
